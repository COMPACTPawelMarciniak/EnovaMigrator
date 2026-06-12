using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Dapper;
using EnovaMigrator.Configuration;
using EnovaMigrator.Models;
using Spectre.Console;

namespace EnovaMigrator.Services;

/// <summary>
/// Helper do budowania spójnych kluczy biznesowych z obsługą NULL
/// </summary>
public static class BusinessKeyHelper
{
    public static string FormatDate(DateTime? date) =>
        date.HasValue ? date.Value.ToString("yyyy-MM-dd") : "<NULL>";

    public static string FormatInt(int? value) =>
        value.HasValue ? value.Value.ToString() : "<NULL>";

    public static string FormatString(string? value) =>
        string.IsNullOrEmpty(value) ? "<EMPTY>" : value;

    public static string BuildKey(params object?[] parts)
    {
        var formatted = parts.Select(p => p switch
        {
            null => "<NULL>",
            DateTime dt => dt.ToString("yyyy-MM-dd"),
            string s when string.IsNullOrEmpty(s) => "<EMPTY>",
            _ => p.ToString() ?? "<NULL>"
        });
        return string.Join("|", formatted);
    }
}

public class MigrationService
{
    private readonly DatabaseService _sourceDb;
    private readonly DatabaseService _targetDb;
    private readonly MappingData _mapping;
    private readonly MigrationStats _stats = new();
    private readonly MigrationOptions _options;
    private readonly MigrationLogger _logger;
    private readonly AuditLogService? _auditLog;
    private readonly ProgressTracker _progressTracker = new();
    private MigrationState _state = new();
    private int _recordsSinceLastSave = 0;

    public MigrationService(DatabaseService sourceDb, DatabaseService targetDb, MappingData mapping, MigrationOptions? options = null, AuditLogService? auditLog = null)
    {
        _sourceDb = sourceDb;
        _targetDb = targetDb;
        _mapping = mapping;
        _options = options ?? new MigrationOptions();
        _logger = new MigrationLogger(_options.LogFilePath);
        _auditLog = auditLog;

        // Ustaw informacje o bazach w stanie
        _state.SourceDatabase = sourceDb.GetDatabaseName();
        _state.TargetDatabase = targetDb.GetDatabaseName();
    }

    public MigrationStats Stats => _stats;
    public MigrationState State => _state;
    public AuditLogService? AuditLog => _auditLog;
    public ProgressTracker ProgressTracker => _progressTracker;

    // Tabele do wyłączenia triggerów
    private static readonly string[] TablesToDisableTriggers = new[]
    {
        "Pracownicy", "Umowy", "ListyPlac", "Wyplaty", "WypElementy",
        "Nieobecnosci", "Rodzina", "Dodatki", "Adresy", "RachBankPodmiot",
        "PracHistorie", "Kalendarze", "HistZatrudnien"
    };

    private async Task DisableTriggersAsync(SqlTransaction? transaction)
    {
        foreach (var table in TablesToDisableTriggers)
        {
            try
            {
                var sql = $"ALTER TABLE [{table}] DISABLE TRIGGER ALL";
                if (transaction != null)
                    await transaction.Connection!.ExecuteAsync(sql, transaction: transaction);
                else
                    await _targetDb.ExecuteAsync(sql);
            }
            catch
            {
                // Ignoruj jeśli tabela nie ma triggerów lub nie istnieje
            }
        }
    }

    private async Task EnableTriggersAsync(SqlTransaction? transaction)
    {
        foreach (var table in TablesToDisableTriggers)
        {
            try
            {
                var sql = $"ALTER TABLE [{table}] ENABLE TRIGGER ALL";
                if (transaction != null)
                    await transaction.Connection!.ExecuteAsync(sql, transaction: transaction);
                else
                    await _targetDb.ExecuteAsync(sql);
            }
            catch
            {
                // Ignoruj jeśli tabela nie ma triggerów lub nie istnieje
            }
        }
    }

