using System.Text.Json;
using Spectre.Console;
using EnovaMigrator.Services;
using EnovaMigrator.Configuration;
using EnovaMigrator.Models;

namespace EnovaMigrator;

class Program
{
    private static MigrationConfig _config = new();
    private static MappingData _mapping = new();
    private static DatabaseService? _sourceDb;
    private static DatabaseService? _targetDb;

    static async Task Main(string[] args)
    {
        // Tryb testowy/migracji (nie-interaktywny)
        if (args.Contains("--test") || args.Contains("--dry-run") || args.Contains("--migrate"))
        {
            await TestMigration.RunAsync(args);
            return;
        }

        AnsiConsole.Write(new FigletText("Enova Migrator").Color(Color.Blue));
        AnsiConsole.MarkupLine("[grey]Narzedzie do migracji danych kadrowo-placowych miedzy bazami enova365[/]");
        AnsiConsole.WriteLine();

        await LoadConfigurationAsync();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Wybierz opcje:[/]")
                    .PageSize(15)
                    .AddChoices(new[] {
                        "1. Konfiguracja polaczen",
                        "2. Test polaczen",
                        "3. Statystyki baz danych",
                        "4. Porownaj slowniki (DefElementow, DefNieobecnosci, itp.)",
                        "5. Porownaj pracownikow",
                        "6. Zapisz mapowanie do pliku",
                        "7. Wczytaj mapowanie z pliku",
                        "8. Analiza - co trzeba zmigrowac",
                        "9. WYKONAJ MIGRACJE",
                        "E. EDYTOR MAPOWAN (bulk + fuzzy matching)",
                        "V. WALIDACJA DANYCH (PESEL, wymagane pola)",
                        "0. Wyjscie"
                    }));

            AnsiConsole.WriteLine();

