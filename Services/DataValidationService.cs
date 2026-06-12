using Spectre.Console;

namespace EnovaMigrator.Services;

/// <summary>
/// Serwis do walidacji jakości danych przed migracją.
/// Sprawdza PESEL, wymagane pola, formaty danych itp.
/// </summary>
public class DataValidationService
{
    private readonly DatabaseService _sourceDb;
    private readonly List<ValidationIssue> _issues = new();

    public DataValidationService(DatabaseService sourceDb)
    {
        _sourceDb = sourceDb;
    }

    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>
    /// Przeprowadza pełną walidację danych źródłowych.
    /// </summary>
    public async Task<ValidationReport> ValidateAllAsync(IProgress<string>? progress = null)
    {
        _issues.Clear();
        var report = new ValidationReport { StartTime = DateTime.Now };

        progress?.Report("Walidacja pracowników...");
        await ValidatePracownicyAsync();

        progress?.Report("Walidacja umów...");
        await ValidateUmowyAsync();

        progress?.Report("Walidacja list płac...");
        await ValidateListyPlacAsync();

        progress?.Report("Walidacja nieobecności...");
        await ValidateNieobecnosciAsync();

        report.EndTime = DateTime.Now;
        report.Issues = _issues.ToList();
        report.TotalIssues = _issues.Count;
        report.CriticalIssues = _issues.Count(i => i.Severity == ValidationSeverity.Critical);
        report.WarningIssues = _issues.Count(i => i.Severity == ValidationSeverity.Warning);
        report.InfoIssues = _issues.Count(i => i.Severity == ValidationSeverity.Info);

        return report;
    }

    /// <summary>
    /// Walidacja pracowników - PESEL, wymagane pola.
    /// </summary>
    private async Task ValidatePracownicyAsync()
    {
        var pracownicy = await _sourceDb.QueryAsync("SELECT ID, PESEL, Imie, Nazwisko, DataUrodzenia FROM Pracownicy");

        foreach (var row in pracownicy)
        {
            var dict = (IDictionary<string, object>)row;
            var id = Convert.ToInt32(dict["ID"]);
            var pesel = dict["PESEL"]?.ToString();
            var imie = dict["Imie"]?.ToString();
            var nazwisko = dict["Nazwisko"]?.ToString();
            var dataUrodzenia = dict["DataUrodzenia"] as DateTime?;

            // Walidacja PESEL
            if (!string.IsNullOrEmpty(pesel))
            {
                var peselValidation = ValidatePesel(pesel);
                if (!peselValidation.IsValid)
                {
                    _issues.Add(new ValidationIssue
                    {
                        Table = "Pracownicy",
                        RecordId = id,
                        Field = "PESEL",
                        Value = pesel,
                        Severity = ValidationSeverity.Warning,
                        Message = peselValidation.ErrorMessage,
                        RecordDescription = $"{imie} {nazwisko}"
                    });
                }

                // Sprawdź datę urodzenia vs PESEL
                if (dataUrodzenia.HasValue && peselValidation.IsValid)
                {
                    var peselDate = ExtractDateFromPesel(pesel);
                    if (peselDate.HasValue && peselDate.Value.Date != dataUrodzenia.Value.Date)
                    {
                        _issues.Add(new ValidationIssue
                        {
                            Table = "Pracownicy",
                            RecordId = id,
                            Field = "DataUrodzenia/PESEL",
                            Value = $"{dataUrodzenia:yyyy-MM-dd} vs PESEL={pesel}",
                            Severity = ValidationSeverity.Warning,
                            Message = $"Data urodzenia ({dataUrodzenia:yyyy-MM-dd}) nie zgadza się z PESEL ({peselDate:yyyy-MM-dd})",
                            RecordDescription = $"{imie} {nazwisko}"
                        });
                    }
                }
            }
            else
            {
                // Brak PESEL - info (może być OK dla obcokrajowców)
                _issues.Add(new ValidationIssue
                {
                    Table = "Pracownicy",
                    RecordId = id,
                    Field = "PESEL",
                    Value = "(pusty)",
                    Severity = ValidationSeverity.Info,
                    Message = "Brak numeru PESEL",
                    RecordDescription = $"{imie} {nazwisko}"
                });
            }

            // Wymagane pola
            if (string.IsNullOrWhiteSpace(imie))
            {
                _issues.Add(new ValidationIssue
                {
                    Table = "Pracownicy",
                    RecordId = id,
                    Field = "Imie",
                    Severity = ValidationSeverity.Critical,
                    Message = "Brak imienia"
                });
            }

            if (string.IsNullOrWhiteSpace(nazwisko))
            {
                _issues.Add(new ValidationIssue
                {
                    Table = "Pracownicy",
                    RecordId = id,
                    Field = "Nazwisko",
                    Severity = ValidationSeverity.Critical,
                    Message = "Brak nazwiska"
                });
            }
        }
    }

