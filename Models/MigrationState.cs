using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnovaMigrator.Models;

/// <summary>
/// Stan migracji - przechowuje informacje o postępie i zmigrowanych rekordach.
/// Umożliwia wznowienie przerwanej migracji oraz migrację przyrostową.
/// </summary>
public class MigrationState
{
    /// <summary>
    /// Data rozpoczęcia migracji
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Data ostatniej aktualizacji stanu
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Czy migracja została zakończona
    /// </summary>
    public bool IsCompleted { get; set; } = false;

    /// <summary>
    /// Nazwa bazy źródłowej
    /// </summary>
    public string? SourceDatabase { get; set; }

    /// <summary>
    /// Nazwa bazy docelowej
    /// </summary>
    public string? TargetDatabase { get; set; }

    /// <summary>
    /// Aktualnie przetwarzana tabela (dla wznowienia)
    /// </summary>
    public string? CurrentTable { get; set; }

    /// <summary>
    /// Ostatnie przetworzone ID w aktualnej tabeli
    /// </summary>
    public int LastProcessedId { get; set; } = 0;

    /// <summary>
    /// Zmigrowane rekordy per tabela: TableName -> HashSet<SourceId>
    /// </summary>
    public Dictionary<string, HashSet<int>> MigratedRecords { get; set; } = new();

    /// <summary>
    /// Mapowania ID: TableName -> (SourceId -> TargetId)
    /// </summary>
    public Dictionary<string, Dictionary<int, int>> IdMappings { get; set; } = new();

    /// <summary>
    /// Błędy per tabela: TableName -> List<(SourceId, ErrorMessage)>
    /// </summary>
    public Dictionary<string, List<MigrationError>> Errors { get; set; } = new();

    /// <summary>
    /// Statystyki per tabela
    /// </summary>
    public Dictionary<string, TableStats> TableStatistics { get; set; } = new();

    /// <summary>
    /// Znacznik czasu ostatniej pełnej migracji (dla delt)
    /// </summary>
    public DateTime? LastFullMigrationTimestamp { get; set; }

    /// <summary>
    /// Sprawdza czy rekord został już zmigrowany
    /// </summary>
    public bool IsRecordMigrated(string tableName, int sourceId)
    {
        return MigratedRecords.TryGetValue(tableName, out var records) && records.Contains(sourceId);
    }

    /// <summary>
    /// Oznacza rekord jako zmigrowany
    /// </summary>
    public void MarkRecordMigrated(string tableName, int sourceId, int targetId)
    {
        if (!MigratedRecords.ContainsKey(tableName))
            MigratedRecords[tableName] = new HashSet<int>();

        if (!IdMappings.ContainsKey(tableName))
            IdMappings[tableName] = new Dictionary<int, int>();

        MigratedRecords[tableName].Add(sourceId);
        IdMappings[tableName][sourceId] = targetId;
        LastProcessedId = sourceId;
        LastUpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// Dodaje błąd migracji
    /// </summary>
    public void AddError(string tableName, int sourceId, string errorMessage)
    {
        if (!Errors.ContainsKey(tableName))
            Errors[tableName] = new List<MigrationError>();

        Errors[tableName].Add(new MigrationError
        {
            SourceId = sourceId,
            Message = errorMessage,
            Timestamp = DateTime.Now
        });
    }

    /// <summary>
    /// Pobiera mapowanie ID dla tabeli
    /// </summary>
    public int? GetTargetId(string tableName, int sourceId)
    {
        if (IdMappings.TryGetValue(tableName, out var mappings) &&
            mappings.TryGetValue(sourceId, out var targetId))
        {
            return targetId;
        }
        return null;
    }

    /// <summary>
    /// Aktualizuje statystyki dla tabeli
    /// </summary>
    public void UpdateTableStats(string tableName, int total, int migrated, int skipped, int errors)
    {
        TableStatistics[tableName] = new TableStats
        {
            Total = total,
            Migrated = migrated,
            Skipped = skipped,
            Errors = errors,
            LastUpdated = DateTime.Now
        };
    }

    /// <summary>
    /// Zapisuje stan do pliku
    /// </summary>
    public async Task SaveAsync(string filePath)
    {
        LastUpdatedAt = DateTime.Now;
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(this, options);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Wczytuje stan z pliku
    /// </summary>
    public static async Task<MigrationState?> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<MigrationState>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Zwraca podsumowanie stanu
    /// </summary>
    public string GetSummary()
    {
        var totalMigrated = MigratedRecords.Values.Sum(r => r.Count);
        var totalErrors = Errors.Values.Sum(e => e.Count);
        var status = IsCompleted ? "ZAKONCZONA" : $"W TRAKCIE ({CurrentTable})";

        return $"Stan: {status}, Zmigrowano: {totalMigrated}, Błędów: {totalErrors}, " +
               $"Rozpoczęto: {StartedAt:yyyy-MM-dd HH:mm}, Ostatnia aktualizacja: {LastUpdatedAt:yyyy-MM-dd HH:mm}";
    }
}

public class MigrationError
{
    public int SourceId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class TableStats
{
    public int Total { get; set; }
    public int Migrated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public DateTime LastUpdated { get; set; }
}