    /// <summary>
    /// Wczytuje stan migracji (dla trybu wznowienia lub przyrostowego)
    /// </summary>
    public async Task<bool> LoadStateAsync()
    {
        if (!_options.ResumeMode && !_options.IncrementalMode)
            return false;

        var loadedState = await MigrationState.LoadAsync(_options.StateFilePath);
        if (loadedState != null)
        {
            _state = loadedState;
            _logger.Log($"Wczytano stan migracji: {_state.GetSummary()}");

            // W trybie wznowienia, odtwórz mapowania z zapisanego stanu
            if (_options.ResumeMode)
            {
                RestoreMappingsFromState();
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Odtwarza mapowania ID z zapisanego stanu
    /// </summary>
    private void RestoreMappingsFromState()
    {
        // Przywróć mapowania Pracownicy
        if (_state.IdMappings.TryGetValue("Pracownicy", out var pracMappings))
        {
            foreach (var (sourceId, targetId) in pracMappings)
                _mapping.Pracownicy[sourceId] = targetId;
        }

        // Przywróć mapowania ListyPlac
        if (_state.IdMappings.TryGetValue("ListyPlac", out var lpMappings))
        {
            foreach (var (sourceId, targetId) in lpMappings)
                _mapping.ListyPlac[sourceId] = targetId;
        }

        // Przywróć mapowania Wyplaty
        if (_state.IdMappings.TryGetValue("Wyplaty", out var wypMappings))
        {
            foreach (var (sourceId, targetId) in wypMappings)
                _mapping.Wyplaty[sourceId] = targetId;
        }

        // Przywróć mapowania Umowy
        if (_state.IdMappings.TryGetValue("Umowy", out var umMappings))
        {
            foreach (var (sourceId, targetId) in umMappings)
                _mapping.Umowy[sourceId] = targetId;
        }

        _logger.Log($"Przywrócono mapowania z stanu: Pracownicy={_mapping.Pracownicy.Count}, ListyPlac={_mapping.ListyPlac.Count}, Wyplaty={_mapping.Wyplaty.Count}, Umowy={_mapping.Umowy.Count}");
    }

    /// <summary>
    /// Zapisuje stan migracji (wywoływane co N rekordów)
    /// </summary>
    private async Task SaveStateIfNeededAsync()
    {
        if (_options.DryRun) return;

        _recordsSinceLastSave++;
        if (_recordsSinceLastSave >= _options.SaveStateEveryNRecords)
        {
            await _state.SaveAsync(_options.StateFilePath);
            _recordsSinceLastSave = 0;
        }
    }

    /// <summary>
    /// Wymusza zapis stanu
    /// </summary>
    private async Task SaveStateAsync()
    {
        if (!_options.DryRun)
        {
            await _state.SaveAsync(_options.StateFilePath);
            _logger.Log($"Zapisano stan migracji do {_options.StateFilePath}");
        }
    }

    /// <summary>
    /// Sprawdza czy rekord powinien być pominięty (tryb przyrostowy/wznowienie)
    /// </summary>
    private bool ShouldSkipRecord(string tableName, int sourceId)
    {
        if (!_options.IncrementalMode && !_options.ResumeMode)
            return false;

        return _state.IsRecordMigrated(tableName, sourceId);
    }

    /// <summary>
    /// Waliduje czy wszystkie wymagane mapowania są dostępne przed migracją.
    /// Zwraca listę brakujących mapowań.
    /// </summary>
    public async Task<ValidationResult> ValidateMappingsAsync(IProgress<string>? progress = null)
    {
        var result = new ValidationResult();

        progress?.Report("Sprawdzanie mapowań dla list płac...");

        // Sprawdź ListyPlac -> DefListPlac
        var listyPlac = await _sourceDb.QueryAsync("SELECT DISTINCT Definicja FROM ListyPlac WHERE Definicja IS NOT NULL");
        foreach (var lp in listyPlac)
        {
            var dict = (IDictionary<string, object>)lp;
            var defId = Convert.ToInt32(dict["Definicja"]);
            if (!_mapping.DefListPlac.ContainsKey(defId))
                result.MissingDefListPlac.Add(defId);
        }

        progress?.Report("Sprawdzanie mapowań dla wypłat...");

        // Sprawdź Wyplaty -> Pracownicy
        var wyplaty = await _sourceDb.QueryAsync("SELECT DISTINCT Pracownik FROM Wyplaty WHERE Pracownik IS NOT NULL");
        foreach (var w in wyplaty)
        {
            var dict = (IDictionary<string, object>)w;
            var pracId = Convert.ToInt32(dict["Pracownik"]);
            if (!_mapping.Pracownicy.ContainsKey(pracId))
                result.MissingPracownicy.Add(pracId);
        }

        progress?.Report("Sprawdzanie mapowań dla elementów wypłat...");

        // Sprawdź WypElementy -> DefElementow
        var wypElementy = await _sourceDb.QueryAsync("SELECT DISTINCT Definicja FROM WypElementy WHERE Definicja IS NOT NULL");
        foreach (var we in wypElementy)
        {
            var dict = (IDictionary<string, object>)we;
            var defId = Convert.ToInt32(dict["Definicja"]);
            if (!_mapping.DefElementow.ContainsKey(defId))
                result.MissingDefElementow.Add(defId);
        }

        progress?.Report("Sprawdzanie mapowań dla nieobecności...");

        // Sprawdź Nieobecnosci -> DefNieobecnosci + Pracownicy (przez Zrodlo/ZrodloType)
        var nieobecnosci = await _sourceDb.QueryAsync(
            "SELECT DISTINCT Definicja, Zrodlo FROM Nieobecnosci WHERE Definicja IS NOT NULL AND Zrodlo IS NOT NULL AND ZrodloType LIKE 'Pracowni%'");
        foreach (var n in nieobecnosci)
        {
            var dict = (IDictionary<string, object>)n;
            var defId = Convert.ToInt32(dict["Definicja"]);
            var pracId = Convert.ToInt32(dict["Zrodlo"]);

            if (!_mapping.DefNieobecnosci.ContainsKey(defId))
                result.MissingDefNieobecnosci.Add(defId);
            if (!_mapping.Pracownicy.ContainsKey(pracId))
                result.MissingPracownicy.Add(pracId);
        }

        progress?.Report("Sprawdzanie mapowań dla umów...");

        // Sprawdź Umowy -> Pracownicy
        var umowy = await _sourceDb.QueryAsync("SELECT DISTINCT Pracownik FROM Umowy WHERE Pracownik IS NOT NULL");
        foreach (var u in umowy)
        {
            var dict = (IDictionary<string, object>)u;
            var pracId = Convert.ToInt32(dict["Pracownik"]);
            if (!_mapping.Pracownicy.ContainsKey(pracId))
                result.MissingPracownicy.Add(pracId);
        }

        progress?.Report("Walidacja zakończona.");

        return result;
    }

    /// <summary>
    /// Migruje pracowników ze źródła do celu.
    /// Pracownicy istniejący w target (po PESEL lub Imie|Nazwisko) są mapowani, nowi są tworzeni.
    /// </summary>
    private async Task MigratePracownicyAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        var sourceData = await _sourceDb.QueryAsync("SELECT * FROM Pracownicy ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("Pracownicy")).ToList();

        _stats.PracownicyTotal = sourceData.Count();
        _logger.Log($"Pracownicy: {_stats.PracownicyTotal} do przetworzenia");

        var count = sourceData.Count();
        var i = 0;
        foreach (var row in sourceData)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(1/13) Pracownicy: {i}/{count}");

            var dict = (IDictionary<string, object>)row;
            var sourceId = Convert.ToInt32(dict["ID"]);
            var pesel = dict["PESEL"]?.ToString();
            var imie = dict["Imie"]?.ToString() ?? "";
            var nazwisko = dict["Nazwisko"]?.ToString() ?? "";
            var kod = dict.ContainsKey("Kod") ? dict["Kod"]?.ToString() : null;

            // Sprawdź czy pracownik jest na liście pomijanych
            if (_mapping.SkipPracownicy.Contains(sourceId))
            {
                _stats.PracownicySkipped++;
                _logger.Log($"  Pracownik {imie} {nazwisko} (ID={sourceId}) -> POMINIĘTY (decyzja użytkownika)");
                continue;
            }

            // Sprawdź czy już zmigrowany (tryb przyrostowy/wznowienie)
            if (ShouldSkipRecord("Pracownicy", sourceId))
            {
                // Przywróć mapowanie ze stanu
                var targetId = _state.GetTargetId("Pracownicy", sourceId);
                if (targetId.HasValue)
                    _mapping.Pracownicy[sourceId] = targetId.Value;
                _stats.PracownicySkipped++;
                continue;
            }

            try
            {
                // Sprawdź czy pracownik już istnieje w target
                int? existingTargetId = null;

                // Sprawdź po Kod PIERWSZY (unique index Pracownicy_Podstawowy - najwyższy priorytet)
                if (!string.IsNullOrEmpty(kod) && existing.PracownicyKodToId.TryGetValue(kod, out var targetIdByKod))
                {
                    existingTargetId = targetIdByKod;
                }
                // Sprawdź po PESEL
                else if (!string.IsNullOrEmpty(pesel) && existing.PracownicyPeselToId.TryGetValue(pesel, out var targetIdByPesel))
                {
                    existingTargetId = targetIdByPesel;
                }
                // Sprawdź po Imie|Nazwisko
                else
                {
                    var nameKey = $"{BusinessKeyHelper.FormatString(imie)}|{BusinessKeyHelper.FormatString(nazwisko)}";
                    if (existing.PracownicyKeysToId.TryGetValue(nameKey, out var targetIdByName))
                    {
                        existingTargetId = targetIdByName;
                    }
                }

                if (existingTargetId.HasValue)
                {
                    // Pracownik już istnieje - tylko mapuj
                    _mapping.Pracownicy[sourceId] = existingTargetId.Value;
                    if (!string.IsNullOrEmpty(pesel))
                        _mapping.PracownicyByPesel[pesel] = existingTargetId.Value;
                    _stats.PracownicySkipped++;
                    _state.MarkRecordMigrated("Pracownicy", sourceId, existingTargetId.Value);
                    _auditLog?.LogSkipped("Pracownicy", sourceId, $"Już istnieje jako ID={existingTargetId.Value}");
                    _logger.Log($"  Pracownik {imie} {nazwisko} (ID={sourceId}) -> istniejący ID={existingTargetId.Value}");
                }
                else
                {
                    // Nowy pracownik - wstaw do target
                    int? newTargetId = await InsertPracownikAsync(row, columns, transaction);
                    if (newTargetId.HasValue)
                    {
                        _mapping.Pracownicy[sourceId] = newTargetId.Value;
                        if (!string.IsNullOrEmpty(pesel))
                            _mapping.PracownicyByPesel[pesel] = newTargetId.Value;
                        _stats.PracownicyMigrated++;
                        _state.MarkRecordMigrated("Pracownicy", sourceId, newTargetId.Value);
                        _auditLog?.LogSuccess("Pracownicy", sourceId, newTargetId.Value, $"{imie} {nazwisko}");
                        _logger.Log($"  Pracownik {imie} {nazwisko} (ID={sourceId}) -> NOWY ID={newTargetId.Value}");

                        // Dodaj do existing żeby kolejne iteracje też widział
                        if (!string.IsNullOrEmpty(pesel))
                        {
                            existing.PracownicyPesel.Add(pesel);
                            existing.PracownicyPeselToId[pesel] = newTargetId.Value;
                        }
                        if (!string.IsNullOrEmpty(kod))
                        {
                            existing.PracownicyKod.Add(kod);
                            existing.PracownicyKodToId[kod] = newTargetId.Value;
                        }
                        var newNameKey = $"{BusinessKeyHelper.FormatString(imie)}|{BusinessKeyHelper.FormatString(nazwisko)}";
                        existing.PracownicyKeys.Add(newNameKey);
                        existing.PracownicyKeysToId[newNameKey] = newTargetId.Value;
                    }
                }

                // Zapisz stan co N rekordów
                await SaveStateIfNeededAsync();
            }
            catch (Exception ex)
            {
                _stats.PracownicyErrors++;
                var errorMsg = $"Pracownik ID={sourceId} ({imie} {nazwisko}): {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _state.AddError("Pracownicy", sourceId, ex.Message);
                _auditLog?.LogError("Pracownicy", sourceId, ex.Message);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }

        // Aktualizuj statystyki tabeli w stanie
        _state.UpdateTableStats("Pracownicy", _stats.PracownicyTotal, _stats.PracownicyMigrated, _stats.PracownicySkipped, _stats.PracownicyErrors);
        _state.CurrentTable = "Umowy"; // Następna tabela
        await SaveStateAsync();

        _logger.Log($"Pracownicy: {_stats.PracownicyMigrated} nowych, {_stats.PracownicySkipped} istniejących, {_stats.PracownicyErrors} błędów");
    }

    private async Task<int?> InsertPracownikAsync(dynamic row, List<string> columns, SqlTransaction? transaction)
    {
        var dict = (IDictionary<string, object>)row;

        // DRY-RUN: nie wykonuj INSERT
        if (_options.DryRun)
        {
            _logger.Log($"  [DRY-RUN] Pracownicy: INSERT dla ID={dict["ID"]}");
            return (int?)Convert.ToInt32(dict["ID"]);
        }

        // Pobierz nowe ID dla tabeli docelowej (używaj transakcji jeśli dostępna)
        var newId = transaction != null
            ? await _targetDb.GetNextIdAsync("Pracownicy", transaction)
            : await _targetDb.GetNextIdAsync("Pracownicy");

        // Przygotuj kolumny i wartości
        var insertColumns = new List<string>();
        var insertParams = new List<string>();
        var parameters = new DynamicParameters();

        foreach (var col in columns)
        {
            var value = dict.ContainsKey(col) ? dict[col] : null;

            // Mapuj ID
            if (col == "ID")
            {
                value = newId;
            }
            // Generuj nowy GUID (unikaj duplikatów gdy rekord już był migrowany ze źródła)
            else if (col == "Guid")
            {
                value = Guid.NewGuid();
            }
            // Mapuj FK do kalendarzy wzorcowych
            else if (col == "Kalendarz" && value != null)
            {
                var oldKalId = Convert.ToInt32(value);
                if (_mapping.Kalendarze.TryGetValue(oldKalId, out var newKalId))
                    value = newKalId;
                else if (_mapping.SetNullKalendarze.Contains(oldKalId))
                    value = null; // SetNull - użytkownik wybrał ustawienie NULL
                // W przeciwnym razie kalendarze opcjonalne - zostawiamy oryginalne ID
            }
            // Mapuj FK do wydziałów (kolumna Wydzial lub EtatWydzial)
            else if ((col == "Wydzial" || col == "EtatWydzial") && value != null)
            {
                var oldWydId = Convert.ToInt32(value);
                if (_mapping.Wydzialy.TryGetValue(oldWydId, out var newWydId))
                    value = newWydId;
                else if (_mapping.SetNullWydzialy.Contains(oldWydId))
                    value = null; // SetNull - użytkownik wybrał ustawienie NULL
                // W przeciwnym razie wydziały opcjonalne - zostawiamy oryginalne ID
            }

            insertColumns.Add($"[{col}]");
            insertParams.Add($"@{col}");
            parameters.Add(col, value);
        }

        var sql = $@"
            SET IDENTITY_INSERT [Pracownicy] ON;
            INSERT INTO [Pracownicy] ({string.Join(", ", insertColumns)}) VALUES ({string.Join(", ", insertParams)});
            SET IDENTITY_INSERT [Pracownicy] OFF;";

        if (transaction != null)
        {
            await transaction.Connection!.ExecuteAsync(sql, parameters, transaction);
        }
        else
        {
            await _targetDb.ExecuteAsync(sql, parameters);
        }

        return newId;
    }

    /// <summary>
    /// Tworzy brakujące definicje w bazie docelowej (kopiuje ze źródłowej).
    /// Wywoływane PRZED właściwą migracją danych.
    /// </summary>
    public async Task CreateDefinitionsAsync(SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        _logger.Log("=== TWORZENIE DEFINICJI ===");

        // DefElementow
        if (_mapping.CreateDefElementow.Any())
        {
            progress?.Report($"Tworzenie {_mapping.CreateDefElementow.Count} definicji elementów...");
            await CreateDefinitionsForTableAsync("DefElementow", _mapping.CreateDefElementow, _mapping.DefElementow, transaction);
        }

        // DefNieobecnosci
        if (_mapping.CreateDefNieobecnosci.Any())
        {
            progress?.Report($"Tworzenie {_mapping.CreateDefNieobecnosci.Count} definicji nieobecności...");
            await CreateDefinitionsForTableAsync("DefNieobecnosci", _mapping.CreateDefNieobecnosci, _mapping.DefNieobecnosci, transaction);
        }

        // DefListPlac
        if (_mapping.CreateDefListPlac.Any())
        {
            progress?.Report($"Tworzenie {_mapping.CreateDefListPlac.Count} definicji list płac...");
            await CreateDefinitionsForTableAsync("DefListPlac", _mapping.CreateDefListPlac, _mapping.DefListPlac, transaction);
        }

        // DefDokumentow
        if (_mapping.CreateDefDokumentow.Any())
        {
            progress?.Report($"Tworzenie {_mapping.CreateDefDokumentow.Count} definicji dokumentów...");
            await CreateDefinitionsForTableAsync("DefDokumentow", _mapping.CreateDefDokumentow, _mapping.DefDokumentow, transaction);
        }

        // UrzedySkarbowe
        if (_mapping.CreateUrzedySkarbowe.Any())
        {
            progress?.Report($"Tworzenie {_mapping.CreateUrzedySkarbowe.Count} urzędów skarbowych...");
            await CreateDefinitionsForTableAsync("UrzedySkarbowe", _mapping.CreateUrzedySkarbowe, _mapping.UrzedySkarbowe, transaction);
        }

        // Wydzialy
        if (_mapping.CreateWydzialy.Any())
        {
            progress?.Report($"Tworzenie {_mapping.CreateWydzialy.Count} wydziałów...");
            await CreateDefinitionsForTableAsync("Wydzialy", _mapping.CreateWydzialy, _mapping.Wydzialy, transaction);
        }

        // Kalendarze (wzorcowe)
        if (_mapping.CreateKalendarze.Any())
        {
            progress?.Report($"Tworzenie {_mapping.CreateKalendarze.Count} kalendarzy...");
            await CreateDefinitionsForTableAsync("Kalendarze", _mapping.CreateKalendarze, _mapping.Kalendarze, transaction);
        }

        _logger.Log("=== KONIEC TWORZENIA DEFINICJI ===");
    }

    private async Task CreateDefinitionsForTableAsync(string tableName, HashSet<int> sourceIds, Dictionary<int, int> mapping, SqlTransaction? transaction)
    {
        if (!sourceIds.Any()) return;

        _logger.Log($"Tworzenie {sourceIds.Count} rekordów w {tableName}...");

        // Pobierz kolumny tabeli źródłowej
        var columns = (await _sourceDb.GetTableColumnsAsync(tableName)).ToList();

        foreach (var sourceId in sourceIds)
        {
            try
            {
                // Pobierz rekord ze źródła
                var sourceRows = await _sourceDb.QueryAsync($"SELECT * FROM [{tableName}] WHERE ID = @Id", new { Id = sourceId });
                var sourceRow = sourceRows.FirstOrDefault();

                if (sourceRow == null)
                {
                    _logger.Log($"  OSTRZEŻENIE: {tableName} ID={sourceId} nie istnieje w źródle - pomijam");
                    continue;
                }

                var dict = (IDictionary<string, object>)sourceRow;

                if (_options.DryRun)
                {
                    _logger.Log($"  [DRY-RUN] {tableName}: CREATE dla ID={sourceId}");
                    mapping[sourceId] = sourceId; // W dry-run mapuj na siebie
                    continue;
                }

                // Pobierz nowe ID dla tabeli docelowej
                var newId = transaction != null
                    ? await _targetDb.GetNextIdAsync(tableName, transaction)
                    : await _targetDb.GetNextIdAsync(tableName);

                // Przygotuj kolumny i wartości
                var insertColumns = new List<string>();
                var insertParams = new List<string>();
                var parameters = new DynamicParameters();

                foreach (var col in columns)
                {
                    var value = dict.ContainsKey(col) ? dict[col] : null;

                    // Mapuj ID
                    if (col == "ID")
                    {
                        value = newId;
                    }
                    // Generuj nowy GUID
                    else if (col == "Guid")
                    {
                        value = Guid.NewGuid();
                    }
                    // Mapuj FK do wydziałów (np. w DefListPlac)
                    else if (col == "Wydzial" && value != null)
                    {
                        var oldWydId = Convert.ToInt32(value);
                        if (_mapping.Wydzialy.TryGetValue(oldWydId, out var newWydId))
                            value = newWydId;
                        // Jeśli brak mapowania, zostaw NULL lub oryginalne ID
                    }

                    insertColumns.Add($"[{col}]");
                    insertParams.Add($"@{col}");
                    parameters.Add(col, value);
                }

                var sql = $@"
                    SET IDENTITY_INSERT [{tableName}] ON;
                    INSERT INTO [{tableName}] ({string.Join(", ", insertColumns)}) VALUES ({string.Join(", ", insertParams)});
                    SET IDENTITY_INSERT [{tableName}] OFF;";

                if (transaction != null)
                {
                    await transaction.Connection!.ExecuteAsync(sql, parameters, transaction);
                }
                else
                {
                    await _targetDb.ExecuteAsync(sql, parameters);
                }

                // Dodaj do mapowania
                mapping[sourceId] = newId;
                _logger.Log($"  {tableName}: source ID={sourceId} -> target ID={newId} (UTWORZONO)");
            }
            catch (Exception ex)
            {
                _logger.Log($"  BŁĄD: {tableName} ID={sourceId}: {ex.Message}");
                _stats.Errors.Add($"Create {tableName} ID={sourceId}: {ex.Message}");
            }
        }
    }

    public async Task<MigrationResult> MigrateAllAsync(IProgress<string>? progress = null)
    {
        var result = new MigrationResult();

        _logger.Log($"========================================");
        _logger.Log($"ROZPOCZECIE MIGRACJI: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _logger.Log($"Tryb: {(_options.DryRun ? "DRY-RUN (bez zmian)" : "PRODUKCYJNY")}");
        _logger.Log($"Transakcje: {(_options.UseTransaction ? "TAK" : "NIE")}");
        _logger.Log($"Tryb przyrostowy: {(_options.IncrementalMode ? "TAK" : "NIE")}");
        _logger.Log($"Wznowienie: {(_options.ResumeMode ? "TAK" : "NIE")}");
        _logger.Log($"========================================");

        SqlTransaction? transaction = null;

        try
        {
            // Wczytaj stan jeśli tryb wznowienia lub przyrostowy
            if (_options.ResumeMode || _options.IncrementalMode)
            {
                progress?.Report("Wczytywanie stanu poprzedniej migracji...");
                var stateLoaded = await LoadStateAsync();
                if (stateLoaded)
                {
                    progress?.Report($"Wczytano stan: {_state.GetSummary()}");
                    _logger.Log($"Wczytano stan migracji: {_state.GetSummary()}");
                }
                else
                {
                    _logger.Log("Brak zapisanego stanu - rozpoczynam od początku");
                }
            }

            // Pobierz istniejące rekordy
            progress?.Report("Pobieranie istniejących rekordów z bazy docelowej...");
            _logger.Log("Pobieranie istniejących rekordów...");
            var existing = await _targetDb.GetExistingRecordsAsync();

            // Rozpocznij transakcję jeśli włączona
            if (_options.UseTransaction && !_options.DryRun)
            {
                transaction = _targetDb.BeginTransaction();
                _logger.Log("Rozpoczęto transakcję SQL");
            }

            // Wyłącz triggery podczas migracji (enova365 ma dużo triggerów walidacyjnych)
            if (!_options.DryRun)
            {
                progress?.Report("Wyłączanie triggerów...");
                await DisableTriggersAsync(transaction);
                _logger.Log("Wyłączono triggery");
            }

            // 0. NAJPIERW utwórz brakujące definicje (wybrane przez użytkownika jako "Utwórz")
            progress?.Report("Tworzenie brakujących definicji...");
            await CreateDefinitionsAsync(transaction, progress);

            // 1. Migruj pracowników (MUSI BYĆ PIERWSZY - inne tabele zależą od mapowania pracowników)
            progress?.Report("(1/13) Migracja pracowników...");
            _auditLog?.StartTable("Pracownicy");
            await MigratePracownicyAsync(existing, transaction, progress);
            _auditLog?.EndTable("Pracownicy", _stats.PracownicyTotal, _stats.PracownicyMigrated, _stats.PracownicySkipped, _stats.PracownicyErrors);

            // 2. Migruj umowy (MUSI BYĆ PRZED WYPŁATAMI - trigger wymaga)
            progress?.Report("(2/13) Migracja umów...");
            _auditLog?.StartTable("Umowy");
            await MigrateUmowyAsync(existing, transaction, progress);
            _auditLog?.EndTable("Umowy", _stats.UmowyTotal, _stats.UmowyMigrated, _stats.UmowySkipped, _stats.UmowyErrors);

            // 3. Migruj listy płac
            progress?.Report("(3/13) Migracja list płac...");
            _auditLog?.StartTable("ListyPlac");
            await MigrateListyPlacAsync(existing, transaction, progress);
            _auditLog?.EndTable("ListyPlac", _stats.ListyPlacTotal, _stats.ListyPlacMigrated, _stats.ListyPlacSkipped, _stats.ListyPlacErrors);

            // 4. Migruj wypłaty
            progress?.Report("(4/13) Migracja wypłat...");
            _auditLog?.StartTable("Wyplaty");
            await MigrateWyplatyAsync(existing, transaction, progress);
            _auditLog?.EndTable("Wyplaty", _stats.WyplatyTotal, _stats.WyplatyMigrated, _stats.WyplatySkipped, _stats.WyplatyErrors);

            // 5. Migruj elementy wypłat
            progress?.Report("(5/13) Migracja elementów wypłat...");
            _auditLog?.StartTable("WypElementy");
            await MigrateWypElementyAsync(existing, transaction, progress);
            _auditLog?.EndTable("WypElementy", _stats.WypElementyTotal, _stats.WypElementyMigrated, _stats.WypElementySkipped, _stats.WypElementyErrors);

            // 6. Migruj rodziny (MUSI BYĆ PRZED NIEOBECNOŚCIAMI - FK CzlonekRodziny)
            progress?.Report("(6/13) Migracja członków rodzin...");
            _auditLog?.StartTable("Rodzina");
            await MigrateRodzinaAsync(existing, transaction, progress);
            _auditLog?.EndTable("Rodzina", _stats.RodzinaTotal, _stats.RodzinaMigrated, _stats.RodzinaSkipped, _stats.RodzinaErrors);

            // 7. Migruj nieobecności
            progress?.Report("(7/13) Migracja nieobecności...");
            _auditLog?.StartTable("Nieobecnosci");
            await MigrateNieobecnosciAsync(existing, transaction, progress);
            _auditLog?.EndTable("Nieobecnosci", _stats.NieobecnosciTotal, _stats.NieobecnosciMigrated, _stats.NieobecnosciSkipped, _stats.NieobecnosciErrors);

            // 8. Migruj dodatki
            progress?.Report("(8/13) Migracja dodatków...");
            _auditLog?.StartTable("Dodatki");
            await MigrateDodatkiAsync(existing, transaction, progress);
            _auditLog?.EndTable("Dodatki", _stats.DodatkiTotal, _stats.DodatkiMigrated, _stats.DodatkiSkipped, _stats.DodatkiErrors);

            // 9. Migruj adresy
            progress?.Report("(9/13) Migracja adresów...");
            _auditLog?.StartTable("Adresy");
            await MigrateAdresyAsync(existing, transaction, progress);
            _auditLog?.EndTable("Adresy", _stats.AdresyTotal, _stats.AdresyMigrated, _stats.AdresySkipped, _stats.AdresyErrors);

            // 10. Migruj rachunki bankowe
            progress?.Report("(10/13) Migracja rachunków bankowych...");
            _auditLog?.StartTable("RachBankPodmiot");
            await MigrateRachunkiAsync(existing, transaction, progress);
            _auditLog?.EndTable("RachBankPodmiot", _stats.RachunkiTotal, _stats.RachunkiMigrated, _stats.RachunkiSkipped, _stats.RachunkiErrors);

            // 11. Migruj historię kadrową
            progress?.Report("(11/13) Migracja historii kadrowej (PracHistorie)...");
            _auditLog?.StartTable("PracHistorie");
            await MigratePracHistorieAsync(existing, transaction, progress);
            _auditLog?.EndTable("PracHistorie", _stats.PracHistorieTotal, _stats.PracHistorieMigrated, _stats.PracHistorieSkipped, _stats.PracHistorieErrors);

            // 12. Migruj kalendarze pracowników
            progress?.Report("(12/13) Migracja kalendarzy pracowników...");
            _auditLog?.StartTable("Kalendarze");
            await MigrateKalendarzeAsync(existing, transaction, progress);
            _auditLog?.EndTable("Kalendarze", _stats.KalendarzeTotal, _stats.KalendarzeMigrated, _stats.KalendarzeSkipped, _stats.KalendarzeErrors);

            // 13. Migruj historię zatrudnień
            progress?.Report("(13/13) Migracja historii zatrudnień...");
            _auditLog?.StartTable("HistZatrudnien");
            await MigrateHistZatrudnienAsync(existing, transaction, progress);
            _auditLog?.EndTable("HistZatrudnien", _stats.HistZatrudnienTotal, _stats.HistZatrudnienMigrated, _stats.HistZatrudnienSkipped, _stats.HistZatrudnienErrors);

            // Włącz z powrotem triggery
            if (!_options.DryRun)
            {
                progress?.Report("Włączanie triggerów...");
                await EnableTriggersAsync(transaction);
                _logger.Log("Włączono triggery");
            }

            // Zatwierdź transakcję
            if (transaction != null)
            {
                transaction.Commit();
                _logger.Log("Transakcja zatwierdzona (COMMIT)");
            }

            // Oznacz migrację jako zakończoną
            _state.IsCompleted = true;
            _state.LastFullMigrationTimestamp = DateTime.Now;
            await SaveStateAsync();

            result.Success = true;
            result.Stats = _stats;
        }
        catch (Exception ex)
        {
            // Zapisz stan przed rollbackiem (dla możliwości wznowienia)
            _state.IsCompleted = false;
            await SaveStateAsync();

            // Wycofaj transakcję przy błędzie
            if (transaction != null)
            {
                try
                {
                    transaction.Rollback();
                    _logger.Log($"Transakcja wycofana (ROLLBACK) z powodu błędu: {ex.Message}");
                }
                catch (Exception rollbackEx)
                {
                    _logger.Log($"Błąd podczas ROLLBACK: {rollbackEx.Message}");
                }
            }

            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Stats = _stats;
            _logger.Log($"BŁĄD KRYTYCZNY: {ex.Message}");
            _logger.Log(ex.StackTrace ?? "");
        }
        finally
        {
            transaction?.Dispose();
        }

        // Podsumowanie
        _logger.Log($"========================================");
        _logger.Log($"ZAKONCZENIE MIGRACJI: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _logger.Log($"Status: {(result.Success ? "SUKCES" : "BŁĄD")}");
        _logger.LogStats(_stats);
        _logger.Log($"========================================");
        _logger.Flush();

        return result;
    }

    private async Task MigrateListyPlacAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        var sourceData = await _sourceDb.QueryAsync("SELECT * FROM ListyPlac ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("ListyPlac")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var numerPelny = dict["NumerPelny"]?.ToString();

            // Sprawdź po NumerPelny
            if (!string.IsNullOrEmpty(numerPelny) && existing.ListyPlacNumery.Contains(numerPelny))
                return false;

            // Sprawdź po kluczu biznesowym: Definicja|OkresFrom|OkresTo|Wydzial
            var sourceDefinicja = dict["Definicja"] != null ? Convert.ToInt32(dict["Definicja"]) : 0;
            var okresFrom = dict["OkresFrom"] as DateTime?;
            var okresTo = dict["OkresTo"] as DateTime?;
            var sourceWydzial = dict["Wydzial"] != null ? Convert.ToInt32(dict["Wydzial"]) : (int?)null;

            // Sprawdź czy definicja/wydział są na liście pomijanych
            if (_mapping.SkipDefListPlac.Contains(sourceDefinicja))
                return false; // użytkownik zdecydował pominąć tę definicję
            if (sourceWydzial.HasValue && _mapping.SkipWydzialy.Contains(sourceWydzial.Value))
                return false; // użytkownik zdecydował pominąć ten wydział

            // Sprawdź definicję - może być zmapowana lub SetNull
            int? targetDefinicja = null;
            if (_mapping.DefListPlac.TryGetValue(sourceDefinicja, out var mappedDef))
                targetDefinicja = mappedDef;
            else if (_mapping.SetNullDefListPlac.Contains(sourceDefinicja))
                targetDefinicja = null; // SetNull - FK będzie NULL, ale rekord migrujemy
            else if (sourceDefinicja > 0)
                return false; // brak mapowania i nie SetNull - pomijamy (jeśli była definicja)

            // Sprawdź wydział - może być zmapowany lub SetNull
            int? targetWydzial = null;
            if (sourceWydzial.HasValue)
            {
                if (_mapping.Wydzialy.TryGetValue(sourceWydzial.Value, out var mappedWyd))
                    targetWydzial = mappedWyd;
                else if (_mapping.SetNullWydzialy.Contains(sourceWydzial.Value))
                    targetWydzial = null; // SetNull - FK będzie NULL
                else
                    targetWydzial = sourceWydzial; // wydziały opcjonalne - zostawiamy oryginalne ID
            }

            var businessKey = BusinessKeyHelper.BuildKey(targetDefinicja, okresFrom, okresTo, targetWydzial);

            return !existing.ListyPlacKeys.Contains(businessKey);
        }).ToList();

        _stats.ListyPlacTotal = sourceData.Count();
        _stats.ListyPlacSkipped = _stats.ListyPlacTotal - toMigrate.Count;
        _logger.Log($"ListyPlac: {toMigrate.Count} do migracji z {_stats.ListyPlacTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(2/13) ListyPlac: {i}/{count}");
            try
            {
                int? sourceId = await InsertRowAsync("ListyPlac", row, columns, existing, transaction);
                if (sourceId.HasValue)
                {
                    var dict = (IDictionary<string, object>)row;
                    var oldId = (int)dict["ID"];
                    _mapping.ListyPlac[oldId] = sourceId.Value;
                    _stats.ListyPlacMigrated++;
                }
            }
            catch (Exception ex)
            {
                _stats.ListyPlacErrors++;
                var errorMsg = $"ListyPlac ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateWyplatyAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        var sourceData = await _sourceDb.QueryAsync("SELECT * FROM Wyplaty ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("Wyplaty")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var sourceListaPlac = dict["ListaPlac"] != null ? Convert.ToInt32(dict["ListaPlac"]) : 0;
            var sourcePracownik = dict["Pracownik"] != null ? Convert.ToInt32(dict["Pracownik"]) : 0;

            // Sprawdź czy mamy mapowania
            if (!_mapping.ListyPlac.TryGetValue(sourceListaPlac, out var targetListaPlac))
                return false; // brak mapowania listy płac - pomijamy
            if (!_mapping.Pracownicy.TryGetValue(sourcePracownik, out var targetPracownik))
                return false; // brak mapowania pracownika - pomijamy

            // Klucz biznesowy: ListaPlac|Pracownik (z target IDs)
            var businessKey = $"{targetListaPlac}|{targetPracownik}";

            return !existing.WyplatyKeys.Contains(businessKey);
        }).ToList();

        _stats.WyplatyTotal = sourceData.Count();
        _stats.WyplatySkipped = _stats.WyplatyTotal - toMigrate.Count;
        _logger.Log($"Wyplaty: {toMigrate.Count} do migracji z {_stats.WyplatyTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(3/13) Wyplaty: {i}/{count}");
            try
            {
                int? sourceId = await InsertRowAsync("Wyplaty", row, columns, existing, transaction);
                if (sourceId.HasValue)
                {
                    var dict = (IDictionary<string, object>)row;
                    var oldId = (int)dict["ID"];
                    _mapping.Wyplaty[oldId] = sourceId.Value;
                    _stats.WyplatyMigrated++;
                }
            }
            catch (Exception ex)
            {
                _stats.WyplatyErrors++;
                var errorMsg = $"Wyplaty ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateWypElementyAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        var sourceData = await _sourceDb.QueryAsync("SELECT * FROM WypElementy ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("WypElementy")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var sourceWyplata = dict["Wyplata"] != null ? Convert.ToInt32(dict["Wyplata"]) : 0;
            var sourceDefinicja = dict["Definicja"] != null ? Convert.ToInt32(dict["Definicja"]) : 0;
            var okresFrom = dict["OkresFrom"] as DateTime?;
            var okresTo = dict["OkresTo"] as DateTime?;

            // Sprawdź czy definicja jest na liście pomijanych
            if (_mapping.SkipDefElementow.Contains(sourceDefinicja))
                return false; // użytkownik zdecydował pominąć tę definicję

            // Sprawdź czy mamy mapowanie wypłaty
            if (!_mapping.Wyplaty.TryGetValue(sourceWyplata, out var targetWyplata))
                return false; // brak mapowania wypłaty - pomijamy

            // Sprawdź definicję - może być zmapowana lub SetNull
            int? targetDefinicja = null;
            if (_mapping.DefElementow.TryGetValue(sourceDefinicja, out var mappedDef))
                targetDefinicja = mappedDef;
            else if (_mapping.SetNullDefElementow.Contains(sourceDefinicja))
                targetDefinicja = null; // SetNull - FK będzie NULL, ale rekord migrujemy
            else
                return false; // brak mapowania i nie SetNull - pomijamy

            // Klucz biznesowy: Wyplata|Definicja|OkresFrom|OkresTo (z target IDs)
            var businessKey = BusinessKeyHelper.BuildKey(targetWyplata, targetDefinicja, okresFrom, okresTo);

            return !existing.WypElementyKeys.Contains(businessKey);
        }).ToList();

        _stats.WypElementyTotal = sourceData.Count();
        _stats.WypElementySkipped = _stats.WypElementyTotal - toMigrate.Count;
        _logger.Log($"WypElementy: {toMigrate.Count} do migracji z {_stats.WypElementyTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 50 == 0 || i == count) progress?.Report($"(4/13) WypElementy: {i}/{count}");
            try
            {
                await InsertRowAsync("WypElementy", row, columns, existing, transaction);
                _stats.WypElementyMigrated++;
            }
            catch (Exception ex)
            {
                _stats.WypElementyErrors++;
                var errorMsg = $"WypElementy ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateNieobecnosciAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        // Nieobecnosci używa Zrodlo/ZrodloType zamiast Pracownik (polimorficzny związek)
        var sourceData = await _sourceDb.QueryAsync("SELECT * FROM Nieobecnosci WHERE ZrodloType LIKE 'Pracowni%' ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("Nieobecnosci")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var sourcePracownik = dict["Zrodlo"] != null ? Convert.ToInt32(dict["Zrodlo"]) : 0;
            var sourceDefinicja = dict["Definicja"] != null ? Convert.ToInt32(dict["Definicja"]) : 0;
            var okresFrom = dict["OkresFrom"] as DateTime?;
            var okresTo = dict["OkresTo"] as DateTime?;

            // Sprawdź czy definicja jest na liście pomijanych
            if (_mapping.SkipDefNieobecnosci.Contains(sourceDefinicja))
                return false; // użytkownik zdecydował pominąć tę definicję

            // Sprawdź czy mamy mapowanie pracownika
            if (!_mapping.Pracownicy.TryGetValue(sourcePracownik, out var targetPracownik))
                return false; // brak mapowania pracownika - pomijamy

            // Sprawdź definicję - może być zmapowana lub SetNull
            int? targetDefinicja = null;
            if (_mapping.DefNieobecnosci.TryGetValue(sourceDefinicja, out var mappedDef))
                targetDefinicja = mappedDef;
            else if (_mapping.SetNullDefNieobecnosci.Contains(sourceDefinicja))
                targetDefinicja = null; // SetNull - FK będzie NULL, ale rekord migrujemy
            else
                return false; // brak mapowania i nie SetNull - pomijamy

            // Klucz biznesowy: Pracownik|Definicja|OkresFrom|OkresTo (z target IDs)
            var businessKey = BusinessKeyHelper.BuildKey(targetPracownik, targetDefinicja, okresFrom, okresTo);

            return !existing.NieobecnosciKeys.Contains(businessKey);
        }).ToList();

        _stats.NieobecnosciTotal = sourceData.Count();
        _stats.NieobecnosciSkipped = _stats.NieobecnosciTotal - toMigrate.Count;
        _logger.Log($"Nieobecnosci: {toMigrate.Count} do migracji z {_stats.NieobecnosciTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 20 == 0 || i == count) progress?.Report($"(7/13) Nieobecnosci: {i}/{count}");
            try
            {
                await InsertRowAsync("Nieobecnosci", row, columns, existing, transaction);
                _stats.NieobecnosciMigrated++;
            }
            catch (Exception ex)
            {
                _stats.NieobecnosciErrors++;
                var errorMsg = $"Nieobecnosci ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateUmowyAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        var sourceData = await _sourceDb.QueryAsync("SELECT * FROM Umowy ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("Umowy")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var sourceId = Convert.ToInt32(dict["ID"]);
            var sourcePracownik = dict["Pracownik"] != null ? Convert.ToInt32(dict["Pracownik"]) : 0;
            var sourceDefinicja = dict["Definicja"] != null ? Convert.ToInt32(dict["Definicja"]) : 0;
            var numerPelny = dict["NumerPelny"]?.ToString();
            var data = dict["Data"] as DateTime?;

            // Sprawdź czy umowa jest na liście pomijanych (duplikat)
            if (_mapping.SkipUmowy.Contains(sourceId))
                return false; // użytkownik zdecydował pominąć tę umowę

            // Sprawdź czy pracownik/definicja są na liście pomijanych
            if (_mapping.SkipPracownicy.Contains(sourcePracownik))
                return false; // użytkownik zdecydował pominąć tego pracownika
            if (sourceDefinicja > 0 && _mapping.SkipDefDokumentow.Contains(sourceDefinicja))
                return false; // użytkownik zdecydował pominąć tę definicję

            // Sprawdź czy mamy mapowanie pracownika
            if (!_mapping.Pracownicy.TryGetValue(sourcePracownik, out var targetPracownik))
                return false; // brak mapowania pracownika - pomijamy

            // Sprawdź globalny NumerPelny (unique index na całej tabeli)
            if (!string.IsNullOrEmpty(numerPelny))
            {
                if (existing.UmowyNumerPelnyGlobal.Contains(numerPelny))
                    return false; // umowa o tym numerze już istnieje globalnie
            }

            // Sprawdź po Pracownik|NumerPelny (dodatkowy check)
            if (!string.IsNullOrEmpty(numerPelny))
            {
                var numerKey = BusinessKeyHelper.BuildKey(targetPracownik, numerPelny);
                if (existing.UmowyNumery.Contains(numerKey))
                    return false;
            }

            // Sprawdź po Data
            var dataKey = BusinessKeyHelper.BuildKey(targetPracownik, data);
            return !existing.UmowyKeys.Contains(dataKey);
        }).ToList();

        _stats.UmowyTotal = sourceData.Count();
        _stats.UmowySkipped = _stats.UmowyTotal - toMigrate.Count;
        _logger.Log($"Umowy: {toMigrate.Count} do migracji z {_stats.UmowyTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(6/13) Umowy: {i}/{count}");
            try
            {
                int? newTargetId = await InsertRowAsync("Umowy", row, columns, existing, transaction);
                if (newTargetId.HasValue)
                {
                    var dict = (IDictionary<string, object>)row;
                    var oldId = (int)dict["ID"];
                    _mapping.Umowy[oldId] = newTargetId.Value;
                    _stats.UmowyMigrated++;

                    // Dodaj do existing żeby kolejne iteracje też widziały
                    var numerPelnyIns = dict["NumerPelny"]?.ToString();
                    if (!string.IsNullOrEmpty(numerPelnyIns))
                    {
                        existing.UmowyNumerPelnyGlobal.Add(numerPelnyIns);
                        existing.UmowyNumerPelnyGlobalToId[numerPelnyIns] = newTargetId.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _stats.UmowyErrors++;
                var errorMsg = $"Umowy ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateRodzinaAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        var sourceData = await _sourceDb.QueryAsync("SELECT * FROM Rodzina ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("Rodzina")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var sourcePracownik = dict["Pracownik"] != null ? Convert.ToInt32(dict["Pracownik"]) : 0;
            var pesel = dict["PESEL"]?.ToString();
            var imie = dict["Imie"]?.ToString();
            var nazwisko = dict["Nazwisko"]?.ToString();
            var dataUrodzenia = dict["DataUrodzenia"] as DateTime?;

            // Sprawdź czy mamy mapowanie pracownika
            if (!_mapping.Pracownicy.TryGetValue(sourcePracownik, out var targetPracownik))
                return false; // brak mapowania pracownika - pomijamy

            // Sprawdź po PESEL
            if (!string.IsNullOrEmpty(pesel))
            {
                var peselKey = BusinessKeyHelper.BuildKey(targetPracownik, pesel);
                if (existing.RodzinaPesel.Contains(peselKey))
                    return false;
            }

            // Sprawdź po Imie|Nazwisko|DataUrodzenia
            var nameKey = BusinessKeyHelper.BuildKey(targetPracownik, imie, nazwisko, dataUrodzenia);
            return !existing.RodzinaKeys.Contains(nameKey);
        }).ToList();

        _stats.RodzinaTotal = sourceData.Count();
        _stats.RodzinaSkipped = _stats.RodzinaTotal - toMigrate.Count;
        _logger.Log($"Rodzina: {toMigrate.Count} do migracji z {_stats.RodzinaTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(6/13) Rodzina: {i}/{count}");
            try
            {
                await InsertRowAsync("Rodzina", row, columns, existing, transaction);
                _stats.RodzinaMigrated++;
            }
            catch (Exception ex)
            {
                _stats.RodzinaErrors++;
                var errorMsg = $"Rodzina ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateDodatkiAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        var sourceData = await _sourceDb.QueryAsync("SELECT * FROM Dodatki ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("Dodatki")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var sourcePracownik = dict["Pracownik"] != null ? Convert.ToInt32(dict["Pracownik"]) : 0;
            var nazwa = dict["Nazwa"]?.ToString() ?? "";

            // Sprawdź czy mamy mapowanie pracownika
            if (!_mapping.Pracownicy.TryGetValue(sourcePracownik, out var targetPracownik))
                return false; // brak mapowania pracownika - pomijamy

            // Klucz biznesowy: Pracownik|Nazwa (z target ID pracownika)
            var businessKey = BusinessKeyHelper.BuildKey(targetPracownik, nazwa);

            return !existing.DodatkiKeys.Contains(businessKey);
        }).ToList();

        _stats.DodatkiTotal = sourceData.Count();
        _stats.DodatkiSkipped = _stats.DodatkiTotal - toMigrate.Count;
        _logger.Log($"Dodatki: {toMigrate.Count} do migracji z {_stats.DodatkiTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(8/13) Dodatki: {i}/{count}");
            try
            {
                await InsertRowAsync("Dodatki", row, columns, existing, transaction);
                _stats.DodatkiMigrated++;
            }
            catch (Exception ex)
            {
                _stats.DodatkiErrors++;
                var errorMsg = $"Dodatki ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateAdresyAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        // Pobierz tylko adresy pracowników
        var sourceData = await _sourceDb.QueryAsync(
            "SELECT * FROM Adresy WHERE HostType LIKE 'Pracowni%' ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("Adresy")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var host = Convert.ToInt32(dict["Host"]);
            var hostType = dict["HostType"]?.ToString() ?? "";
            var typ = dict["Typ"] != null ? Convert.ToInt32(dict["Typ"]) : (int?)null;

            // Sprawdź czy mamy mapowanie pracownika
            if (!_mapping.Pracownicy.ContainsKey(host))
                return false;

            // Utwórz klucz z nowym ID pracownika
            var newHost = _mapping.Pracownicy[host];
            var newKey = BusinessKeyHelper.BuildKey(newHost, hostType, typ);

            return !existing.AdresyKeys.Contains(newKey);
        }).ToList();

        _stats.AdresyTotal = sourceData.Count();
        _stats.AdresySkipped = _stats.AdresyTotal - toMigrate.Count;
        _logger.Log($"Adresy: {toMigrate.Count} do migracji z {_stats.AdresyTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(9/13) Adresy: {i}/{count}");
            try
            {
                await InsertRowAsync("Adresy", row, columns, existing, transaction);
                _stats.AdresyMigrated++;
            }
            catch (Exception ex)
            {
                _stats.AdresyErrors++;
                var errorMsg = $"Adresy ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateRachunkiAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        // Pobierz tylko rachunki pracowników
        var sourceData = await _sourceDb.QueryAsync(
            "SELECT * FROM RachBankPodmiot WHERE PodmiotType LIKE 'Pracowni%' ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("RachBankPodmiot")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var podmiot = Convert.ToInt32(dict["Podmiot"]);
            var podmiotType = dict["PodmiotType"]?.ToString() ?? "";
            var numer = dict["RachunekNumerNumer"]?.ToString() ?? "";

            // Sprawdź czy mamy mapowanie pracownika
            if (!_mapping.Pracownicy.ContainsKey(podmiot))
                return false;

            // Utwórz klucz z nowym ID pracownika
            var newPodmiot = _mapping.Pracownicy[podmiot];
            var newKey = BusinessKeyHelper.BuildKey(newPodmiot, podmiotType, numer);

            return !existing.RachunkiKeys.Contains(newKey);
        }).ToList();

        _stats.RachunkiTotal = sourceData.Count();
        _stats.RachunkiSkipped = _stats.RachunkiTotal - toMigrate.Count;
        _logger.Log($"RachBankPodmiot: {toMigrate.Count} do migracji z {_stats.RachunkiTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(10/13) RachBankPodmiot: {i}/{count}");
            try
            {
                await InsertRowAsync("RachBankPodmiot", row, columns, existing, transaction);
                _stats.RachunkiMigrated++;
            }
            catch (Exception ex)
            {
                _stats.RachunkiErrors++;
                var errorMsg = $"RachBankPodmiot ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigratePracHistorieAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        var sourceData = await _sourceDb.QueryAsync("SELECT * FROM PracHistorie ORDER BY Pracownik, AktualnoscFrom");
        // Używaj kolumn z TARGET - jeśli target nie ma danej kolumny, po prostu jej nie wstawiamy
        // Używaj transakcji jeśli dostępna, w przeciwnym razie użyj zwykłego połączenia
        var columns = transaction != null
            ? (await _targetDb.GetTableColumnsAsync("PracHistorie", transaction)).ToList()
            : (await _targetDb.GetTableColumnsAsync("PracHistorie")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var pracownik = Convert.ToInt32(dict["Pracownik"]);
            var aktualnoscFrom = dict["AktualnoscFrom"] as DateTime?;

            // Sprawdź czy mamy mapowanie pracownika
            if (!_mapping.Pracownicy.ContainsKey(pracownik))
                return false;

            // Utwórz klucz z nowym ID pracownika
            var newPracownik = _mapping.Pracownicy[pracownik];
            var key = BusinessKeyHelper.BuildKey(newPracownik, aktualnoscFrom);

            return !existing.PracHistorieKeys.Contains(key);
        }).ToList();

        _stats.PracHistorieTotal = sourceData.Count();
        _stats.PracHistorieSkipped = _stats.PracHistorieTotal - toMigrate.Count;
        _logger.Log($"PracHistorie: {toMigrate.Count} do migracji z {_stats.PracHistorieTotal} (tabela ma {columns.Count} kolumn)");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 20 == 0 || i == count) progress?.Report($"(11/13) PracHistorie: {i}/{count}");
            try
            {
                await InsertRowAsync("PracHistorie", row, columns, existing, transaction);
                _stats.PracHistorieMigrated++;
            }
            catch (Exception ex)
            {
                _stats.PracHistorieErrors++;
                var errorMsg = $"PracHistorie ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateKalendarzeAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        // Kalendarze przypisane do pracowników
        var sourceData = await _sourceDb.QueryAsync(
            "SELECT * FROM Kalendarze WHERE Pracownik IS NOT NULL ORDER BY ID");
        var columns = (await _sourceDb.GetTableColumnsAsync("Kalendarze")).ToList();

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var sourcePracownik = dict["Pracownik"] != null ? Convert.ToInt32(dict["Pracownik"]) : (int?)null;
            var nazwa = dict["Nazwa"]?.ToString() ?? "";
            var typ = dict.ContainsKey("Typ") && dict["Typ"] != null ? Convert.ToInt32(dict["Typ"]) : 0;

            // Sprawdź czy mamy mapowanie pracownika
            if (!sourcePracownik.HasValue || !_mapping.Pracownicy.TryGetValue(sourcePracownik.Value, out var targetPracownik))
                return false;

            // Klucz biznesowy: Typ|Nazwa (unique index Kalendarze_Podstawowy)
            var businessKey = $"{typ}|{(string.IsNullOrEmpty(nazwa) ? "<EMPTY>" : nazwa)}";
            return !existing.KalendarzeKeys.Contains(businessKey);
        }).ToList();

        _stats.KalendarzeTotal = sourceData.Count();
        _stats.KalendarzeSkipped = _stats.KalendarzeTotal - toMigrate.Count;
        _logger.Log($"Kalendarze: {toMigrate.Count} do migracji z {_stats.KalendarzeTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(12/13) Kalendarze: {i}/{count}");
            try
            {
                await InsertRowAsync("Kalendarze", row, columns, existing, transaction);
                _stats.KalendarzeMigrated++;
            }
            catch (Exception ex)
            {
                _stats.KalendarzeErrors++;
                var errorMsg = $"Kalendarze ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task MigrateHistZatrudnienAsync(ExistingRecords existing, SqlTransaction? transaction, IProgress<string>? progress = null)
    {
        IEnumerable<dynamic> sourceData;
        List<string> columns;

        try
        {
            sourceData = await _sourceDb.QueryAsync("SELECT * FROM HistZatrudnien ORDER BY Pracownik, DataOd");
            columns = (await _sourceDb.GetTableColumnsAsync("HistZatrudnien")).ToList();
        }
        catch
        {
            _logger.Log("HistZatrudnien: tabela nie istnieje lub brak dostępu - pomijam");
            return;
        }

        var toMigrate = sourceData.Where(row =>
        {
            var dict = (IDictionary<string, object>)row;
            var pracownik = Convert.ToInt32(dict["Pracownik"]);
            var dataOd = dict["DataOd"] as DateTime?;

            // Sprawdź czy mamy mapowanie pracownika
            if (!_mapping.Pracownicy.ContainsKey(pracownik))
                return false;

            // Utwórz klucz z nowym ID pracownika
            var newPracownik = _mapping.Pracownicy[pracownik];
            var key = BusinessKeyHelper.BuildKey(newPracownik, dataOd);

            return !existing.HistZatrudnienKeys.Contains(key);
        }).ToList();

        _stats.HistZatrudnienTotal = sourceData.Count();
        _stats.HistZatrudnienSkipped = _stats.HistZatrudnienTotal - toMigrate.Count;
        _logger.Log($"HistZatrudnien: {toMigrate.Count} do migracji z {_stats.HistZatrudnienTotal}");

        var count = toMigrate.Count;
        var i = 0;
        foreach (var row in toMigrate)
        {
            i++;
            if (i % 10 == 0 || i == count) progress?.Report($"(13/13) HistZatrudnien: {i}/{count}");
            try
            {
                await InsertRowAsync("HistZatrudnien", row, columns, existing, transaction);
                _stats.HistZatrudnienMigrated++;
            }
            catch (Exception ex)
            {
                _stats.HistZatrudnienErrors++;
                var errorMsg = $"HistZatrudnien ID {((IDictionary<string, object>)row)["ID"]}: {ex.Message}";
                _stats.Errors.Add(errorMsg);
                _logger.Log($"BŁĄD: {errorMsg}");
            }
        }
    }

    private async Task<int?> InsertRowAsync(string tableName, dynamic row, List<string> columns,
        ExistingRecords existing, SqlTransaction? transaction)
    {
        var dict = (IDictionary<string, object>)row;

        // DRY-RUN: nie wykonuj INSERT
        if (_options.DryRun)
        {
            _logger.Log($"  [DRY-RUN] {tableName}: INSERT dla ID={dict["ID"]}");
            return (int?)Convert.ToInt32(dict["ID"]);
        }

        // Pobierz nowe ID dla tabeli docelowej (używaj transakcji jeśli dostępna)
        var newId = transaction != null
            ? await _targetDb.GetNextIdAsync(tableName, transaction)
            : await _targetDb.GetNextIdAsync(tableName);

        // Przygotuj kolumny i wartości
        var insertColumns = new List<string>();
        var insertParams = new List<string>();
        var parameters = new DynamicParameters();

        foreach (var col in columns)
        {
            // Pomijaj kolumnę Bank w RachBankPodmiot (FK do tabeli Banki której nie migrujemy)
            if (tableName == "RachBankPodmiot" && (col == "Bank" || col == "RachunekBank"))
                continue;

            var value = dict.ContainsKey(col) ? dict[col] : null;

            // Mapuj ID
            if (col == "ID")
            {
                value = newId;
            }
            // Generuj nowy GUID (unikaj duplikatów gdy rekord już był migrowany ze źródła)
            else if (col == "Guid")
            {
                value = Guid.NewGuid();
            }
            // Mapuj FK do pracowników
            else if (col == "Pracownik" && value != null)
            {
                var oldPracId = Convert.ToInt32(value);
                if (_mapping.Pracownicy.TryGetValue(oldPracId, out var newPracId))
                {
                    value = newPracId;
                }
                else
                {
                    throw new Exception($"Brak mapowania dla Pracownik ID={oldPracId}");
                }
            }
            // Mapuj FK do definicji elementów
            else if (col == "Definicja" && value != null && (tableName == "WypElementy" || tableName == "Nieobecnosci"))
            {
                var oldDefId = Convert.ToInt32(value);

                if (tableName == "WypElementy")
                {
                    if (_mapping.DefElementow.TryGetValue(oldDefId, out var newDefId))
                        value = newDefId;
                    else if (_mapping.SetNullDefElementow.Contains(oldDefId))
                        value = null; // SetNull - użytkownik wybrał ustawienie NULL
                    else
                        throw new Exception($"Brak mapowania dla DefElementow ID={oldDefId}");
                }
                else if (tableName == "Nieobecnosci")
                {
                    if (_mapping.DefNieobecnosci.TryGetValue(oldDefId, out var newDefId))
                        value = newDefId;
                    else if (_mapping.SetNullDefNieobecnosci.Contains(oldDefId))
                        value = null; // SetNull - użytkownik wybrał ustawienie NULL
                    else
                        throw new Exception($"Brak mapowania dla DefNieobecnosci ID={oldDefId}");
                }
            }
            // Mapuj FK do definicji dokumentów (w Umowy)
            else if (col == "Definicja" && value != null && tableName == "Umowy")
            {
                var oldDefId = Convert.ToInt32(value);
                if (_mapping.DefDokumentow.TryGetValue(oldDefId, out var newDefId))
                    value = newDefId;
                // DefDokumentow - jeśli brak mapowania, zostaw oryginalne ID (może istnieć w target)
            }
            // Mapuj FK do definicji list płac (w ListyPlac i DefElementow)
            else if ((col == "Definicja" || col == "DefListaPlac") && value != null && tableName == "ListyPlac")
            {
                var oldDefId = Convert.ToInt32(value);
                if (_mapping.DefListPlac.TryGetValue(oldDefId, out var newDefId))
                    value = newDefId;
                else if (_mapping.SetNullDefListPlac.Contains(oldDefId))
                    value = null; // SetNull - użytkownik wybrał ustawienie NULL
                else
                    throw new Exception($"Brak mapowania dla DefListPlac ID={oldDefId}");
            }
            // Mapuj FK do definicji list płac w DefElementow
            else if (col == "DefinicjaListyPlac" && value != null)
            {
                var oldDefId = Convert.ToInt32(value);
                if (_mapping.DefListPlac.TryGetValue(oldDefId, out var newDefId))
                    value = newDefId;
                else if (_mapping.SetNullDefListPlac.Contains(oldDefId))
                    value = null; // SetNull
                // Jeśli brak mapowania, zostaw oryginalne ID (może istnieć w target)
            }
            // Mapuj FK do wydziałów (kolumna Wydzial lub EtatWydzial)
            else if ((col == "Wydzial" || col == "EtatWydzial") && value != null)
            {
                var oldWydId = Convert.ToInt32(value);
                if (_mapping.Wydzialy.TryGetValue(oldWydId, out var newWydId))
                    value = newWydId;
                else if (_mapping.SetNullWydzialy.Contains(oldWydId))
                    value = null; // SetNull - użytkownik wybrał ustawienie NULL
                // W przeciwnym razie wydziały opcjonalne - zostawiamy oryginalne ID
            }
            // Mapuj FK do list płac
            else if (col == "ListaPlac" && value != null)
            {
                var oldLpId = Convert.ToInt32(value);
                if (_mapping.ListyPlac.TryGetValue(oldLpId, out var newLpId))
                    value = newLpId;
                else
                    throw new Exception($"Brak mapowania dla ListaPlac ID={oldLpId}");
            }
            // Mapuj FK do wypłat
            else if (col == "Wyplata" && value != null)
            {
                var oldWypId = Convert.ToInt32(value);
                if (_mapping.Wyplaty.TryGetValue(oldWypId, out var newWypId))
                    value = newWypId;
                else
                    throw new Exception($"Brak mapowania dla Wyplata ID={oldWypId}");
            }
            // Mapuj FK do umów
            else if (col == "Umowa" && value != null)
            {
                var oldUmowaId = Convert.ToInt32(value);
                if (_mapping.Umowy.TryGetValue(oldUmowaId, out var newUmowaId))
                    value = newUmowaId;
                // Umowy opcjonalne w niektórych kontekstach
            }
            // Mapuj FK do kalendarzy wzorcowych
            else if (col == "Kalendarz" && value != null)
            {
                var oldKalId = Convert.ToInt32(value);
                if (_mapping.Kalendarze.TryGetValue(oldKalId, out var newKalId))
                    value = newKalId;
                else if (_mapping.SetNullKalendarze.Contains(oldKalId))
                    value = null; // SetNull - użytkownik wybrał ustawienie NULL
                // W przeciwnym razie kalendarze opcjonalne - zostawiamy oryginalne ID
            }
            // Mapuj FK do definicji dokumentów
            else if (col == "DefDokumentu" && value != null)
            {
                var oldDefId = Convert.ToInt32(value);
                if (_mapping.DefDokumentow.TryGetValue(oldDefId, out var newDefId))
                    value = newDefId;
                else if (_mapping.SetNullDefDokumentow.Contains(oldDefId))
                    value = null; // SetNull - użytkownik wybrał ustawienie NULL
                // W przeciwnym razie DefDokumentow opcjonalne - zostawiamy oryginalne ID
            }
            // Mapuj FK do urzędów skarbowych
            else if (col == "PodatkiUrzadSkarbowy" && value != null)
            {
                var oldUsId = Convert.ToInt32(value);
                if (_mapping.UrzedySkarbowe.TryGetValue(oldUsId, out var newUsId))
                    value = newUsId;
                else if (_mapping.SetNullUrzedySkarbowe.Contains(oldUsId))
                    value = null; // SetNull - użytkownik wybrał ustawienie NULL
                else
                    value = null; // Brak mapowania - ustaw NULL zamiast rzucać błąd (domyślnie)
            }
            // Polimorficzne FK (Zrodlo, Host, Podmiot) - mapuj jeśli typ to Pracownik
            else if (col == "Zrodlo" && value != null && dict.ContainsKey("ZrodloType"))
            {
                var zrodloType = dict["ZrodloType"]?.ToString() ?? "";
                if (zrodloType.StartsWith("Pracowni"))
                {
                    var oldZrodloId = Convert.ToInt32(value);
                    if (_mapping.Pracownicy.TryGetValue(oldZrodloId, out var newZrodloId))
                        value = newZrodloId;
                    else
                        throw new Exception($"Brak mapowania dla Zrodlo (Pracownik) ID={oldZrodloId}");
                }
            }
            else if (col == "Host" && value != null && dict.ContainsKey("HostType"))
            {
                var hostType = dict["HostType"]?.ToString() ?? "";
                if (hostType.StartsWith("Pracowni"))
                {
                    var oldHostId = Convert.ToInt32(value);
                    if (_mapping.Pracownicy.TryGetValue(oldHostId, out var newHostId))
                        value = newHostId;
                    else
                        throw new Exception($"Brak mapowania dla Host (Pracownik) ID={oldHostId}");
                }
            }
            else if (col == "Podmiot" && value != null && dict.ContainsKey("PodmiotType"))
            {
                var podmiotType = dict["PodmiotType"]?.ToString() ?? "";
                if (podmiotType.StartsWith("Pracowni"))
                {
                    var oldPodmiotId = Convert.ToInt32(value);
                    if (_mapping.Pracownicy.TryGetValue(oldPodmiotId, out var newPodmiotId))
                        value = newPodmiotId;
                    else
                        throw new Exception($"Brak mapowania dla Podmiot (Pracownik) ID={oldPodmiotId}");
                }
            }

            insertColumns.Add($"[{col}]");
            insertParams.Add($"@{col}");
            parameters.Add(col, value);
        }

        var sql = $@"
            SET IDENTITY_INSERT [{tableName}] ON;
            INSERT INTO [{tableName}] ({string.Join(", ", insertColumns)}) VALUES ({string.Join(", ", insertParams)});
            SET IDENTITY_INSERT [{tableName}] OFF;";

        if (transaction != null)
        {
            await transaction.Connection!.ExecuteAsync(sql, parameters, transaction);
        }
        else
        {
            await _targetDb.ExecuteAsync(sql, parameters);
        }

        return newId;
    }
}

public class MigrationOptions
{
    public bool DryRun { get; set; } = false;
    public bool UseTransaction { get; set; } = true;
    public bool CreateBackupBeforeMigration { get; set; } = true;
    public string LogFilePath { get; set; } = $"migration_{DateTime.Now:yyyyMMdd_HHmmss}.log";
    public string? BackupFilePath { get; set; } // Ścieżka do utworzonego backupu

    // Migracja przyrostowa
    public bool IncrementalMode { get; set; } = false; // Tryb przyrostowy (pomijaj już zmigrowane)
    public bool ResumeMode { get; set; } = false; // Wznowienie przerwanej migracji
    public string StateFilePath { get; set; } = "migration_state.json";
    public int SaveStateEveryNRecords { get; set; } = 100; // Zapisuj stan co N rekordów
}

public class MigrationStats
{
    public int PracownicyTotal { get; set; }
    public int PracownicyMigrated { get; set; }
    public int PracownicySkipped { get; set; }
    public int PracownicyErrors { get; set; }

    public int ListyPlacTotal { get; set; }
    public int ListyPlacMigrated { get; set; }
    public int ListyPlacSkipped { get; set; }
    public int ListyPlacErrors { get; set; }

    public int WyplatyTotal { get; set; }
    public int WyplatyMigrated { get; set; }
    public int WyplatySkipped { get; set; }
    public int WyplatyErrors { get; set; }

    public int WypElementyTotal { get; set; }
    public int WypElementyMigrated { get; set; }
    public int WypElementySkipped { get; set; }
    public int WypElementyErrors { get; set; }

    public int NieobecnosciTotal { get; set; }
    public int NieobecnosciMigrated { get; set; }
    public int NieobecnosciSkipped { get; set; }
    public int NieobecnosciErrors { get; set; }

    public int UmowyTotal { get; set; }
    public int UmowyMigrated { get; set; }
    public int UmowySkipped { get; set; }
    public int UmowyErrors { get; set; }

    public int RodzinaTotal { get; set; }
    public int RodzinaMigrated { get; set; }
    public int RodzinaSkipped { get; set; }
    public int RodzinaErrors { get; set; }

    public int DodatkiTotal { get; set; }
    public int DodatkiMigrated { get; set; }
    public int DodatkiSkipped { get; set; }
    public int DodatkiErrors { get; set; }

    public int AdresyTotal { get; set; }
    public int AdresyMigrated { get; set; }
    public int AdresySkipped { get; set; }
    public int AdresyErrors { get; set; }

    public int RachunkiTotal { get; set; }
    public int RachunkiMigrated { get; set; }
    public int RachunkiSkipped { get; set; }
    public int RachunkiErrors { get; set; }

    public int PracHistorieTotal { get; set; }
    public int PracHistorieMigrated { get; set; }
    public int PracHistorieSkipped { get; set; }
    public int PracHistorieErrors { get; set; }

    public int KalendarzeTotal { get; set; }
    public int KalendarzeMigrated { get; set; }
    public int KalendarzeSkipped { get; set; }
    public int KalendarzeErrors { get; set; }

    public int HistZatrudnienTotal { get; set; }
    public int HistZatrudnienMigrated { get; set; }
    public int HistZatrudnienSkipped { get; set; }
    public int HistZatrudnienErrors { get; set; }

    public List<string> Errors { get; set; } = new();

    public int TotalMigrated => PracownicyMigrated + ListyPlacMigrated + WyplatyMigrated + WypElementyMigrated +
                                NieobecnosciMigrated + UmowyMigrated + RodzinaMigrated +
                                DodatkiMigrated + AdresyMigrated + RachunkiMigrated +
                                PracHistorieMigrated + KalendarzeMigrated + HistZatrudnienMigrated;

    public int TotalErrors => PracownicyErrors + ListyPlacErrors + WyplatyErrors + WypElementyErrors +
                             NieobecnosciErrors + UmowyErrors + RodzinaErrors +
                             DodatkiErrors + AdresyErrors + RachunkiErrors +
                             PracHistorieErrors + KalendarzeErrors + HistZatrudnienErrors;
}

public class MigrationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public MigrationStats Stats { get; set; } = new();
}

public class MigrationLogger
{
    private readonly string _filePath;
    private readonly StringBuilder _buffer = new();