            try
            {
                switch (choice[0])
                {
                    case '1': await ConfigureConnectionsAsync(); break;
                    case '2': await TestConnectionsAsync(); break;
                    case '3': await ShowStatisticsAsync(); break;
                    case '4': await CompareDefinitionsAsync(); break;
                    case '5': await ComparePracownicyAsync(); break;
                    case '6': await SaveMappingAsync(); break;
                    case '7': await LoadMappingAsync(); break;
                    case '8': await AnalyzeMigrationAsync(); break;
                    case '9': await ExecuteMigrationAsync(); break;
                    case 'E': await RunMappingEditorAsync(); break;
                    case 'V': await RunDataValidationAsync(); break;
                    case '0': return;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Blad: {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(ex.StackTrace ?? "")}[/]");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Nacisnij Enter, aby kontynuowac...[/]");
            Console.ReadLine();
            AnsiConsole.Clear();
        }
    }

    static async Task LoadConfigurationAsync()
    {
        var configPath = "appsettings.json";
        if (File.Exists(configPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(configPath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("ConnectionStrings", out var cs))
                {
                    if (cs.TryGetProperty("SourceDatabase", out var src))
                        _config.SourceConnectionString = src.GetString() ?? "";
                    if (cs.TryGetProperty("TargetDatabase", out var tgt))
                        _config.TargetConnectionString = tgt.GetString() ?? "";
                }

                if (root.TryGetProperty("Migration", out var mig))
                {
                    if (mig.TryGetProperty("MappingFilePath", out var mp))
                        _config.MappingFilePath = mp.GetString() ?? "mapping.json";
                }

                AnsiConsole.MarkupLine("[green]Wczytano konfiguracje z appsettings.json[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Uwaga: Blad wczytywania konfiguracji: {ex.Message}[/]");
            }
        }
    }

    static async Task ConfigureConnectionsAsync()
    {
        AnsiConsole.MarkupLine("[blue]Konfiguracja polaczen[/]");
        AnsiConsole.WriteLine();

        // Zrodlo
        AnsiConsole.MarkupLine("[yellow]BAZA ZRODLOWA (biuro rachunkowe - z ktorej migrujemy):[/]");
        var srcServer = AnsiConsole.Ask("Server (np. localhost,1433):", "localhost,1433");
        var srcDatabase = AnsiConsole.Ask("Database:", "");
        var srcUser = AnsiConsole.Ask("User:", "sa");
        var srcPassword = AnsiConsole.Prompt(new TextPrompt<string>("Password:").Secret());

        _config.SourceConnectionString = $"Server={srcServer};Database={srcDatabase};User Id={srcUser};Password={srcPassword};TrustServerCertificate=True;";

        // Cel
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]BAZA DOCELOWA (klient - do ktorej migrujemy):[/]");
        var tgtServer = AnsiConsole.Ask("Server (np. localhost,1433):", "localhost,1433");
        var tgtDatabase = AnsiConsole.Ask("Database:", "");
        var tgtUser = AnsiConsole.Ask("User:", "sa");
        var tgtPassword = AnsiConsole.Prompt(new TextPrompt<string>("Password:").Secret());

        _config.TargetConnectionString = $"Server={tgtServer};Database={tgtDatabase};User Id={tgtUser};Password={tgtPassword};TrustServerCertificate=True;";

        // Zapisz do pliku
        var configJson = JsonSerializer.Serialize(new
        {
            ConnectionStrings = new
            {
                SourceDatabase = _config.SourceConnectionString,
                TargetDatabase = _config.TargetConnectionString
            },
            Migration = new
            {
                MappingFilePath = _config.MappingFilePath
            }
        }, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync("appsettings.json", configJson);
        AnsiConsole.MarkupLine("[green]Konfiguracja zapisana do appsettings.json[/]");

        _sourceDb = new DatabaseService(_config.SourceConnectionString);
        _targetDb = new DatabaseService(_config.TargetConnectionString);
    }

    static async Task TestConnectionsAsync()
    {
        EnsureConnections();

        var table = new Table();
        table.AddColumn("Baza");
        table.AddColumn("Status");
        table.AddColumn("Nazwa bazy");

        var srcOk = _sourceDb!.TestConnection();
        var tgtOk = _targetDb!.TestConnection();

        table.AddRow(
            "Zrodlowa (biuro)",
            srcOk ? "[green]OK[/]" : "[red]BLAD[/]",
            srcOk ? _sourceDb.GetDatabaseName() : "-");

        table.AddRow(
            "Docelowa (klient)",
            tgtOk ? "[green]OK[/]" : "[red]BLAD[/]",
            tgtOk ? _targetDb.GetDatabaseName() : "-");

        AnsiConsole.Write(table);
        await Task.CompletedTask;
    }

    static async Task ShowStatisticsAsync()
    {
        EnsureConnections();

        var table = new Table();
        table.AddColumn("Tabela");
        table.AddColumn("Zrodlo (biuro)");
        table.AddColumn("Cel (klient)");
        table.AddColumn("Roznica");

        await AnsiConsole.Status()
            .StartAsync("Pobieranie statystyk...", async ctx =>
            {
                var srcCounts = await _sourceDb!.GetTableCountsAsync();
                var tgtCounts = await _targetDb!.GetTableCountsAsync();

                foreach (var key in srcCounts.Keys)
                {
                    var srcVal = srcCounts[key];
                    var tgtVal = tgtCounts.ContainsKey(key) ? tgtCounts[key] : -1;

                    var srcStr = srcVal >= 0 ? srcVal.ToString() : "[grey]brak[/]";
                    var tgtStr = tgtVal >= 0 ? tgtVal.ToString() : "[grey]brak[/]";

                    var diff = "";
                    if (srcVal >= 0 && tgtVal >= 0)
                    {
                        var d = srcVal - tgtVal;
                        if (d > 0) diff = $"[yellow]+{d}[/]";
                        else if (d < 0) diff = $"[green]{d}[/]";
                        else diff = "[grey]0[/]";
                    }

                    table.AddRow(key, srcStr, tgtStr, diff);
                }
            });

        AnsiConsole.Write(table);
    }

    static async Task CompareDefinitionsAsync()
    {
        EnsureConnections();

        var comparison = new ComparisonService(_sourceDb!, _targetDb!);
        List<DefinitionComparison>? defDokumentow = null;
        List<DefinitionComparison>? kalendarze = null;

        await AnsiConsole.Status()
            .StartAsync("Porownywanie slownikow...", async ctx =>
            {
                // DefElementow
                ctx.Status("Porownywanie DefElementow...");
                var defElementow = await comparison.CompareDefElementowAsync();
                ShowComparisonTable("DefElementow (skladniki placowe)", defElementow);

                // DefNieobecnosci
                ctx.Status("Porownywanie DefNieobecnosci...");
                var defNieobecnosci = await comparison.CompareDefNieobecnosciAsync();
                ShowComparisonTable("DefNieobecnosci", defNieobecnosci);

                // DefListPlac
                ctx.Status("Porownywanie DefListPlac...");
                var defListPlac = await comparison.CompareDefListPlacAsync();
                ShowComparisonTable("DefListPlac", defListPlac);

                // Wydzialy
                ctx.Status("Porownywanie Wydzialow...");
                var wydzialy = await comparison.CompareWydzialyAsync();
                ShowComparisonTable("Wydzialy", wydzialy);

                // DefDokumentow
                ctx.Status("Porownywanie DefDokumentow...");
                defDokumentow = await comparison.CompareDefDokumentowAsync();
                ShowComparisonTable("DefDokumentow (typy dokumentow)", defDokumentow);

                // Kalendarze wzorcowe
                ctx.Status("Porownywanie Kalendarzy wzorcowych...");
                kalendarze = await comparison.CompareKalendarzeAsync();
                ShowComparisonTable("Kalendarze (wzorcowe)", kalendarze);

                // Aktualizuj mapowanie
                var pracownicy = await comparison.ComparePracownicyAsync();
                _mapping = comparison.BuildMappingFromComparisons(
                    defElementow, defNieobecnosci, defListPlac, wydzialy, pracownicy,
                    defDokumentow, kalendarze);
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Zaktualizowano mapowanie slownikow[/]");
    }

    static void ShowComparisonTable(string title, List<DefinitionComparison> comparisons)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[blue]{title}[/]");

        var matched = comparisons.Count(x => x.IsMatched);
        var notMatched = comparisons.Count(x => !x.IsMatched);

        AnsiConsole.MarkupLine($"[green]Dopasowane: {matched}[/] | [red]Niedopasowane: {notMatched}[/]");

        if (notMatched > 0)
        {
            var table = new Table();
            table.AddColumn("ID zrodlo");
            table.AddColumn("Nazwa");
            table.AddColumn("Kod");
            table.AddColumn("Status");

            foreach (var item in comparisons.Where(x => !x.IsMatched).Take(20))
            {
                table.AddRow(
                    item.SourceID.ToString(),
                    item.Name.Length > 40 ? item.Name[..40] + "..." : item.Name,
                    item.Code ?? "-",
                    "[red]Brak w celu[/]");
            }

            if (notMatched > 20)
                table.AddRow("...", $"[grey]i {notMatched - 20} wiecej[/]", "", "");

            AnsiConsole.Write(table);
        }
    }

    static async Task ComparePracownicyAsync()
    {
        EnsureConnections();

        var comparison = new ComparisonService(_sourceDb!, _targetDb!);

        List<PracownikComparison> pracownicy = null!;

        await AnsiConsole.Status()
            .StartAsync("Porownywanie pracownikow...", async ctx =>
            {
                pracownicy = await comparison.ComparePracownicyAsync();
            });

        var matched = pracownicy.Count(x => x.IsMatched);
        var notMatched = pracownicy.Count(x => !x.IsMatched);

        AnsiConsole.MarkupLine($"[blue]Porownanie pracownikow[/]");
        AnsiConsole.MarkupLine($"[green]Dopasowani (po PESEL): {pracownicy.Count(x => x.MatchType == "ByPESEL")}[/]");
        AnsiConsole.MarkupLine($"[yellow]Dopasowani (po nazwisku): {pracownicy.Count(x => x.MatchType == "ByName")}[/]");
        AnsiConsole.MarkupLine($"[red]Niedopasowani: {notMatched}[/]");

        if (notMatched > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Pracownicy ze zrodla bez odpowiednika w celu:[/]");

            var table = new Table();
            table.AddColumn("ID zrodlo");
            table.AddColumn("PESEL");
            table.AddColumn("Nazwisko");
            table.AddColumn("Imie");

            foreach (var item in pracownicy.Where(x => !x.IsMatched).Take(30))
            {
                table.AddRow(
                    item.SourceID.ToString(),
                    MaskPesel(item.PESEL),
                    item.SourceNazwisko,
                    item.SourceImie);
            }

            AnsiConsole.Write(table);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Ci pracownicy musza istniec w bazie docelowej przed migracja,[/]");
            AnsiConsole.MarkupLine("[yellow]lub ich dane nie zostana zmigrowne.[/]");
        }

        // Aktualizuj mapowanie
        foreach (var p in pracownicy.Where(x => x.IsMatched && x.TargetID.HasValue))
        {
            _mapping.Pracownicy[p.SourceID] = p.TargetID!.Value;
            if (!string.IsNullOrEmpty(p.PESEL))
                _mapping.PracownicyByPesel[p.PESEL] = p.TargetID!.Value;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Zaktualizowano mapowanie: {_mapping.Pracownicy.Count} pracownikow[/]");
    }

    static string MaskPesel(string pesel)
    {
        if (string.IsNullOrEmpty(pesel) || pesel.Length < 11)
            return pesel;
        return pesel[..2] + "*******" + pesel[9..];
    }

    static async Task SaveMappingAsync()
    {
        var json = JsonSerializer.Serialize(_mapping, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_config.MappingFilePath, json);
        AnsiConsole.MarkupLine($"[green]Zapisano mapowanie do {_config.MappingFilePath}[/]");
        ShowMappingStats();
    }

    static async Task LoadMappingAsync()
    {
        if (!File.Exists(_config.MappingFilePath))
        {
            AnsiConsole.MarkupLine($"[red]Plik {_config.MappingFilePath} nie istnieje![/]");
            return;
        }

        var json = await File.ReadAllTextAsync(_config.MappingFilePath);
        _mapping = JsonSerializer.Deserialize<MappingData>(json) ?? new MappingData();

        AnsiConsole.MarkupLine($"[green]Wczytano mapowanie z {_config.MappingFilePath}[/]");
        ShowMappingStats();
    }

    static void ShowMappingStats()
    {
        AnsiConsole.MarkupLine($"  Pracownicy: {_mapping.Pracownicy.Count}");
        AnsiConsole.MarkupLine($"  DefElementow: {_mapping.DefElementow.Count}");
        AnsiConsole.MarkupLine($"  DefNieobecnosci: {_mapping.DefNieobecnosci.Count}");
        AnsiConsole.MarkupLine($"  DefListPlac: {_mapping.DefListPlac.Count}");
        AnsiConsole.MarkupLine($"  DefDokumentow: {_mapping.DefDokumentow.Count}");
        AnsiConsole.MarkupLine($"  Wydzialy: {_mapping.Wydzialy.Count}");
        AnsiConsole.MarkupLine($"  Kalendarze: {_mapping.Kalendarze.Count}");
        if (_mapping.ListyPlac.Count > 0)
            AnsiConsole.MarkupLine($"  ListyPlac (zmigr.): {_mapping.ListyPlac.Count}");
        if (_mapping.Wyplaty.Count > 0)
            AnsiConsole.MarkupLine($"  Wyplaty (zmigr.): {_mapping.Wyplaty.Count}");
        if (_mapping.Umowy.Count > 0)
            AnsiConsole.MarkupLine($"  Umowy (zmigr.): {_mapping.Umowy.Count}");
    }

    static async Task AnalyzeMigrationAsync()
    {
        EnsureConnections();

        AnsiConsole.MarkupLine("[blue]Analiza danych do migracji[/]");
        AnsiConsole.MarkupLine("[grey]Sprawdzam co jest w zrodle, czego nie ma w celu...[/]");
        AnsiConsole.WriteLine();

        // Sprawdz mapowanie
        if (_mapping.Pracownicy.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Uwaga: Brak mapowania pracownikow! Najpierw wykonaj porownanie (opcje 4 i 5).[/]");
            return;
        }

        ExistingRecords existingInTarget = null!;
        var sourceListyPlac = new List<ListaPlac>();
        int sourceWyplaty = 0, sourceWypElementy = 0, sourceNieobecnosci = 0, sourceUmowy = 0;

        await AnsiConsole.Status()
            .StartAsync("Analizowanie...", async ctx =>
            {
                ctx.Status("Pobieranie istniejacych rekordow z bazy docelowej...");
                existingInTarget = await _targetDb!.GetExistingRecordsAsync();

                ctx.Status("Pobieranie danych ze zrodla...");
                sourceListyPlac = (await _sourceDb!.GetListyPlacAsync()).ToList();
                sourceWyplaty = await _sourceDb.GetWyplatyCountAsync();
                sourceWypElementy = await _sourceDb.GetWypElementyCountAsync();
                sourceNieobecnosci = await _sourceDb.GetNieobecnosciCountAsync();
                sourceUmowy = await _sourceDb.GetUmowyCountAsync();
            });

        // Analiza list plac (po NumerPelny lub kluczu biznesowym)
        var newListyPlac = sourceListyPlac.Where(lp =>
            (string.IsNullOrEmpty(lp.NumerPelny) || !existingInTarget.ListyPlacNumery.Contains(lp.NumerPelny))
        ).ToList();

        var table = new Table();
        table.AddColumn("Element");
        table.AddColumn("W zrodle");
        table.AddColumn("Juz w celu (klucze biz.)");
        table.AddColumn("Do migracji (przyblizone)");

        table.AddRow(
            "Listy plac",
            sourceListyPlac.Count.ToString(),
            $"{existingInTarget.ListyPlacNumery.Count} (NumerPelny) / {existingInTarget.ListyPlacKeys.Count} (klucz)",
            $"[yellow]{newListyPlac.Count}[/]");

        table.AddRow(
            "Wyplaty",
            sourceWyplaty.ToString(),
            existingInTarget.WyplatyKeys.Count.ToString(),
            "[grey]wymaga mapowania[/]");

        table.AddRow(
            "Elementy wyplat",
            sourceWypElementy.ToString(),
            existingInTarget.WypElementyKeys.Count.ToString(),
            "[grey]wymaga mapowania[/]");

        table.AddRow(
            "Nieobecnosci",
            sourceNieobecnosci.ToString(),
            existingInTarget.NieobecnosciKeys.Count.ToString(),
            "[grey]wymaga mapowania[/]");

        table.AddRow(
            "Umowy",
            sourceUmowy.ToString(),
            $"{existingInTarget.UmowyNumery.Count} (numer) / {existingInTarget.UmowyKeys.Count} (klucz)",
            "[grey]wymaga mapowania[/]");

        AnsiConsole.Write(table);

        if (newListyPlac.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Nowe listy plac do migracji:[/]");

            var lpTable = new Table();
            lpTable.AddColumn("Numer");
            lpTable.AddColumn("Data");
            lpTable.AddColumn("Okres");

            foreach (var lp in newListyPlac.Take(15))
            {
                lpTable.AddRow(
                    lp.NumerPelny ?? "-",
                    lp.Data?.ToString("yyyy-MM-dd") ?? "-",
                    $"{lp.OkresFrom:yyyy-MM-dd} - {lp.OkresTo:yyyy-MM-dd}");
            }

            if (newListyPlac.Count > 15)
                lpTable.AddRow("...", $"i {newListyPlac.Count - 15} wiecej", "");

            AnsiConsole.Write(lpTable);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[blue]Podsumowanie mapowan:[/]");

        var summaryTable = new Table();
        summaryTable.AddColumn("Mapowanie");
        summaryTable.AddColumn("Ilosc");
        summaryTable.AddColumn("Status");

        summaryTable.AddRow("Pracownicy", _mapping.Pracownicy.Count.ToString(),
            _mapping.Pracownicy.Count > 0 ? "[green]OK[/]" : "[red]BRAK[/]");
        summaryTable.AddRow("DefElementow", _mapping.DefElementow.Count.ToString(),
            _mapping.DefElementow.Count > 0 ? "[green]OK[/]" : "[yellow]Sprawdz[/]");
        summaryTable.AddRow("DefNieobecnosci", _mapping.DefNieobecnosci.Count.ToString(),
            _mapping.DefNieobecnosci.Count > 0 ? "[green]OK[/]" : "[yellow]Sprawdz[/]");
        summaryTable.AddRow("DefListPlac", _mapping.DefListPlac.Count.ToString(),
            _mapping.DefListPlac.Count > 0 ? "[green]OK[/]" : "[yellow]Sprawdz[/]");
        summaryTable.AddRow("Wydzialy", _mapping.Wydzialy.Count.ToString(),
            _mapping.Wydzialy.Count > 0 ? "[green]OK[/]" : "[yellow]Sprawdz[/]");

        AnsiConsole.Write(summaryTable);
    }

    static async Task RunDataValidationAsync()
    {
        EnsureConnections();

        AnsiConsole.MarkupLine("[blue]Walidacja jakości danych źródłowych...[/]");
        AnsiConsole.WriteLine();

        var validationService = new DataValidationService(_sourceDb!);
        ValidationReport? report = null;

        await AnsiConsole.Status()
            .StartAsync("Sprawdzanie danych...", async ctx =>
            {
                report = await validationService.ValidateAllAsync(
                    new Progress<string>(msg => ctx.Status(msg)));
            });

        if (report != null)
        {
            validationService.DisplayReport(report);

            if (report.HasCriticalIssues)
            {
                AnsiConsole.MarkupLine("[red]Wykryto krytyczne problemy! Zalecane jest ich naprawienie przed migracją.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[green]Brak krytycznych problemów.[/]");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter aby kontynuować...[/]").AllowEmpty());
    }

    static async Task RunMappingEditorAsync()
    {
        EnsureConnections();

        var categoryChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Wybierz kategorię do edycji mapowań:[/]")
                .AddChoices(new[]
                {
                    "1. DefElementow (składniki wypłat)",
                    "2. DefNieobecnosci (urlopy, zwolnienia)",
                    "3. DefListPlac (definicje list płac)",
                    "4. DefDokumentow (typy umów)",
                    "5. Wydzialy (struktura organizacyjna)",
                    "6. Kalendarze (kalendarze wzorcowe)",
                    "7. UrzedySkarbowe",
                    "0. Wróć"
                }));

        if (categoryChoice.StartsWith("0")) return;

        var category = categoryChoice[0] switch
        {
            '1' => "DefElementow",
            '2' => "DefNieobecnosci",
            '3' => "DefListPlac",
            '4' => "DefDokumentow",
            '5' => "Wydzialy",
            '6' => "Kalendarze",
            '7' => "UrzedySkarbowe",
            _ => throw new InvalidOperationException()
        };

        var editor = new MappingEditorService(_sourceDb!, _targetDb!, _mapping);
        await editor.RunEditorAsync(category);
    }

    static async Task ExecuteMigrationAsync()
    {
        EnsureConnections();

        // 1. Walidacja połączeń
        AnsiConsole.MarkupLine("[blue]Sprawdzanie polaczen z bazami danych...[/]");

        var sourceOk = _sourceDb!.TestConnection();
        var targetOk = _targetDb!.TestConnection();

        if (!sourceOk)
        {
            AnsiConsole.MarkupLine("[red]BLAD: Brak polaczenia z baza zrodlowa![/]");
            return;
        }
        if (!targetOk)
        {
            AnsiConsole.MarkupLine("[red]BLAD: Brak polaczenia z baza docelowa![/]");
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓ Baza zrodlowa:[/] {_sourceDb.GetDatabaseName()}");
        AnsiConsole.MarkupLine($"[green]✓ Baza docelowa:[/] {_targetDb.GetDatabaseName()}");
        AnsiConsole.WriteLine();

        // 2. Analiza i decyzje
        var analysisService = new AnalysisService(_sourceDb, _targetDb);
        var decisionService = new DecisionService(_targetDb);

        MigrationPlan? plan = null;
        var planFilePath = "migration_plan.json";

        // Sprawdź czy istnieje zapisany plan
        if (File.Exists(planFilePath))
        {
            var loadChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[yellow]Znaleziono zapisany plan migracji ({planFilePath}). Co chcesz zrobic?[/]")
                    .AddChoices(new[] {
                        "1. Wczytaj zapisany plan",
                        "2. Wykonaj nowa analize",
                        "0. Anuluj"
                    }));

            if (loadChoice.StartsWith("0"))
            {
                AnsiConsole.MarkupLine("[yellow]Anulowano.[/]");
                return;
            }

            if (loadChoice.StartsWith("1"))
            {
                plan = await decisionService.LoadPlanAsync(planFilePath);
                if (plan != null)
                {
                    AnsiConsole.MarkupLine($"[green]Wczytano plan z {plan.CreatedAt:yyyy-MM-dd HH:mm}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Nie udalo sie wczytac planu, wykonuje nowa analize...[/]");
                }
            }
        }

        // Wykonaj analizę jeśli brak planu
        if (plan == null)
        {
            AnsiConsole.MarkupLine("[blue]Wykonywanie analizy...[/]");
            await AnsiConsole.Status()
                .StartAsync("Analizowanie danych...", async ctx =>
                {
                    plan = await analysisService.AnalyzeAsync(
                        new Progress<string>(msg => ctx.Status(msg)));
                });
        }

        // Wyświetl podsumowanie i pozwól na decyzje
        if (plan.Issues.Any(i => i.Resolution == ResolutionType.None))
        {
            AnsiConsole.MarkupLine($"[yellow]Wykryto {plan.PendingDecisionsCount} problemow wymagajacych decyzji.[/]");
            AnsiConsole.WriteLine();

            try
            {
                var shouldMigrate = await decisionService.RunInteractiveMenuAsync(plan);

                if (!shouldMigrate)
                {
                    // Zapisz plan i wyjdź
                    await decisionService.SavePlanAsync(plan, planFilePath);
                    AnsiConsole.MarkupLine("[yellow]Plan zapisany. Mozesz kontynuowac pozniej.[/]");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Anulowano.[/]");
                return;
            }
        }

        // Zapisz plan przed migracją
        await decisionService.SavePlanAsync(plan, planFilePath);

        // Zbuduj mapowanie z planu
        AnsiConsole.MarkupLine("[blue]Budowanie mapowania na podstawie decyzji...[/]");
        _mapping = await analysisService.BuildMappingFromPlanAsync(plan);

        // 3. Backup bazy docelowej
        AnsiConsole.MarkupLine("[red]╔════════════════════════════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[red]║                    MIGRACJA DANYCH                                 ║[/]");
        AnsiConsole.MarkupLine("[red]╠════════════════════════════════════════════════════════════════════╣[/]");
        AnsiConsole.MarkupLine("[red]║  Ta operacja ZMODYFIKUJE baze docelowa!                           ║[/]");
        AnsiConsole.MarkupLine("[red]║  Zalecane jest wykonanie BACKUPU przed rozpoczeciem.              ║[/]");
        AnsiConsole.MarkupLine("[red]╚════════════════════════════════════════════════════════════════════╝[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[grey]Mapowania zbudowane z planu:[/]");
        ShowMappingStats();
        AnsiConsole.WriteLine();

        // 4. Wybór trybu
        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Wybierz tryb migracji:[/]")
                .AddChoices(new[] {
                    "1. DRY-RUN (testowy - bez zmian w bazie)",
                    "2. PRODUKCYJNY (z transakcja - rollback przy bledzie)",
                    "3. PRODUKCYJNY (bez transakcji - szybszy)",
                    "4. WZNOW poprzednia migracje (kontynuuj od miejsca przerwania)",
                    "5. PRZYROSTOWA (tylko nowe rekordy od ostatniej migracji)",
                    "0. Anuluj"
                }));

        if (mode.StartsWith("0"))
        {
            AnsiConsole.MarkupLine("[yellow]Anulowano.[/]");
            return;
        }

        var options = new MigrationOptions();

        if (mode.StartsWith("1"))
        {
            options.DryRun = true;
            options.UseTransaction = false;
            AnsiConsole.MarkupLine("[yellow]Tryb DRY-RUN - zadne dane nie zostana zmienione[/]");
        }
        else if (mode.StartsWith("2"))
        {
            options.DryRun = false;
            options.UseTransaction = true;
            AnsiConsole.MarkupLine("[green]Tryb z transakcja - przy bledzie wszystko zostanie wycofane[/]");
        }
        else if (mode.StartsWith("3"))
        {
            options.DryRun = false;
            options.UseTransaction = false;
            AnsiConsole.MarkupLine("[yellow]Tryb bez transakcji - szybszy, ale bledne rekordy pozostana[/]");
        }
        else if (mode.StartsWith("4"))
        {
            options.DryRun = false;
            options.UseTransaction = false; // Wznowienie nie używa transakcji
            options.ResumeMode = true;
            AnsiConsole.MarkupLine("[blue]Tryb WZNOWIENIA - kontynuacja od miejsca przerwania[/]");

            // Sprawdź czy istnieje stan do wznowienia
            if (!File.Exists(options.StateFilePath))
            {
                AnsiConsole.MarkupLine("[red]Brak zapisanego stanu migracji do wznowienia![/]");
                AnsiConsole.MarkupLine("[yellow]Użyj trybu 2 lub 3 aby rozpocząć nową migrację.[/]");
                return;
            }
        }
        else if (mode.StartsWith("5"))
        {
            options.DryRun = false;
            options.UseTransaction = false;
            options.IncrementalMode = true;
            AnsiConsole.MarkupLine("[blue]Tryb PRZYROSTOWY - tylko rekordy nie migrowane wcześniej[/]");
        }

        AnsiConsole.WriteLine();

        // Backup przed migracją produkcyjną
        if (!options.DryRun)
        {
            var backupService = new BackupService(_config!.TargetConnectionString);

            // Sprawdź uprawnienia do backupu
            var (canBackup, permMessage) = await backupService.CheckBackupPermissionsAsync();

            if (canBackup)
            {
                var backupChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[yellow]Czy wykonac backup bazy docelowej przed migracja?[/]")
                        .AddChoices(new[] {
                            "1. Tak, wykonaj backup (ZALECANE)",
                            "2. Nie, kontynuuj bez backupu",
                            "0. Anuluj migracje"
                        }));

                if (backupChoice.StartsWith("0"))
                {
                    AnsiConsole.MarkupLine("[yellow]Anulowano.[/]");
                    return;
                }

                if (backupChoice.StartsWith("1"))
                {
                    AnsiConsole.WriteLine();
                    string? backupPath = null;

                    await AnsiConsole.Status()
                        .StartAsync("Tworzenie backupu...", async ctx =>
                        {
                            backupPath = await backupService.CreateBackupAsync(
                                new Progress<string>(msg => ctx.Status(msg)));
                        });

                    if (backupPath != null)
                    {
                        options.BackupFilePath = backupPath;
                        AnsiConsole.MarkupLine($"[green]Backup utworzony:[/] {backupPath}");
                        AnsiConsole.WriteLine();
                    }
                    else
                    {
                        var continueAnyway = AnsiConsole.Confirm(
                            "[yellow]Nie udalo sie utworzyc backupu. Czy kontynuowac migracje?[/]",
                            defaultValue: false);

                        if (!continueAnyway)
                        {
                            AnsiConsole.MarkupLine("[yellow]Anulowano.[/]");
                            return;
                        }
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Kontynuacja bez backupu - na wlasna odpowiedzialnosc![/]");
                    AnsiConsole.WriteLine();
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Automatyczny backup niedostepny: {permMessage}[/]");
                AnsiConsole.MarkupLine("[yellow]Upewnij sie, ze masz reczny backup bazy docelowej![/]");
                AnsiConsole.WriteLine();
            }

            AnsiConsole.MarkupLine("[red]Aby potwierdzic migracje, wpisz: MIGRUJ[/]");
            var migrationConfirm = AnsiConsole.Ask<string>("Potwierdzenie:");
            if (migrationConfirm != "MIGRUJ")
            {
                AnsiConsole.MarkupLine("[yellow]Anulowano.[/]");
                return;
            }
        }

        AnsiConsole.WriteLine();

        // Utwórz audit log
        var auditLog = new AuditLogService();
        var migrationService = new MigrationService(_sourceDb!, _targetDb!, _mapping, options, auditLog);
        MigrationResult result = null!;

        // Użyj ProgressTracker do lepszego wyświetlania postępu
        var tracker = migrationService.ProgressTracker;
        tracker.Start();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                // Główny pasek postępu (13 tabel)
                var mainTask = ctx.AddTask("[blue]Migracja ogolna[/]", maxValue: 13);

                // Pasek dla aktualnej tabeli
                var tableTask = ctx.AddTask("[grey]Oczekiwanie...[/]", maxValue: 100);
                tableTask.IsIndeterminate = true;

                var lastTable = "";
                var lastTableRecords = 0;

                var progress = new Progress<string>(msg =>
                {
                    // Parse msg to update progress bars
                    // Format: "(X/13) TableName: current/total" or "(X/13) TableName..."
                    if (msg.StartsWith("(") && msg.Contains("/13)"))
                    {
                        var parts = msg.Split(") ", 2);
                        if (parts.Length >= 2)
                        {
                            var tableInfo = parts[1];
                            var tableName = tableInfo.Split(":")[0].Trim();

                            // Nowa tabela
                            if (tableName != lastTable)
                            {
                                lastTable = tableName;
                                tableTask.Description = $"[green]{tableName}[/]";
                                tableTask.Value = 0;
                                tableTask.IsIndeterminate = true;
                                lastTableRecords = 0;

                                // Parsuj numer tabeli
                                var numPart = parts[0].Replace("(", "").Replace("/13", "");
                                if (int.TryParse(numPart, out var tableNum))
                                {
                                    mainTask.Value = tableNum - 1;
                                }
                            }

                            // Parsuj postęp w tabeli (format: "X/Y")
                            if (tableInfo.Contains(": ") && tableInfo.Contains("/"))
                            {
                                var progressPart = tableInfo.Split(": ", 2);
                                if (progressPart.Length >= 2)
                                {
                                    var numbers = progressPart[1].Split("/");
                                    if (numbers.Length >= 2 &&
                                        int.TryParse(numbers[0], out var current) &&
                                        int.TryParse(numbers[1], out var total) &&
                                        total > 0)
                                    {
                                        tableTask.IsIndeterminate = false;
                                        tableTask.MaxValue = total;
                                        tableTask.Value = current;
                                        lastTableRecords = total;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Inne komunikaty (np. "Wyłączanie triggerów...")
                        tableTask.Description = $"[grey]{Markup.Escape(msg)}[/]";
                    }
                });

                result = await migrationService.MigrateAllAsync(progress);

                mainTask.Value = 13;
                tableTask.Value = tableTask.MaxValue;
                tableTask.Description = "[green]Zakończono[/]";
            });

        tracker.Stop();

        // Wyswietl wyniki
        AnsiConsole.WriteLine();

        if (options.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]===== DRY-RUN ZAKONCZONY (bez zmian) =====[/]");
        }
        else if (result.Success)
        {
            AnsiConsole.MarkupLine("[green]===== MIGRACJA ZAKONCZONA POMYSLNIE =====[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]===== MIGRACJA ZAKONCZONA Z BLEDAMI =====[/]");
            AnsiConsole.MarkupLine($"[red]Blad: {result.ErrorMessage}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Log zapisany do: {options.LogFilePath}[/]");
        AnsiConsole.WriteLine();

        // Tabela statystyk
        var statsTable = new Table();
        statsTable.AddColumn("Tabela");
        statsTable.AddColumn("Razem");
        statsTable.AddColumn(options.DryRun ? "Do migracji" : "Zmigrowne");
        statsTable.AddColumn("Pominiete");
        statsTable.AddColumn("Bledy");

        var stats = result.Stats;

        void AddStatsRow(string name, int total, int migrated, int skipped, int errors)
        {
            if (total > 0 || migrated > 0)
            {
                statsTable.AddRow(name,
                    total.ToString(),
                    migrated > 0 ? $"[green]{migrated}[/]" : "0",
                    skipped.ToString(),
                    errors > 0 ? $"[red]{errors}[/]" : "0");
            }
        }

        AddStatsRow("ListyPlac", stats.ListyPlacTotal, stats.ListyPlacMigrated, stats.ListyPlacSkipped, stats.ListyPlacErrors);
        AddStatsRow("Wyplaty", stats.WyplatyTotal, stats.WyplatyMigrated, stats.WyplatySkipped, stats.WyplatyErrors);
        AddStatsRow("WypElementy", stats.WypElementyTotal, stats.WypElementyMigrated, stats.WypElementySkipped, stats.WypElementyErrors);
        AddStatsRow("Nieobecnosci", stats.NieobecnosciTotal, stats.NieobecnosciMigrated, stats.NieobecnosciSkipped, stats.NieobecnosciErrors);
        AddStatsRow("Umowy", stats.UmowyTotal, stats.UmowyMigrated, stats.UmowySkipped, stats.UmowyErrors);
        AddStatsRow("Rodzina", stats.RodzinaTotal, stats.RodzinaMigrated, stats.RodzinaSkipped, stats.RodzinaErrors);
        AddStatsRow("Dodatki", stats.DodatkiTotal, stats.DodatkiMigrated, stats.DodatkiSkipped, stats.DodatkiErrors);
        AddStatsRow("Adresy", stats.AdresyTotal, stats.AdresyMigrated, stats.AdresySkipped, stats.AdresyErrors);
        AddStatsRow("RachBankPodmiot", stats.RachunkiTotal, stats.RachunkiMigrated, stats.RachunkiSkipped, stats.RachunkiErrors);
        AddStatsRow("PracHistorie", stats.PracHistorieTotal, stats.PracHistorieMigrated, stats.PracHistorieSkipped, stats.PracHistorieErrors);
        AddStatsRow("Kalendarze", stats.KalendarzeTotal, stats.KalendarzeMigrated, stats.KalendarzeSkipped, stats.KalendarzeErrors);
        AddStatsRow("HistZatrudnien", stats.HistZatrudnienTotal, stats.HistZatrudnienMigrated, stats.HistZatrudnienSkipped, stats.HistZatrudnienErrors);

        AnsiConsole.Write(statsTable);

        // Podsumowanie
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[blue]Razem {(options.DryRun ? "do migracji" : "zmigrowno")}: {stats.TotalMigrated} rekordow[/]");

        if (stats.TotalErrors > 0)
        {
            AnsiConsole.MarkupLine($"[red]Razem bledow: {stats.TotalErrors}[/]");

            if (stats.Errors.Count > 0 && AnsiConsole.Confirm("Czy wyswietlic szczegoly bledow?", false))
            {
                AnsiConsole.WriteLine();
                foreach (var error in stats.Errors.Take(50))
                {
                    AnsiConsole.MarkupLine($"[red]  - {Markup.Escape(error)}[/]");
                }

                if (stats.Errors.Count > 50)
                {
                    AnsiConsole.MarkupLine($"[grey]  ... i {stats.Errors.Count - 50} wiecej[/]");
                }
            }
        }

        // Generuj raporty
        if (!options.DryRun)
        {
            AnsiConsole.WriteLine();
            var generateReports = AnsiConsole.Confirm("Czy wygenerowac raporty (audit CSV + HTML)?", true);

            if (generateReports)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[blue]Generowanie raportow...[/]");

                // Audit CSV
                var auditFiles = auditLog.ExportAll();
                foreach (var file in auditFiles)
                {
                    AnsiConsole.MarkupLine($"[grey]  Audit: {file}[/]");
                }

                // Raport HTML
                var reportGenerator = new ReportGeneratorService();
                var htmlReportPath = reportGenerator.SaveHtmlReport(result, auditLog);
                AnsiConsole.MarkupLine($"[green]  Raport HTML: {htmlReportPath}[/]");
            }
        }

        // Zapisz zaktualizowane mapowanie
        if (!options.DryRun && stats.TotalMigrated > 0)
        {
            AnsiConsole.WriteLine();
            if (AnsiConsole.Confirm("Czy zapisac zaktualizowane mapowanie (z nowymi ID)?", true))
            {
                await SaveMappingAsync();
            }
        }
    }

    static void EnsureConnections()
    {
        if (string.IsNullOrEmpty(_config.SourceConnectionString) ||
            string.IsNullOrEmpty(_config.TargetConnectionString))
        {
            throw new InvalidOperationException("Brak konfiguracji polaczen! Uzyj opcji 1 aby skonfigurowac.");
        }

        _sourceDb ??= new DatabaseService(_config.SourceConnectionString);
        _targetDb ??= new DatabaseService(_config.TargetConnectionString);
    }
}
