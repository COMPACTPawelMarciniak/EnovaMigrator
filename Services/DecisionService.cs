using EnovaMigrator.Models;
using Spectre.Console;

namespace EnovaMigrator.Services;

/// <summary>
/// Interaktywny interfejs do podejmowania decyzji migracyjnych
/// </summary>
public class DecisionService
{
    private readonly DatabaseService _targetDb;

    public DecisionService(DatabaseService targetDb)
    {
        _targetDb = targetDb;
    }

    /// <summary>
    /// Wyświetla podsumowanie analizy
    /// </summary>
    public void DisplayAnalysisSummary(MigrationPlan plan)
    {
        AnsiConsole.Clear();

        var panel = new Panel(
            new Markup($"[bold]Analiza migracji[/]\n" +
                       $"Source: [cyan]{plan.SourceDatabase}[/]\n" +
                       $"Target: [cyan]{plan.TargetDatabase}[/]\n" +
                       $"Data: [dim]{plan.CreatedAt:yyyy-MM-dd HH:mm}[/]"))
        {
            Border = BoxBorder.Double,
            Padding = new Padding(2, 1)
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        // Statystyki mapowań
        DisplayMappingStats(plan.Stats);
        AnsiConsole.WriteLine();

        // Statystyki rekordów
        DisplayRecordStats(plan.Stats);
        AnsiConsole.WriteLine();

        // Podsumowanie problemów
        DisplayIssueSummary(plan);
    }

    private void DisplayMappingStats(AnalysisStats stats)
    {
        var table = new Table()
            .Title("[bold yellow]Mapowania definicji[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("Typ")
            .AddColumn(new TableColumn("Dopasowane").Centered())
            .AddColumn(new TableColumn("Razem").Centered())
            .AddColumn(new TableColumn("Status").Centered());

        AddMappingRow(table, "DefElementow", stats.DefElementowMatched, stats.DefElementowTotal);
        AddMappingRow(table, "DefNieobecnosci", stats.DefNieobecnosciMatched, stats.DefNieobecnosciTotal);
        AddMappingRow(table, "DefListPlac", stats.DefListPlacMatched, stats.DefListPlacTotal);
        AddMappingRow(table, "DefDokumentow", stats.DefDokumentowMatched, stats.DefDokumentowTotal);
        AddMappingRow(table, "UrzedySkarbowe", stats.UrzedySkarboweMatched, stats.UrzedySkarboweTotal);
        AddMappingRow(table, "Wydzialy", stats.WydzialyMatched, stats.WydzialyTotal);
        AddMappingRow(table, "Kalendarze", stats.KalendarzeMatched, stats.KalendarzeTotal);
        AddMappingRow(table, "Pracownicy", stats.PracownicyMatched, stats.PracownicyTotal);

        AnsiConsole.Write(table);
    }

    private void AddMappingRow(Table table, string name, int matched, int total)
    {
        var status = matched == total ? "[green]✓[/]" :
                     matched == 0 ? "[red]✗[/]" : "[yellow]![/]";
        var matchedStr = matched == total ? $"[green]{matched}[/]" :
                         matched == 0 ? $"[red]{matched}[/]" : $"[yellow]{matched}[/]";
        table.AddRow(name, matchedStr, total.ToString(), status);
    }

    private void DisplayRecordStats(AnalysisStats stats)
    {
        var table = new Table()
            .Title("[bold yellow]Rekordy do migracji[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("Tabela")
            .AddColumn(new TableColumn("W source").RightAligned())
            .AddColumn(new TableColumn("Do migracji").RightAligned());

        table.AddRow("Pracownicy", stats.SourcePracownicy.ToString(), stats.ToMigratePracownicy.ToString());
        table.AddRow("Umowy", stats.SourceUmowy.ToString(), stats.ToMigrateUmowy.ToString());
        table.AddRow("ListyPlac", stats.SourceListyPlac.ToString(), stats.ToMigrateListyPlac.ToString());
        table.AddRow("Wyplaty", stats.SourceWyplaty.ToString(), stats.ToMigrateWyplaty.ToString());
        table.AddRow("WypElementy", stats.SourceWypElementy.ToString(), stats.ToMigrateWypElementy.ToString());
        table.AddRow("Nieobecnosci", stats.SourceNieobecnosci.ToString(), stats.ToMigrateNieobecnosci.ToString());
        table.AddRow("Rodzina", stats.SourceRodzina.ToString(), stats.ToMigrateRodzina.ToString());
        table.AddRow("Dodatki", stats.SourceDodatki.ToString(), stats.ToMigrateDodatki.ToString());
        table.AddRow("Adresy", stats.SourceAdresy.ToString(), stats.ToMigrateAdresy.ToString());
        table.AddRow("RachBankPodmiot", stats.SourceRachunki.ToString(), stats.ToMigrateRachunki.ToString());
        table.AddRow("PracHistorie", stats.SourcePracHistorie.ToString(), stats.ToMigratePracHistorie.ToString());
        table.AddRow("Kalendarze", stats.SourceKalendarze.ToString(), stats.ToMigrateKalendarze.ToString());
        table.AddRow("HistZatrudnienia", stats.SourceHistZatrudnien.ToString(), stats.ToMigrateHistZatrudnien.ToString());
        table.AddRow("[bold]RAZEM[/]", "", $"[bold]{stats.TotalToMigrate}[/]");

        AnsiConsole.Write(table);
    }

    private void DisplayIssueSummary(MigrationPlan plan)
    {
        if (plan.Issues.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Brak problemów wymagających decyzji![/]");
            return;
        }

        var table = new Table()
            .Title($"[bold red]Problemy wymagające decyzji ({plan.Issues.Count})[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("Kategoria")
            .AddColumn(new TableColumn("Liczba").Centered())
            .AddColumn("Status");

        foreach (var group in plan.IssuesByCategory)
        {
            var resolved = group.Count(i => i.Resolution != ResolutionType.None);
            var total = group.Count();
            var status = resolved == total ? "[green]Rozwiązane[/]" :
                         resolved == 0 ? "[red]Oczekuje[/]" : $"[yellow]{resolved}/{total}[/]";
            table.AddRow(group.Key, total.ToString(), status);
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Główne menu interaktywne
    /// </summary>
    public async Task<bool> RunInteractiveMenuAsync(MigrationPlan plan)
    {
        while (true)
        {
            DisplayAnalysisSummary(plan);
            AnsiConsole.WriteLine();

            var choices = new List<string>();

            // Dodaj kategorie z problemami
            foreach (var group in plan.IssuesByCategory)
            {
                var pending = group.Count(i => i.Resolution == ResolutionType.None);
                var marker = pending > 0 ? $"[red]({pending})[/]" : "[green](✓)[/]";
                choices.Add($"Rozwiąż: {group.Key} {marker}");
            }

            choices.Add("");
            if (plan.IsComplete)
            {
                choices.Add("[green]>>> WYKONAJ MIGRACJĘ <<<[/]");
            }
            else
            {
                choices.Add($"[dim]>>> WYKONAJ MIGRACJĘ (wymaga {plan.PendingDecisionsCount} decyzji)[/]");
            }
            choices.Add("[yellow]Zapisz plan i wyjdź[/]");
            choices.Add("[red]Anuluj[/]");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Co chcesz zrobić?")
                    .PageSize(15)
                    .AddChoices(choices.Where(c => !string.IsNullOrEmpty(c))));

            if (choice.StartsWith("Rozwiąż:"))
            {
                var category = choice.Replace("Rozwiąż:", "").Trim();
                // Usuń markery kolorów
                category = System.Text.RegularExpressions.Regex.Replace(category, @"\[.*?\]", "").Trim();
                category = category.Replace("(✓)", "").Replace(")", "").Trim();
                // Wyciągnij samą nazwę kategorii
                var match = System.Text.RegularExpressions.Regex.Match(category, @"^([^\(]+)");
                if (match.Success)
                    category = match.Groups[1].Value.Trim();

                await ResolveIssuesByCategoryAsync(plan, category);
            }
            else if (choice.Contains("WYKONAJ MIGRACJĘ"))
            {
                if (plan.IsComplete)
                {
                    return true; // Wykonaj migrację
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Najpierw rozwiąż wszystkie problemy![/]");
                    AnsiConsole.WriteLine("Naciśnij dowolny klawisz...");
                    Console.ReadKey(true);
                }
            }
            else if (choice.Contains("Zapisz plan"))
            {
                return false; // Zapisz bez migracji
            }
            else if (choice.Contains("Anuluj"))
            {
                if (AnsiConsole.Confirm("Czy na pewno chcesz anulować? Utracisz wszystkie decyzje.", false))
                {
                    throw new OperationCanceledException();
                }
            }
        }
    }

    /// <summary>
    /// Rozwiązuje problemy w danej kategorii
    /// </summary>
    private async Task ResolveIssuesByCategoryAsync(MigrationPlan plan, string category)
    {
        var issues = plan.Issues.Where(i => i.Category == category).ToList();
        if (issues.Count == 0) return;

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold]Rozwiązywanie problemów: {category}[/]");
        AnsiConsole.MarkupLine($"[dim]Liczba problemów: {issues.Count}[/]");
        AnsiConsole.WriteLine();

        // Sprawdź czy wszystkie mają takie same dostępne rozwiązania
        var firstIssue = issues.First();
        var allSameType = issues.All(i => i.Type == firstIssue.Type);

        if (allSameType && issues.Count > 1)
        {
            // Zapytaj czy rozwiązać zbiorczo
            var batchChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Jak chcesz rozwiązać te problemy?")
                    .AddChoices(
                        "Rozwiąż wszystkie naraz (ta sama decyzja)",
                        "Rozwiąż pojedynczo",
                        "[yellow]<< Wróć do menu głównego[/]"
                    ));

            if (batchChoice.Contains("Wróć"))
            {
                return;
            }

            if (batchChoice.Contains("naraz"))
            {
                await ResolveBatchAsync(issues);
                return;
            }
        }

        // Rozwiązuj pojedynczo z możliwością cofania
        var index = 0;
        while (index < issues.Count)
        {
            var result = await ResolveSingleIssueAsync(issues[index], index + 1, issues.Count);

            if (result == NavigationResult.Back)
            {
                if (index > 0)
                    index--; // Cofnij do poprzedniego
                else
                    return; // Wróć do menu głównego
            }
            else if (result == NavigationResult.Exit)
            {
                return; // Wróć do menu głównego
            }
            else
            {
                index++; // Następny problem
            }
        }
    }

    private enum NavigationResult { Next, Back, Exit }

    /// <summary>
    /// Rozwiązuje wiele problemów tą samą decyzją
    /// </summary>
    private async Task ResolveBatchAsync(List<MigrationIssue> issues)
    {
        var firstIssue = issues.First();

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold]Rozwiązywanie zbiorcze: {issues.Count} problemów[/]");
        AnsiConsole.WriteLine();

        // Pokaż listę problemów
        var table = new Table()
            .Border(TableBorder.Simple)
            .AddColumn("ID")
            .AddColumn("Nazwa")
            .AddColumn("Kod");

        foreach (var issue in issues.Take(15))
        {
            table.AddRow(
                issue.SourceId?.ToString() ?? "-",
                issue.Name.Length > 40 ? issue.Name[..37] + "..." : issue.Name,
                issue.Code ?? "-"
            );
        }
        if (issues.Count > 15)
        {
            table.AddRow("...", $"[dim]i {issues.Count - 15} więcej[/]", "");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Pokaż dostępne rozwiązania
        var resolution = await SelectResolutionWithNavigationAsync(firstIssue);

        if (resolution == null || resolution.Type == ResolutionType.None)
        {
            return; // Powrót do menu
        }

        // Zastosuj do wszystkich
        foreach (var issue in issues)
        {
            issue.Resolution = resolution.Type;
            issue.ResolutionTargetId = resolution.TargetId;
            issue.ResolutionTargetName = resolution.TargetName;
        }

        AnsiConsole.MarkupLine($"[green]Zastosowano rozwiązanie do {issues.Count} problemów[/]");
        await Task.Delay(500);
    }

    /// <summary>
    /// Rozwiązuje pojedynczy problem z nawigacją
    /// </summary>
    private async Task<NavigationResult> ResolveSingleIssueAsync(MigrationIssue issue, int current, int total)
    {
        AnsiConsole.Clear();

        AnsiConsole.MarkupLine($"[dim]Problem {current}/{total} | << Wstecz (wybierz) | >> Dalej (rozwiąż)[/]");
        AnsiConsole.WriteLine();

        var panel = new Panel(
            $"[bold]{issue.Type}[/]\n\n" +
            $"Nazwa: [cyan]{issue.Name}[/]\n" +
            (issue.Code != null ? $"Kod: [cyan]{issue.Code}[/]\n" : "") +
            $"ID źródłowe: [cyan]{issue.SourceId}[/]\n" +
            $"Opis: {issue.Description}\n" +
            $"Dotknięte rekordy: [yellow]{issue.AffectedRecordsCount}[/]" +
            (issue.Resolution != ResolutionType.None
                ? $"\n\n[green]Aktualne rozwiązanie: {issue.Resolution}[/]"
                : ""))
        {
            Header = new PanelHeader($"Problem #{issue.Id}"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        var resolution = await SelectResolutionWithNavigationAsync(issue);

        if (resolution == null)
            return NavigationResult.Back;

        if (resolution.Type == ResolutionType.None)
            return NavigationResult.Exit;

        issue.Resolution = resolution.Type;
        issue.ResolutionTargetId = resolution.TargetId;
        issue.ResolutionTargetName = resolution.TargetName;

        AnsiConsole.MarkupLine($"[green]✓ Rozwiązanie zapisane: {resolution.Label}[/]");
        await Task.Delay(200);

        return NavigationResult.Next;
    }

    /// <summary>
    /// Rozwiązuje pojedynczy problem (bez nawigacji - dla batch)
    /// </summary>
    private async Task ResolveSingleIssueAsync(MigrationIssue issue)
    {
        await ResolveSingleIssueAsync(issue, 1, 1);
    }

    /// <summary>
    /// Pozwala wybrać rozwiązanie z opcjami nawigacji
    /// Zwraca null = Wróć, ResolutionType.None = Wyjdź do menu
    /// </summary>
    private async Task<ResolutionOption?> SelectResolutionWithNavigationAsync(MigrationIssue issue)
    {
        if (issue.AvailableResolutions.Count == 0)
        {
            issue.AvailableResolutions = GetDefaultResolutions(issue.Type);
        }

        // Buduj listę wyboru z nawigacją
        var choices = new List<string>();

        // Opcje rozwiązań
        foreach (var r in issue.AvailableResolutions)
        {
            var label = r.Type == ResolutionType.MapTo && r.TargetId == null
                ? $"{r.Label} (wybierz cel...)"
                : r.Label;
            choices.Add(label);
        }

        // Opcje nawigacji
        choices.Add(""); // separator wizualny
        choices.Add("[yellow]<< Wróć do poprzedniego[/]");
        choices.Add("[red]<< Wróć do menu głównego[/]");

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Wybierz rozwiązanie:")
                .AddChoices(choices.Where(c => !string.IsNullOrEmpty(c))));

        // Sprawdź nawigację
        if (selected.Contains("poprzedniego"))
            return null; // Sygnał do cofnięcia

        if (selected.Contains("menu głównego"))
            return new ResolutionOption { Type = ResolutionType.None }; // Sygnał do wyjścia

        // Znajdź wybrany indeks (pomijając puste)
        var selectedIndex = issue.AvailableResolutions.FindIndex(r =>
        {
            var label = r.Type == ResolutionType.MapTo && r.TargetId == null
                ? $"{r.Label} (wybierz cel...)"
                : r.Label;
            return label == selected;
        });

        if (selectedIndex < 0)
            return null;

        var resolution = issue.AvailableResolutions[selectedIndex];

        // Jeśli MapTo wymaga wyboru celu
        if (resolution.Type == ResolutionType.MapTo && resolution.TargetId == null)
        {
            var mappingResult = await SelectMappingTargetAsync(issue);
            if (mappingResult.Type == ResolutionType.None)
                return await SelectResolutionWithNavigationAsync(issue); // Wróć do wyboru
            resolution = mappingResult;
        }

        return resolution;
    }

    /// <summary>
    /// Pozwala wybrać rozwiązanie dla problemu (wersja prosta)
    /// </summary>
    private async Task<ResolutionOption> SelectResolutionAsync(MigrationIssue issue)
    {
        var result = await SelectResolutionWithNavigationAsync(issue);
        return result ?? new ResolutionOption { Type = ResolutionType.Skip, Label = "Pomiń" };
    }

    /// <summary>
    /// Pozwala wybrać cel mapowania z wyszukiwaniem
    /// </summary>
    private async Task<ResolutionOption> SelectMappingTargetAsync(MigrationIssue issue)
    {
        // Pobierz listę dostępnych celów z target DB
        var targets = await GetAvailableTargetsAsync(issue.Type);

        if (targets.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Brak dostępnych celów do mapowania w bazie docelowej[/]");
            AnsiConsole.MarkupLine("[grey]Naciśnij Enter aby wrócić...[/]");
            Console.ReadKey(true);
            return new ResolutionOption { Type = ResolutionType.None }; // Sygnał do powrotu
        }

        AnsiConsole.MarkupLine($"\n[blue]Mapowanie dla:[/] [cyan]{issue.Name}[/] (Kod: {issue.Code ?? "-"})");
        AnsiConsole.MarkupLine($"[grey]Dostępnych definicji w target: {targets.Count}[/]\n");

        var choices = targets.Select(t => $"{t.Name} (ID: {t.Id})").ToList();
        choices.Insert(0, "[yellow]<< Wróć do wyboru rozwiązania[/]");

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Wybierz definicję docelową:")
                .PageSize(20)
                .EnableSearch()
                .SearchPlaceholderText("[grey]Wpisz aby wyszukać...[/]")
                .AddChoices(choices));

        if (selected.Contains("Wróć"))
        {
            return new ResolutionOption { Type = ResolutionType.None }; // Sygnał do powrotu
        }

        var selectedIndex = choices.IndexOf(selected) - 1; // -1 bo dodaliśmy "Wróć" na początku
        var target = targets[selectedIndex];

        return new ResolutionOption
        {
            Type = ResolutionType.MapTo,
            Label = $"Mapuj na: {target.Name}",
            TargetId = target.Id,
            TargetName = target.Name
        };
    }

    /// <summary>
    /// Pobiera dostępne cele mapowania z bazy target
    /// </summary>
    private async Task<List<(int Id, string Name)>> GetAvailableTargetsAsync(IssueType issueType)
    {
        var results = new List<(int Id, string Name)>();

        try
        {
            IEnumerable<dynamic> items = issueType switch
            {
                IssueType.MissingDefElementow => await _targetDb.GetDefElementowAsync(),
                IssueType.MissingDefNieobecnosci => await _targetDb.GetDefNieobecnosciAsync(),
                IssueType.MissingDefListPlac => await _targetDb.GetDefListPlacAsync(),
                IssueType.MissingDefDokumentow => await _targetDb.GetDefDokumentowAsync(),
                IssueType.MissingUrzadSkarbowy => await _targetDb.GetUrzedySkarboweAsync(),
                IssueType.MissingWydzial => await _targetDb.GetWydzialyAsync(),
                IssueType.MissingKalendarz => await _targetDb.GetKalendarzeWzorcoweAsync(),
                _ => Enumerable.Empty<dynamic>()
            };

            foreach (var item in items)
            {
                var dict = (IDictionary<string, object>)item;
                var id = (int)dict["ID"];
                var name = dict.ContainsKey("Nazwa") ? dict["Nazwa"]?.ToString() ?? "" :
                           dict.ContainsKey("Kod") ? dict["Kod"]?.ToString() ?? "" : $"ID: {id}";
                results.Add((id, name));
            }
        }
        catch
        {
            // Ignoruj błędy
        }

        return results;
    }

    /// <summary>
    /// Zwraca domyślne rozwiązania dla danego typu problemu
    /// </summary>
    private List<ResolutionOption> GetDefaultResolutions(IssueType issueType)
    {
        return issueType switch
        {
            // Brakujące definicje - można mapować lub pominąć
            IssueType.MissingDefElementow or
            IssueType.MissingDefNieobecnosci or
            IssueType.MissingDefListPlac or
            IssueType.MissingDefDokumentow or
            IssueType.MissingUrzadSkarbowy or
            IssueType.MissingWydzial or
            IssueType.MissingKalendarz => new List<ResolutionOption>
            {
                new() { Type = ResolutionType.Skip, Label = "Pomiń rekordy używające tej definicji",
                        Description = "Rekordy odwołujące się do tej definicji nie zostaną zmigrowane" },
                new() { Type = ResolutionType.MapTo, Label = "Mapuj na inną definicję w target",
                        Description = "Wybierz istniejącą definicję w bazie docelowej" },
                new() { Type = ResolutionType.SetNull, Label = "Ustaw NULL (jeśli pole opcjonalne)",
                        Description = "Zostaw puste - działa tylko dla opcjonalnych FK" }
            },

            // Nowi pracownicy
            IssueType.NewPracownik => new List<ResolutionOption>
            {
                new() { Type = ResolutionType.Migrate, Label = "Migruj jako nowego pracownika",
                        Description = "Utwórz nowego pracownika w bazie docelowej" },
                new() { Type = ResolutionType.Skip, Label = "Pomiń tego pracownika",
                        Description = "Nie migruj tego pracownika ani jego danych" },
                new() { Type = ResolutionType.MapTo, Label = "Połącz z istniejącym pracownikiem",
                        Description = "Wybierz istniejącego pracownika do połączenia" }
            },

            // Brakujący pracownik (bez PESEL/dopasowania)
            IssueType.MissingPracownik => new List<ResolutionOption>
            {
                new() { Type = ResolutionType.Skip, Label = "Pomiń rekordy tego pracownika",
                        Description = "Nie migruj danych tego pracownika" },
                new() { Type = ResolutionType.MapTo, Label = "Połącz z istniejącym pracownikiem",
                        Description = "Ręcznie wybierz pracownika w target" }
            },

            // Duplikaty - pomiń lub nadpisz
            IssueType.DuplicateUmowa or
            IssueType.DuplicateListaPlac or
            IssueType.DuplicateWyplata or
            IssueType.DuplicateNieobecnosc or
            IssueType.DuplicateRodzina or
            IssueType.DuplicateDodatek or
            IssueType.DuplicateAdres or
            IssueType.DuplicateRachunek or
            IssueType.DuplicatePracHistoria or
            IssueType.DuplicateKalendarz or
            IssueType.DuplicateHistZatrudnienia => new List<ResolutionOption>
            {
                new() { Type = ResolutionType.Skip, Label = "Pomiń (już istnieje w target)",
                        Description = "Nie migruj - rekord już istnieje" },
                new() { Type = ResolutionType.Override, Label = "Nadpisz istniejący rekord",
                        Description = "Zastąp dane w target danymi ze source (UWAGA!)" }
            },

            _ => new List<ResolutionOption>
            {
                new() { Type = ResolutionType.Skip, Label = "Pomiń", Description = "Nie migruj tego rekordu" }
            }
        };
    }

    /// <summary>
    /// Zapisuje plan do pliku JSON
    /// </summary>
    public async Task SavePlanAsync(MigrationPlan plan, string filePath)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(plan, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
        await File.WriteAllTextAsync(filePath, json);
        AnsiConsole.MarkupLine($"[green]Plan zapisany do: {filePath}[/]");
    }

    /// <summary>
    /// Wczytuje plan z pliku JSON
    /// </summary>
    public async Task<MigrationPlan?> LoadPlanAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var json = await File.ReadAllTextAsync(filePath);
        return System.Text.Json.JsonSerializer.Deserialize<MigrationPlan>(json, new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
    }
}