    public MigrationLogger(string filePath)
    {
        _filePath = filePath;
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _buffer.AppendLine(line);
    }

    public void LogStats(MigrationStats stats)
    {
        Log("--- STATYSTYKI ---");
        Log($"Pracownicy: {stats.PracownicyMigrated} zmigr. / {stats.PracownicySkipped} pom. / {stats.PracownicyErrors} błędy");
        Log($"ListyPlac: {stats.ListyPlacMigrated} zmigr. / {stats.ListyPlacSkipped} pom. / {stats.ListyPlacErrors} błędy");
        Log($"Wyplaty: {stats.WyplatyMigrated} zmigr. / {stats.WyplatySkipped} pom. / {stats.WyplatyErrors} błędy");
        Log($"WypElementy: {stats.WypElementyMigrated} zmigr. / {stats.WypElementySkipped} pom. / {stats.WypElementyErrors} błędy");
        Log($"Nieobecnosci: {stats.NieobecnosciMigrated} zmigr. / {stats.NieobecnosciSkipped} pom. / {stats.NieobecnosciErrors} błędy");
        Log($"Umowy: {stats.UmowyMigrated} zmigr. / {stats.UmowySkipped} pom. / {stats.UmowyErrors} błędy");
        Log($"Rodzina: {stats.RodzinaMigrated} zmigr. / {stats.RodzinaSkipped} pom. / {stats.RodzinaErrors} błędy");
        Log($"Dodatki: {stats.DodatkiMigrated} zmigr. / {stats.DodatkiSkipped} pom. / {stats.DodatkiErrors} błędy");
        Log($"Adresy: {stats.AdresyMigrated} zmigr. / {stats.AdresySkipped} pom. / {stats.AdresyErrors} błędy");
        Log($"RachBankPodmiot: {stats.RachunkiMigrated} zmigr. / {stats.RachunkiSkipped} pom. / {stats.RachunkiErrors} błędy");
        Log($"PracHistorie: {stats.PracHistorieMigrated} zmigr. / {stats.PracHistorieSkipped} pom. / {stats.PracHistorieErrors} błędy");
        Log($"Kalendarze: {stats.KalendarzeMigrated} zmigr. / {stats.KalendarzeSkipped} pom. / {stats.KalendarzeErrors} błędy");
        Log($"HistZatrudnien: {stats.HistZatrudnienMigrated} zmigr. / {stats.HistZatrudnienSkipped} pom. / {stats.HistZatrudnienErrors} błędy");
        Log($"RAZEM: {stats.TotalMigrated} zmigrownych, {stats.TotalErrors} błędów");
    }

