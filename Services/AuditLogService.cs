using System.Text;
using System.Globalization;

namespace EnovaMigrator.Services;

/// <summary>
/// Rozszerzony audit log z eksportem do CSV i szczegółowymi informacjami o migracji.
/// </summary>
public class AuditLogService
{
    private readonly string _baseFileName;
    private readonly List<AuditEntry> _entries = new();
    private readonly Dictionary<string, TableAudit> _tableAudits = new();
    private DateTime _startTime;
    private DateTime _endTime;

    public AuditLogService(string? baseFileName = null)
    {
        _baseFileName = baseFileName ?? $"audit_{DateTime.Now:yyyyMMdd_HHmmss}";
        _startTime = DateTime.Now;
    }

    /// <summary>
    /// Rozpoczyna audit dla tabeli.
    /// </summary>
    public void StartTable(string tableName)
    {
        _tableAudits[tableName] = new TableAudit
        {
            TableName = tableName,
            StartTime = DateTime.Now
        };
    }

    /// <summary>
    /// Kończy audit dla tabeli.
    /// </summary>
    public void EndTable(string tableName, int total, int migrated, int skipped, int errors)
    {
        if (_tableAudits.TryGetValue(tableName, out var audit))
        {
            audit.EndTime = DateTime.Now;
            audit.TotalRecords = total;
            audit.MigratedRecords = migrated;
            audit.SkippedRecords = skipped;
            audit.ErrorRecords = errors;
        }
    }

    /// <summary>
    /// Loguje sukces migracji rekordu.
    /// </summary>
    public void LogSuccess(string tableName, int sourceId, int targetId, string? description = null)
    {
        _entries.Add(new AuditEntry
        {
            Timestamp = DateTime.Now,
            TableName = tableName,
            SourceId = sourceId,
            TargetId = targetId,
            Status = AuditStatus.Success,
            Description = description ?? "Zmigrowano"
        });

        if (_tableAudits.TryGetValue(tableName, out var audit))
        {
            audit.Mappings[sourceId] = targetId;
        }
    }

    /// <summary>
    /// Loguje pominięcie rekordu.
    /// </summary>
    public void LogSkipped(string tableName, int sourceId, string reason)
    {
        _entries.Add(new AuditEntry
        {
            Timestamp = DateTime.Now,
            TableName = tableName,
            SourceId = sourceId,
            Status = AuditStatus.Skipped,
            Description = reason
        });
    }

    /// <summary>
    /// Loguje błąd migracji rekordu.
    /// </summary>
    public void LogError(string tableName, int sourceId, string errorMessage)
    {
        _entries.Add(new AuditEntry
        {
            Timestamp = DateTime.Now,
            TableName = tableName,
            SourceId = sourceId,
            Status = AuditStatus.Error,
            Description = errorMessage
        });

        if (_tableAudits.TryGetValue(tableName, out var audit))
        {
            audit.Errors.Add((sourceId, errorMessage));
        }
    }

    /// <summary>
    /// Kończy audit i generuje pliki.
    /// </summary>
    public void Finish()
    {
        _endTime = DateTime.Now;
    }