    /// <summary>
    /// Walidacja umów - wymagane pola, spójność dat.
    /// </summary>
    private async Task ValidateUmowyAsync()
    {
        var umowy = await _sourceDb.QueryAsync(@"
            SELECT u.ID, u.Pracownik, u.Data, u.DataRozpoczecia, u.DataZakonczenia, u.NumerPelny,
                   p.Imie, p.Nazwisko
            FROM Umowy u
            LEFT JOIN Pracownicy p ON u.Pracownik = p.ID");

        foreach (var row in umowy)
        {
            var dict = (IDictionary<string, object>)row;
            var id = Convert.ToInt32(dict["ID"]);
            var pracownik = dict["Pracownik"] != null ? Convert.ToInt32(dict["Pracownik"]) : (int?)null;
            var data = dict["Data"] as DateTime?;
            var dataRozp = dict["DataRozpoczecia"] as DateTime?;
            var dataZak = dict["DataZakonczenia"] as DateTime?;
            var numer = dict["NumerPelny"]?.ToString();
            var imie = dict["Imie"]?.ToString();
            var nazwisko = dict["Nazwisko"]?.ToString();

            var recordDesc = $"Umowa {numer} ({imie} {nazwisko})";

            // Sprawdź czy pracownik istnieje
            if (!pracownik.HasValue)
            {
                _issues.Add(new ValidationIssue
                {
                    Table = "Umowy",
                    RecordId = id,
                    Field = "Pracownik",
                    Severity = ValidationSeverity.Critical,
                    Message = "Brak powiązania z pracownikiem",
                    RecordDescription = recordDesc
                });
            }

            // Sprawdź daty
            if (dataRozp.HasValue && dataZak.HasValue && dataRozp > dataZak)
            {
                _issues.Add(new ValidationIssue
                {
                    Table = "Umowy",
                    RecordId = id,
                    Field = "DataRozpoczecia/DataZakonczenia",
                    Value = $"{dataRozp:yyyy-MM-dd} > {dataZak:yyyy-MM-dd}",
                    Severity = ValidationSeverity.Warning,
                    Message = "Data rozpoczęcia późniejsza niż data zakończenia",
                    RecordDescription = recordDesc
                });
            }
        }
    }

    /// <summary>
    /// Walidacja list płac.
    /// </summary>
    private async Task ValidateListyPlacAsync()
    {
        var listy = await _sourceDb.QueryAsync(@"
            SELECT ID, NumerPelny, Definicja, OkresFrom, OkresTo, Status
            FROM ListyPlac");

        foreach (var row in listy)
        {
            var dict = (IDictionary<string, object>)row;
            var id = Convert.ToInt32(dict["ID"]);
            var numer = dict["NumerPelny"]?.ToString();
            var definicja = dict["Definicja"] != null ? Convert.ToInt32(dict["Definicja"]) : (int?)null;
            var okresFrom = dict["OkresFrom"] as DateTime?;
            var okresTo = dict["OkresTo"] as DateTime?;

            var recordDesc = $"Lista {numer}";

            if (!definicja.HasValue)
            {
                _issues.Add(new ValidationIssue
                {
                    Table = "ListyPlac",
                    RecordId = id,
                    Field = "Definicja",
                    Severity = ValidationSeverity.Warning,
                    Message = "Brak definicji listy płac",
                    RecordDescription = recordDesc
                });
            }

            if (okresFrom.HasValue && okresTo.HasValue && okresFrom > okresTo)
            {
                _issues.Add(new ValidationIssue
                {
                    Table = "ListyPlac",
                    RecordId = id,
                    Field = "OkresFrom/OkresTo",
                    Value = $"{okresFrom:yyyy-MM-dd} > {okresTo:yyyy-MM-dd}",
                    Severity = ValidationSeverity.Warning,
                    Message = "Nieprawidłowy zakres dat okresu",
                    RecordDescription = recordDesc
                });
            }
        }
    }

    /// <summary>
    /// Walidacja nieobecności.
    /// </summary>
    private async Task ValidateNieobecnosciAsync()
    {
        var nieobecnosci = await _sourceDb.QueryAsync(@"
            SELECT n.ID, n.Zrodlo, n.ZrodloType, n.Definicja, n.OkresFrom, n.OkresTo,
                   p.Imie, p.Nazwisko
            FROM Nieobecnosci n
            LEFT JOIN Pracownicy p ON n.Zrodlo = p.ID AND n.ZrodloType LIKE 'Pracowni%'
            WHERE n.ZrodloType LIKE 'Pracowni%'");

        foreach (var row in nieobecnosci)
        {
            var dict = (IDictionary<string, object>)row;
            var id = Convert.ToInt32(dict["ID"]);
            var definicja = dict["Definicja"] != null ? Convert.ToInt32(dict["Definicja"]) : (int?)null;
            var okresFrom = dict["OkresFrom"] as DateTime?;
            var okresTo = dict["OkresTo"] as DateTime?;
            var imie = dict["Imie"]?.ToString();
            var nazwisko = dict["Nazwisko"]?.ToString();

            var recordDesc = $"Nieobecność {imie} {nazwisko} ({okresFrom:yyyy-MM-dd})";

            if (!definicja.HasValue)
            {
                _issues.Add(new ValidationIssue
                {
                    Table = "Nieobecnosci",
                    RecordId = id,
                    Field = "Definicja",
                    Severity = ValidationSeverity.Warning,
                    Message = "Brak definicji nieobecności",
                    RecordDescription = recordDesc
                });
            }

            if (okresFrom.HasValue && okresTo.HasValue && okresFrom > okresTo)
            {
                _issues.Add(new ValidationIssue
                {
                    Table = "Nieobecnosci",
                    RecordId = id,
                    Field = "OkresFrom/OkresTo",
                    Value = $"{okresFrom:yyyy-MM-dd} > {okresTo:yyyy-MM-dd}",
                    Severity = ValidationSeverity.Warning,
                    Message = "Data rozpoczęcia późniejsza niż data zakończenia",
                    RecordDescription = recordDesc
                });
            }
        }
    }

    #region PESEL Validation

    /// <summary>
    /// Waliduje numer PESEL.
    /// </summary>
    public static (bool IsValid, string ErrorMessage) ValidatePesel(string pesel)
    {
        if (string.IsNullOrWhiteSpace(pesel))
            return (false, "PESEL jest pusty");

        // Usuń spacje
        pesel = pesel.Replace(" ", "").Replace("-", "");

        if (pesel.Length != 11)
            return (false, $"PESEL ma nieprawidłową długość ({pesel.Length} zamiast 11)");

        if (!pesel.All(char.IsDigit))
            return (false, "PESEL zawiera niedozwolone znaki");

        // Walidacja cyfry kontrolnej
        var weights = new[] { 1, 3, 7, 9, 1, 3, 7, 9, 1, 3 };
        var sum = 0;

        for (int i = 0; i < 10; i++)
        {
            sum += (pesel[i] - '0') * weights[i];
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        var actualCheckDigit = pesel[10] - '0';

        if (checkDigit != actualCheckDigit)
            return (false, $"Nieprawidłowa cyfra kontrolna (oczekiwano {checkDigit}, jest {actualCheckDigit})");

        return (true, string.Empty);
    }

    /// <summary>
    /// Wyciąga datę urodzenia z PESEL.
    /// </summary>
    public static DateTime? ExtractDateFromPesel(string pesel)
    {
        if (string.IsNullOrWhiteSpace(pesel) || pesel.Length < 6)
            return null;

        try
        {
            var year = int.Parse(pesel.Substring(0, 2));
            var month = int.Parse(pesel.Substring(2, 2));
            var day = int.Parse(pesel.Substring(4, 2));

            // Określ stulecie na podstawie miesiąca
            int century;
            if (month > 80)
            {
                century = 1800;
                month -= 80;
            }
            else if (month > 60)
            {
                century = 2200;
                month -= 60;
            }
            else if (month > 40)
            {
                century = 2100;
                month -= 40;
            }
            else if (month > 20)
            {
                century = 2000;
                month -= 20;
            }
            else
            {
                century = 1900;
            }

            return new DateTime(century + year, month, day);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    /// <summary>
    /// Wyświetla raport walidacji w konsoli.
    /// </summary>
    public void DisplayReport(ValidationReport report)
    {
        AnsiConsole.MarkupLine($"[blue]Raport walidacji danych[/]");
        AnsiConsole.MarkupLine($"[grey]Czas: {report.StartTime:HH:mm:ss} - {report.EndTime:HH:mm:ss}[/]");
        AnsiConsole.WriteLine();

        // Podsumowanie
        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Poziom")
            .AddColumn("Liczba");

        summaryTable.AddRow("[red]Krytyczne[/]", report.CriticalIssues.ToString());
        summaryTable.AddRow("[yellow]Ostrzeżenia[/]", report.WarningIssues.ToString());
        summaryTable.AddRow("[blue]Informacje[/]", report.InfoIssues.ToString());
        summaryTable.AddRow("[white]RAZEM[/]", report.TotalIssues.ToString());

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        // Szczegóły (tylko krytyczne i ostrzeżenia)
        var criticalAndWarnings = report.Issues
            .Where(i => i.Severity != ValidationSeverity.Info)
            .GroupBy(i => i.Table)
            .ToList();

        if (criticalAndWarnings.Any())
        {
            AnsiConsole.MarkupLine("[yellow]Problemy do rozwiązania:[/]");

            foreach (var tableGroup in criticalAndWarnings)
            {
                AnsiConsole.MarkupLine($"[blue]{tableGroup.Key}[/] ({tableGroup.Count()} problemów)");

                var issueTable = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("ID")
                    .AddColumn("Pole")
                    .AddColumn("Problem")
                    .AddColumn("Opis");

                foreach (var issue in tableGroup.Take(10))
                {
                    var color = issue.Severity == ValidationSeverity.Critical ? "red" : "yellow";
                    issueTable.AddRow(
                        issue.RecordId.ToString(),
                        issue.Field,
                        $"[{color}]{issue.Message}[/]",
                        issue.RecordDescription ?? "");
                }

                AnsiConsole.Write(issueTable);

                if (tableGroup.Count() > 10)
                {
                    AnsiConsole.MarkupLine($"[grey]... i {tableGroup.Count() - 10} więcej[/]");
                }

                AnsiConsole.WriteLine();
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[green]Brak krytycznych problemów![/]");
        }
    }
}

public enum ValidationSeverity
{
    Info,       // Informacja - może być OK
    Warning,    // Ostrzeżenie - wymaga uwagi
    Critical    // Krytyczne - może uniemożliwić migrację
}

public class ValidationIssue
{
    public string Table { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public string Field { get; set; } = string.Empty;
    public string? Value { get; set; }
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RecordDescription { get; set; }
}

public class ValidationReport
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<ValidationIssue> Issues { get; set; } = new();
    public int TotalIssues { get; set; }
    public int CriticalIssues { get; set; }
    public int WarningIssues { get; set; }
    public int InfoIssues { get; set; }

    public bool HasCriticalIssues => CriticalIssues > 0;
}
