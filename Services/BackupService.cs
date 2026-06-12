using Microsoft.Data.SqlClient;
using Dapper;
using Spectre.Console;

namespace EnovaMigrator.Services;

/// <summary>
/// Serwis do tworzenia backupów bazy danych przed migracją.
/// </summary>
public class BackupService
{
    private readonly string _connectionString;
    private readonly string _backupDirectory;

    public BackupService(string connectionString, string? backupDirectory = null)
    {
        _connectionString = connectionString;
        _backupDirectory = backupDirectory ?? GetDefaultBackupDirectory();
    }

    private static string GetDefaultBackupDirectory()
    {
        // Domyślnie katalog roboczy aplikacji
        return Path.Combine(Directory.GetCurrentDirectory(), "backups");
    }

    /// <summary>
    /// Tworzy backup bazy danych.
    /// </summary>
    /// <returns>Ścieżka do pliku backupu lub null jeśli błąd</returns>
    public async Task<string?> CreateBackupAsync(IProgress<string>? progress = null)
    {
        try
        {
            // Upewnij się że katalog istnieje
            Directory.CreateDirectory(_backupDirectory);

            // Pobierz nazwę bazy z connection string
            var builder = new SqlConnectionStringBuilder(_connectionString);
            var databaseName = builder.InitialCatalog;

            if (string.IsNullOrEmpty(databaseName))
            {
                throw new Exception("Nie można określić nazwy bazy danych z connection string");
            }

            // Nazwa pliku backupu z datą i godziną
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"{databaseName}_backup_{timestamp}.bak";
            var backupPath = Path.Combine(_backupDirectory, backupFileName);

            progress?.Report($"Tworzenie backupu bazy {databaseName}...");

            // Sprawdź czy mamy uprawnienia do backupu
            // Najpierw spróbuj backup do domyślnej lokalizacji SQL Server
            var sqlBackupPath = await GetSqlServerBackupPathAsync(databaseName, backupFileName);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Komenda BACKUP DATABASE
            var backupSql = $@"
                BACKUP DATABASE [{databaseName}]
                TO DISK = @BackupPath
                WITH FORMAT,
                     INIT,
                     NAME = @BackupName,
                     SKIP,
                     NOREWIND,
                     NOUNLOAD,
                     STATS = 10";

            progress?.Report($"Zapisywanie do: {sqlBackupPath}");

            await connection.ExecuteAsync(backupSql, new
            {
                BackupPath = sqlBackupPath,
                BackupName = $"Backup przed migracją - {timestamp}"
            }, commandTimeout: 600); // 10 minut timeout

            progress?.Report($"Backup utworzony: {sqlBackupPath}");

            // Zapisz informację o backupie do pliku lokalnego
            var backupInfoPath = Path.Combine(_backupDirectory, $"backup_info_{timestamp}.txt");
            await File.WriteAllTextAsync(backupInfoPath, $@"Backup bazy danych
==================
Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Baza: {databaseName}
Serwer: {builder.DataSource}
Plik backupu: {sqlBackupPath}
");

            return sqlBackupPath;
        }
        catch (Exception ex)
        {
            progress?.Report($"BŁĄD tworzenia backupu: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Pobiera domyślną ścieżkę backupów SQL Server.
    /// </summary>
    private async Task<string> GetSqlServerBackupPathAsync(string databaseName, string fileName)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Pobierz domyślną ścieżkę backupów z SQL Server
            var sql = @"
                SELECT SERVERPROPERTY('InstanceDefaultBackupPath') as BackupPath";

            var result = await connection.QueryFirstOrDefaultAsync<string>(sql);

            if (!string.IsNullOrEmpty(result))
            {
                return Path.Combine(result, fileName);
            }

            // Fallback - użyj ścieżki danych SQL Server
            var dataSql = @"
                SELECT physical_name
                FROM sys.master_files
                WHERE database_id = DB_ID(@DbName) AND type = 0";

            var dataPath = await connection.QueryFirstOrDefaultAsync<string>(dataSql, new { DbName = databaseName });

            if (!string.IsNullOrEmpty(dataPath))
            {
                var directory = Path.GetDirectoryName(dataPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    return Path.Combine(directory, fileName);
                }
            }

            // Ostateczny fallback
            return Path.Combine("C:\\Backup", fileName);
        }
        catch
        {
            return Path.Combine("C:\\Backup", fileName);
        }
    }

    /// <summary>
    /// Przywraca bazę danych z backupu.
    /// UWAGA: To wymaga wyłączności na bazie (single user mode).
    /// </summary>
    public async Task<bool> RestoreBackupAsync(string backupPath, IProgress<string>? progress = null)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(_connectionString);
            var databaseName = builder.InitialCatalog;

            if (string.IsNullOrEmpty(databaseName))
            {
                throw new Exception("Nie można określić nazwy bazy danych z connection string");
            }

            progress?.Report($"Przywracanie bazy {databaseName} z {backupPath}...");

            // Połącz z master aby móc przywrócić bazę
            builder.InitialCatalog = "master";
            using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            // Ustaw bazę w tryb single user i zamknij połączenia
            progress?.Report("Zamykanie połączeń do bazy...");
            var killSql = $@"
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE";

            try
            {
                await connection.ExecuteAsync(killSql, commandTimeout: 60);
            }
            catch
            {
                // Ignoruj jeśli baza nie istnieje lub nie można zmienić trybu
            }

            // Przywróć bazę
            progress?.Report("Przywracanie danych...");
            var restoreSql = $@"
                RESTORE DATABASE [{databaseName}]
                FROM DISK = @BackupPath
                WITH REPLACE, RECOVERY";

            await connection.ExecuteAsync(restoreSql, new { BackupPath = backupPath }, commandTimeout: 600);

            // Przywróć tryb multi user
            var multiUserSql = $@"
                ALTER DATABASE [{databaseName}] SET MULTI_USER";

            await connection.ExecuteAsync(multiUserSql, commandTimeout: 60);

            progress?.Report("Baza przywrócona pomyślnie!");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"BŁĄD przywracania: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Listuje dostępne backupy w katalogu.
    /// </summary>
    public IEnumerable<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(_backupDirectory))
        {
            yield break;
        }

        foreach (var infoFile in Directory.GetFiles(_backupDirectory, "backup_info_*.txt"))
        {
            var content = File.ReadAllText(infoFile);
            var lines = content.Split('\n');

            var info = new BackupInfo
            {
                InfoFilePath = infoFile,
                Timestamp = File.GetCreationTime(infoFile)
            };

            foreach (var line in lines)
            {
                if (line.StartsWith("Baza:"))
                    info.DatabaseName = line.Replace("Baza:", "").Trim();
                else if (line.StartsWith("Plik backupu:"))
                    info.BackupFilePath = line.Replace("Plik backupu:", "").Trim();
                else if (line.StartsWith("Serwer:"))
                    info.ServerName = line.Replace("Serwer:", "").Trim();
            }

            yield return info;
        }
    }

