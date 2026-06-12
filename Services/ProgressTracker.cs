using System.Diagnostics;

namespace EnovaMigrator.Services;

/// <summary>
/// Śledzenie postępu migracji z estymacją ETA.
/// </summary>
public class ProgressTracker
{
    private readonly Stopwatch _totalStopwatch = new();
    private readonly Dictionary<string, TableProgress> _tableProgress = new();
    private readonly List<string> _tableOrder;
    private int _currentTableIndex = -1;
    private string? _currentTable;

    public ProgressTracker()
    {
        _tableOrder = new List<string>
        {
            "Pracownicy", "Umowy", "ListyPlac", "Wyplaty", "WypElementy",
            "Rodzina", "Nieobecnosci", "Dodatki", "Adresy", "RachBankPodmiot",
            "PracHistorie", "Kalendarze", "HistZatrudnien"
        };
    }

    public void Start()
    {
        _totalStopwatch.Start();
    }

    public void Stop()
    {
        _totalStopwatch.Stop();
    }

    /// <summary>
    /// Rozpoczyna śledzenie tabeli.
    /// </summary>
    public void StartTable(string tableName, int totalRecords)
    {
        _currentTableIndex = _tableOrder.IndexOf(tableName);
        _currentTable = tableName;

        _tableProgress[tableName] = new TableProgress
        {
            TableName = tableName,
            TotalRecords = totalRecords,
            StartTime = DateTime.Now
        };
    }

    /// <summary>
    /// Aktualizuje postęp dla tabeli.
    /// </summary>
    public void UpdateProgress(string tableName, int processed)
    {
        if (_tableProgress.TryGetValue(tableName, out var progress))
        {
            progress.ProcessedRecords = processed;
        }
    }

    /// <summary>
    /// Kończy śledzenie tabeli.
    /// </summary>
    public void EndTable(string tableName, int migrated, int skipped, int errors)
    {
        if (_tableProgress.TryGetValue(tableName, out var progress))
        {
            progress.EndTime = DateTime.Now;
            progress.MigratedRecords = migrated;
            progress.SkippedRecords = skipped;
            progress.ErrorRecords = errors;
            progress.IsCompleted = true;
        }
    }

    /// <summary>
    /// Pobiera aktualny status postępu.
    /// </summary>
    public ProgressStatus GetStatus()
    {
        var completedTables = _tableProgress.Values.Count(t => t.IsCompleted);
        var totalTables = _tableOrder.Count;

        // Oblicz prędkość na podstawie zakończonych tabel
        var completedRecords = _tableProgress.Values.Where(t => t.IsCompleted).Sum(t => t.ProcessedRecords);
        var elapsedSeconds = _totalStopwatch.Elapsed.TotalSeconds;
        var recordsPerSecond = elapsedSeconds > 0 ? completedRecords / elapsedSeconds : 0;

        // Oblicz pozostałe rekordy
        var remainingRecords = _tableProgress.Values
            .Where(t => !t.IsCompleted)
            .Sum(t => Math.Max(0, t.TotalRecords - t.ProcessedRecords));

        // Dodaj szacunkową liczbę rekordów dla nieuruchomionych tabel
        // (zakładamy średnią z ukończonych tabel)
        var avgRecordsPerTable = completedTables > 0
            ? _tableProgress.Values.Where(t => t.IsCompleted).Average(t => t.TotalRecords)
            : 100; // domyślnie

        var tablesNotStarted = totalTables - _tableProgress.Count;
        remainingRecords += (int)(tablesNotStarted * avgRecordsPerTable);

        // Oblicz ETA
        TimeSpan? eta = null;
        if (recordsPerSecond > 0 && remainingRecords > 0)
        {
            eta = TimeSpan.FromSeconds(remainingRecords / recordsPerSecond);
        }

        return new ProgressStatus
        {
            CurrentTable = _currentTable,
            CurrentTableIndex = _currentTableIndex + 1,
            TotalTables = totalTables,
            CompletedTables = completedTables,
            TotalRecordsProcessed = completedRecords + (_tableProgress.TryGetValue(_currentTable ?? "", out var p) ? p.ProcessedRecords : 0),
            RecordsPerSecond = recordsPerSecond,
            ElapsedTime = _totalStopwatch.Elapsed,
            EstimatedTimeRemaining = eta,
            TableProgresses = _tableProgress.Values.ToList()
        };
    }

    /// <summary>
    /// Generuje tekst statusu dla wyświetlenia.
    /// </summary>
    public string GetStatusText()
    {
        var status = GetStatus();
        var parts = new List<string>();

        if (status.CurrentTable != null)
        {
            parts.Add($"[{status.CurrentTableIndex}/{status.TotalTables}] {status.CurrentTable}");
        }

        if (status.RecordsPerSecond > 0)
        {
            parts.Add($"{status.RecordsPerSecond:F0} rec/s");
        }

        if (status.EstimatedTimeRemaining.HasValue)
        {
            var eta = status.EstimatedTimeRemaining.Value;
            if (eta.TotalMinutes >= 1)
            {
                parts.Add($"ETA: {eta.Minutes}m {eta.Seconds}s");
            }
            else
            {
                parts.Add($"ETA: {eta.Seconds}s");
            }
        }

        parts.Add($"Czas: {status.ElapsedTime:mm\\:ss}");

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Pobiera szczegółowy postęp dla aktualnej tabeli.
    /// </summary>
    public string GetCurrentTableProgress()
    {
        if (_currentTable == null || !_tableProgress.TryGetValue(_currentTable, out var progress))
            return "";

        var percent = progress.TotalRecords > 0
            ? (progress.ProcessedRecords * 100.0 / progress.TotalRecords)
            : 0;

        return $"{progress.ProcessedRecords}/{progress.TotalRecords} ({percent:F0}%)";
    }
}

public class TableProgress
{
    public string TableName { get; set; } = "";
    public int TotalRecords { get; set; }
    public int ProcessedRecords { get; set; }
    public int MigratedRecords { get; set; }
    public int SkippedRecords { get; set; }
    public int ErrorRecords { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsCompleted { get; set; }

    public TimeSpan Duration => IsCompleted ? EndTime - StartTime : DateTime.Now - StartTime;
    public double RecordsPerSecond => Duration.TotalSeconds > 0 ? ProcessedRecords / Duration.TotalSeconds : 0;
}

public class ProgressStatus
{
    public string? CurrentTable { get; set; }
    public int CurrentTableIndex { get; set; }
    public int TotalTables { get; set; }
    public int CompletedTables { get; set; }
    public int TotalRecordsProcessed { get; set; }
    public double RecordsPerSecond { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public List<TableProgress> TableProgresses { get; set; } = new();

    public double OverallProgress => TotalTables > 0 ? (CompletedTables * 100.0 / TotalTables) : 0;
}