    public void Flush()
    {
        try
        {
            File.AppendAllText(_filePath, _buffer.ToString());
            _buffer.Clear();
        }
        catch
        {
            // Ignoruj błędy zapisu do pliku
        }
    }
}

public class ValidationResult
{
    public HashSet<int> MissingPracownicy { get; set; } = new();
    public HashSet<int> MissingDefElementow { get; set; } = new();
    public HashSet<int> MissingDefNieobecnosci { get; set; } = new();
    public HashSet<int> MissingDefListPlac { get; set; } = new();
    public HashSet<int> MissingWydzialy { get; set; } = new();

    public bool IsValid => !MissingPracownicy.Any() &&
                           !MissingDefElementow.Any() &&
                           !MissingDefNieobecnosci.Any() &&
                           !MissingDefListPlac.Any() &&
                           !MissingWydzialy.Any();

    public int TotalMissing => MissingPracownicy.Count +
                               MissingDefElementow.Count +
                               MissingDefNieobecnosci.Count +
                               MissingDefListPlac.Count +
                               MissingWydzialy.Count;

    public List<string> GetSummary()
    {
        var summary = new List<string>();

        if (MissingPracownicy.Any())
            summary.Add($"Brak mapowania dla {MissingPracownicy.Count} pracowników (ID: {string.Join(", ", MissingPracownicy.Take(5))}{(MissingPracownicy.Count > 5 ? "..." : "")})");

        if (MissingDefElementow.Any())
            summary.Add($"Brak mapowania dla {MissingDefElementow.Count} definicji elementów (ID: {string.Join(", ", MissingDefElementow.Take(5))}{(MissingDefElementow.Count > 5 ? "..." : "")})");

        if (MissingDefNieobecnosci.Any())
            summary.Add($"Brak mapowania dla {MissingDefNieobecnosci.Count} definicji nieobecności (ID: {string.Join(", ", MissingDefNieobecnosci.Take(5))}{(MissingDefNieobecnosci.Count > 5 ? "..." : "")})");

        if (MissingDefListPlac.Any())
            summary.Add($"Brak mapowania dla {MissingDefListPlac.Count} definicji list płac (ID: {string.Join(", ", MissingDefListPlac.Take(5))}{(MissingDefListPlac.Count > 5 ? "..." : "")})");

        if (MissingWydzialy.Any())
            summary.Add($"Brak mapowania dla {MissingWydzialy.Count} wydziałów (ID: {string.Join(", ", MissingWydzialy.Take(5))}{(MissingWydzialy.Count > 5 ? "..." : "")})");

        return summary;
    }
}