    /// <summary>
    /// Sprawdza czy backup jest możliwy (uprawnienia).
    /// </summary>
    public async Task<(bool CanBackup, string Message)> CheckBackupPermissionsAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Sprawdź czy użytkownik ma uprawnienia do backupu
            var sql = @"
                SELECT IS_SRVROLEMEMBER('sysadmin') as IsSysAdmin,
                       IS_SRVROLEMEMBER('db_backupoperator') as IsBackupOperator,
                       HAS_PERMS_BY_NAME(NULL, NULL, 'BACKUP DATABASE') as HasBackupPerm";

            var result = await connection.QueryFirstOrDefaultAsync<dynamic>(sql);

            if (result == null)
            {
                return (false, "Nie można sprawdzić uprawnień");
            }

            var dict = (IDictionary<string, object>)result;
            var isSysAdmin = Convert.ToInt32(dict["IsSysAdmin"] ?? 0) == 1;
            var isBackupOp = Convert.ToInt32(dict["IsBackupOperator"] ?? 0) == 1;
            var hasBackupPerm = Convert.ToInt32(dict["HasBackupPerm"] ?? 0) == 1;

            if (isSysAdmin || isBackupOp || hasBackupPerm)
            {
                return (true, "Uprawnienia do backupu: OK");
            }

            return (false, "Brak uprawnień do wykonania backupu bazy danych. Wymagana rola: sysadmin, db_backupoperator lub uprawnienie BACKUP DATABASE.");
        }
        catch (Exception ex)
        {
            return (false, $"Błąd sprawdzania uprawnień: {ex.Message}");
        }
    }
}

public class BackupInfo
{
    public string? DatabaseName { get; set; }
    public string? ServerName { get; set; }
    public string? BackupFilePath { get; set; }
    public string? InfoFilePath { get; set; }
    public DateTime Timestamp { get; set; }

    public override string ToString()
    {
        return $"{Timestamp:yyyy-MM-dd HH:mm} - {DatabaseName} ({ServerName})";
    }
}