    /// <summary>
    /// Eksportuje pełny audit do pliku CSV.
    /// </summary>
    public string ExportToCSV()
    {
        var filePath = $"{_baseFileName}_full.csv";
        var sb = new StringBuilder();

        // Nagłówek
        sb.AppendLine("Timestamp;Table;SourceId;TargetId;Status;Description");

        // Dane
        foreach (var entry in _entries)
        {
            sb.AppendLine(string.Join(";",
                entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                entry.TableName,
                entry.SourceId,
                entry.TargetId?.ToString() ?? "",
                entry.Status.ToString(),
                EscapeCsv(entry.Description)));
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    /// <summary>
    /// Eksportuje mapowania ID do pliku CSV (source_id -> target_id).
    /// </summary>
    public string ExportMappingsToCSV()
    {
        var filePath = $"{_baseFileName}_mappings.csv";
        var sb = new StringBuilder();

        // Nagłówek
        sb.AppendLine("Table;SourceId;TargetId");

        // Dane
        foreach (var (tableName, audit) in _tableAudits)
        {
            foreach (var (sourceId, targetId) in audit.Mappings)
            {
                sb.AppendLine($"{tableName};{sourceId};{targetId}");
            }
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    /// <summary>
    /// Eksportuje tylko błędy do pliku CSV.
    /// </summary>
    public string ExportErrorsToCSV()
    {
        var filePath = $"{_baseFileName}_errors.csv";
        var sb = new StringBuilder();

        // Nagłówek
        sb.AppendLine("Table;SourceId;ErrorMessage");

        // Dane
        foreach (var (tableName, audit) in _tableAudits)
        {
            foreach (var (sourceId, errorMessage) in audit.Errors)
            {
                sb.AppendLine($"{tableName};{sourceId};{EscapeCsv(errorMessage)}");
            }
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    /// <summary>
    /// Eksportuje podsumowanie do pliku CSV.
    /// </summary>
    public string ExportSummaryToCSV()
    {
        var filePath = $"{_baseFileName}_summary.csv";
        var sb = new StringBuilder();

        // Metadane
        sb.AppendLine($"# Migration Audit Summary");
        sb.AppendLine($"# Start: {_startTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# End: {_endTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# Duration: {(_endTime - _startTime).TotalMinutes:F1} minutes");
        sb.AppendLine();

        // Nagłówek
        sb.AppendLine("Table;Total;Migrated;Skipped;Errors;Duration(sec);RecordsPerSec");

        // Dane per tabela
        foreach (var (tableName, audit) in _tableAudits.OrderBy(x => x.Value.StartTime))
        {
            var duration = (audit.EndTime - audit.StartTime).TotalSeconds;
            var recordsPerSec = duration > 0 ? audit.MigratedRecords / duration : 0;

            sb.AppendLine(string.Join(";",
                tableName,
                audit.TotalRecords,
                audit.MigratedRecords,
                audit.SkippedRecords,
                audit.ErrorRecords,
                duration.ToString("F1", CultureInfo.InvariantCulture),
                recordsPerSec.ToString("F1", CultureInfo.InvariantCulture)));
        }

        // Suma
        var totalRecords = _tableAudits.Values.Sum(a => a.TotalRecords);
        var totalMigrated = _tableAudits.Values.Sum(a => a.MigratedRecords);
        var totalSkipped = _tableAudits.Values.Sum(a => a.SkippedRecords);
        var totalErrors = _tableAudits.Values.Sum(a => a.ErrorRecords);
        var totalDuration = (_endTime - _startTime).TotalSeconds;
        var totalPerSec = totalDuration > 0 ? totalMigrated / totalDuration : 0;

        sb.AppendLine(string.Join(";",
            "TOTAL",
            totalRecords,
            totalMigrated,
            totalSkipped,
            totalErrors,
            totalDuration.ToString("F1", CultureInfo.InvariantCulture),
            totalPerSec.ToString("F1", CultureInfo.InvariantCulture)));

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    /// <summary>
    /// Eksportuje wszystkie raporty i zwraca ścieżki.
    /// </summary>
    public List<string> ExportAll()
    {
        Finish();

        var files = new List<string>
        {
            ExportSummaryToCSV(),
            ExportMappingsToCSV()
        };

        // Eksportuj błędy tylko jeśli są
        if (_tableAudits.Values.Any(a => a.Errors.Count > 0))
        {
            files.Add(ExportErrorsToCSV());
        }

        // Pełny audit tylko jeśli jest mały
        if (_entries.Count < 10000)
        {
            files.Add(ExportToCSV());
        }

        return files;
    }

    /// <summary>
    /// Pobiera statystyki dla tabeli.
    /// </summary>
    public TableAudit? GetTableStats(string tableName)
    {
        return _tableAudits.TryGetValue(tableName, out var audit) ? audit : null;
    }

    /// <summary>
    /// Pobiera wszystkie statystyki.
    /// </summary>
    public IReadOnlyDictionary<string, TableAudit> GetAllStats() => _tableAudits;

    /// <summary>
    /// Pobiera metryki wydajności.
    /// </summary>
    public PerformanceMetrics GetPerformanceMetrics()
    {
        var totalDuration = (_endTime - _startTime).TotalSeconds;
        var totalMigrated = _tableAudits.Values.Sum(a => a.MigratedRecords);

        return new PerformanceMetrics
        {
            TotalDurationSeconds = totalDuration,
            TotalRecordsMigrated = totalMigrated,
            RecordsPerSecond = totalDuration > 0 ? totalMigrated / totalDuration : 0,
            TableMetrics = _tableAudits.ToDictionary(
                x => x.Key,
                x => new TablePerformance
                {
                    DurationSeconds = (x.Value.EndTime - x.Value.StartTime).TotalSeconds,
                    RecordsMigrated = x.Value.MigratedRecords,
                    RecordsPerSecond = (x.Value.EndTime - x.Value.StartTime).TotalSeconds > 0
                        ? x.Value.MigratedRecords / (x.Value.EndTime - x.Value.StartTime).TotalSeconds
                        : 0
                })
        };
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}

public enum AuditStatus
{
    Success,
    Skipped,
    Error
}

public class AuditEntry
{
    public DateTime Timestamp { get; set; }
    public string TableName { get; set; } = string.Empty;
    public int SourceId { get; set; }
    public int? TargetId { get; set; }
    public AuditStatus Status { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class TableAudit
{
    public string TableName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int TotalRecords { get; set; }
    public int MigratedRecords { get; set; }
    public int SkippedRecords { get; set; }
    public int ErrorRecords { get; set; }
    public Dictionary<int, int> Mappings { get; set; } = new();
    public List<(int SourceId, string ErrorMessage)> Errors { get; set; } = new();

    public TimeSpan Duration => EndTime - StartTime;
    public double RecordsPerSecond => Duration.TotalSeconds > 0 ? MigratedRecords / Duration.TotalSeconds : 0;
}

public class PerformanceMetrics
{
    public double TotalDurationSeconds { get; set; }
    public int TotalRecordsMigrated { get; set; }
    public double RecordsPerSecond { get; set; }
    public Dictionary<string, TablePerformance> TableMetrics { get; set; } = new();
}

public class TablePerformance
{
    public double DurationSeconds { get; set; }
    public int RecordsMigrated { get; set; }
    public double RecordsPerSecond { get; set; }
}
