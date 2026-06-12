using Spectre.Console;
using EnovaMigrator.Configuration;

namespace EnovaMigrator.Services;

/// <summary>
/// Interaktywny edytor mapowań z fuzzy matching i bulk operations.
/// </summary>
public class MappingEditorService
{
    private readonly DatabaseService _sourceDb;
    private readonly DatabaseService _targetDb;
    private readonly MappingData _mapping;

    public MappingEditorService(DatabaseService sourceDb, DatabaseService targetDb, MappingData mapping)
    {
        _sourceDb = sourceDb;
        _targetDb = targetDb;
        _mapping = mapping;
    }

    /// <summary>
    /// Uruchamia interaktywny edytor mapowań dla wybranej kategorii.
    /// </summary>
    public async Task RunEditorAsync(string category)
    {
        var sourceItems = await GetSourceItemsAsync(category);
        var targetItems = await GetTargetItemsAsync(category);
        var currentMappings = GetCurrentMappings(category);

        while (true)
        {
            Console.Clear();
            AnsiConsole.MarkupLine($"[blue]Edytor mapowań: {category}[/]");
            AnsiConsole.MarkupLine($"[grey]Źródło: {sourceItems.Count} | Cel: {targetItems.Count} | Zmapowane: {currentMappings.Count}[/]");
            AnsiConsole.WriteLine();

            // Pokaż statystyki
            var unmapped = sourceItems.Where(s => !currentMappings.ContainsKey(s.Id)).ToList();
            var mapped = sourceItems.Where(s => currentMappings.ContainsKey(s.Id)).ToList();

            AnsiConsole.MarkupLine($"[green]Zmapowane:[/] {mapped.Count}");
            AnsiConsole.MarkupLine($"[yellow]Do zmapowania:[/] {unmapped.Count}");
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Co chcesz zrobić?[/]")
                    .AddChoices(new[]
                    {
                        "1. Pokaż niezmapowane (z sugestiami)",
                        "2. Pokaż wszystkie mapowania",
                        "3. Auto-mapuj po nazwie (dokładne dopasowanie)",
                        "4. Auto-mapuj z fuzzy matching (podobne nazwy)",
                        "5. Mapuj ręcznie wybrane",
                        "6. Bulk: Mapuj wiele źródeł na jeden cel",
                        "7. Usuń mapowanie",
                        "0. Wróć do menu"
                    }));

            if (choice.StartsWith("0")) break;

            if (choice.StartsWith("1"))
            {
                await ShowUnmappedWithSuggestionsAsync(category, unmapped, targetItems);
            }
            else if (choice.StartsWith("2"))
            {
                ShowAllMappings(category, sourceItems, targetItems, currentMappings);
            }
            else if (choice.StartsWith("3"))
            {
                AutoMapExact(category, unmapped, targetItems);
            }
            else if (choice.StartsWith("4"))
            {
                await AutoMapFuzzyAsync(category, unmapped, targetItems);
            }
            else if (choice.StartsWith("5"))
            {
                await ManualMapAsync(category, unmapped, targetItems);
            }
            else if (choice.StartsWith("6"))
            {
                await BulkMapAsync(category, unmapped, targetItems);
            }
            else if (choice.StartsWith("7"))
            {
                RemoveMapping(category, mapped, currentMappings);
            }

            // Odśwież mapowania
            currentMappings = GetCurrentMappings(category);
        }
    }

    /// <summary>
    /// Pokazuje niezmapowane elementy z sugestiami dopasowań.
    /// </summary>
    private async Task ShowUnmappedWithSuggestionsAsync(
        string category,
        List<(int Id, string Name)> unmapped,
        List<(int Id, string Name)> targets)
    {
        if (!unmapped.Any())
        {
            AnsiConsole.MarkupLine("[green]Wszystkie elementy są zmapowane![/]");
            AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("ID")
            .AddColumn("Nazwa źródłowa")
            .AddColumn("Sugerowane dopasowanie")
            .AddColumn("Podobieństwo");

        foreach (var item in unmapped.Take(20)) // Pokaż max 20
        {
            var suggestions = targets
                .Select(t => new { Target = t, Similarity = FuzzyMatchService.CalculateSimilarity(item.Name, t.Name) })
                .Where(x => x.Similarity >= 40)
                .OrderByDescending(x => x.Similarity)
                .Take(1)
                .ToList();

            if (suggestions.Any())
            {
                var best = suggestions.First();
                var color = best.Similarity >= 80 ? "green" : best.Similarity >= 60 ? "yellow" : "grey";
                table.AddRow(
                    item.Id.ToString(),
                    item.Name,
                    $"[{color}]{best.Target.Name}[/]",
                    $"[{color}]{best.Similarity:F0}%[/]");
            }
            else
            {
                table.AddRow(
                    item.Id.ToString(),
                    item.Name,
                    "[red]Brak sugestii[/]",
                    "-");
            }
        }

        AnsiConsole.Write(table);

        if (unmapped.Count > 20)
        {
            AnsiConsole.MarkupLine($"[grey]... i {unmapped.Count - 20} więcej[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
    }

    /// <summary>
    /// Pokazuje wszystkie aktualne mapowania.
    /// </summary>
    private void ShowAllMappings(
        string category,
        List<(int Id, string Name)> sources,
        List<(int Id, string Name)> targets,
        Dictionary<int, int> mappings)
    {
        if (!mappings.Any())
        {
            AnsiConsole.MarkupLine("[yellow]Brak mapowań.[/]");
            AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Source ID")
            .AddColumn("Nazwa źródłowa")
            .AddColumn("->")
            .AddColumn("Target ID")
            .AddColumn("Nazwa docelowa");

        foreach (var (sourceId, targetId) in mappings.Take(30))
        {
            var sourceName = sources.FirstOrDefault(s => s.Id == sourceId).Name ?? "?";
            var targetName = targets.FirstOrDefault(t => t.Id == targetId).Name ?? "?";

            table.AddRow(
                sourceId.ToString(),
                sourceName,
                "->",
                targetId.ToString(),
                targetName);
        }

        AnsiConsole.Write(table);

        if (mappings.Count > 30)
        {
            AnsiConsole.MarkupLine($"[grey]... i {mappings.Count - 30} więcej[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
    }

    /// <summary>
    /// Auto-mapowanie po dokładnej nazwie.
    /// </summary>
    private void AutoMapExact(
        string category,
        List<(int Id, string Name)> unmapped,
        List<(int Id, string Name)> targets)
    {
        var mappingsDict = GetMappingsDictionary(category);
        var mapped = 0;

        foreach (var source in unmapped)
        {
            var exactMatch = targets.FirstOrDefault(t =>
                string.Equals(t.Name, source.Name, StringComparison.OrdinalIgnoreCase));

            if (exactMatch.Id != 0)
            {
                mappingsDict[source.Id] = exactMatch.Id;
                mapped++;
            }
        }

        AnsiConsole.MarkupLine($"[green]Zmapowano {mapped} elementów po dokładnej nazwie.[/]");
        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
    }

    /// <summary>
    /// Auto-mapowanie z fuzzy matching.
    /// </summary>
    private async Task AutoMapFuzzyAsync(
        string category,
        List<(int Id, string Name)> unmapped,
        List<(int Id, string Name)> targets)
    {
        var threshold = AnsiConsole.Ask<int>(
            "[yellow]Minimalny próg podobieństwa (50-100%):[/]", 80);

        threshold = Math.Clamp(threshold, 50, 100);

        var suggestions = new List<MatchSuggestion>();

        await AnsiConsole.Status()
            .StartAsync("Analizowanie podobieństw...", async ctx =>
            {
                await Task.Run(() =>
                {
                    foreach (var source in unmapped)
                    {
                        var best = targets
                            .Select(t => new { Target = t, Similarity = FuzzyMatchService.CalculateSimilarity(source.Name, t.Name) })
                            .Where(x => x.Similarity >= threshold)
                            .OrderByDescending(x => x.Similarity)
                            .FirstOrDefault();

                        if (best != null)
                        {
                            suggestions.Add(new MatchSuggestion
                            {
                                SourceId = source.Id,
                                SourceName = source.Name,
                                TargetId = best.Target.Id,
                                TargetName = best.Target.Name,
                                Similarity = best.Similarity
                            });
                        }
                    }
                });
            });

        if (!suggestions.Any())
        {
            AnsiConsole.MarkupLine($"[yellow]Brak dopasowań z podobieństwem >= {threshold}%[/]");
            AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
            return;
        }

        // Pokaż sugestie
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Źródło")
            .AddColumn("->")
            .AddColumn("Cel")
            .AddColumn("%");

        foreach (var s in suggestions.Take(20))
        {
            var color = s.Similarity >= 90 ? "green" : s.Similarity >= 70 ? "yellow" : "grey";
            table.AddRow(
                s.SourceName,
                "->",
                $"[{color}]{s.TargetName}[/]",
                $"[{color}]{s.SimilarityDisplay}[/]");
        }

        AnsiConsole.Write(table);

        if (suggestions.Count > 20)
        {
            AnsiConsole.MarkupLine($"[grey]... i {suggestions.Count - 20} więcej[/]");
        }

        AnsiConsole.WriteLine();

        if (AnsiConsole.Confirm($"[yellow]Zaakceptować {suggestions.Count} sugerowanych mapowań?[/]"))
        {
            var mappingsDict = GetMappingsDictionary(category);
            foreach (var s in suggestions)
            {
                mappingsDict[s.SourceId] = s.TargetId;
            }
            AnsiConsole.MarkupLine($"[green]Dodano {suggestions.Count} mapowań.[/]");
        }

        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
    }

    /// <summary>
    /// Ręczne mapowanie wybranych elementów.
    /// </summary>
    private async Task ManualMapAsync(
        string category,
        List<(int Id, string Name)> unmapped,
        List<(int Id, string Name)> targets)
    {
        if (!unmapped.Any())
        {
            AnsiConsole.MarkupLine("[green]Wszystkie elementy są zmapowane![/]");
            AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
            return;
        }

        // Wybierz źródło
        var sourceChoices = unmapped
            .Select(u => $"{u.Id}: {u.Name}")
            .Prepend("0: << Wróć")
            .ToList();

        var sourceChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Wybierz element źródłowy do zmapowania:[/]")
                .PageSize(15)
                .AddChoices(sourceChoices));

        if (sourceChoice.StartsWith("0:")) return;

        var sourceId = int.Parse(sourceChoice.Split(':')[0]);
        var sourceItem = unmapped.First(u => u.Id == sourceId);

        // Pokaż sugestie
        var suggestions = targets
            .Select(t => new { Target = t, Similarity = FuzzyMatchService.CalculateSimilarity(sourceItem.Name, t.Name) })
            .OrderByDescending(x => x.Similarity)
            .Take(10)
            .ToList();

        AnsiConsole.MarkupLine($"[blue]Mapowanie:[/] {sourceItem.Name}");
        AnsiConsole.MarkupLine("[grey]Sugestie (posortowane po podobieństwie):[/]");

        // Wybierz cel
        var targetChoices = suggestions
            .Select(s => $"{s.Target.Id}: {s.Target.Name} ({s.Similarity:F0}%)")
            .Concat(targets.Except(suggestions.Select(s => s.Target)).Select(t => $"{t.Id}: {t.Name}"))
            .Prepend("0: << Anuluj")
            .ToList();

        var targetChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Wybierz element docelowy:[/]")
                .PageSize(15)
                .EnableSearch()
                .AddChoices(targetChoices));

        if (targetChoice.StartsWith("0:")) return;

        var targetId = int.Parse(targetChoice.Split(':')[0]);
        var mappingsDict = GetMappingsDictionary(category);
        mappingsDict[sourceId] = targetId;

        AnsiConsole.MarkupLine($"[green]Zmapowano: {sourceItem.Name} -> {targets.First(t => t.Id == targetId).Name}[/]");
        await Task.Delay(500);
    }

    /// <summary>
    /// Bulk mapping - wiele źródeł na jeden cel.
    /// </summary>
    private async Task BulkMapAsync(
        string category,
        List<(int Id, string Name)> unmapped,
        List<(int Id, string Name)> targets)
    {
        if (!unmapped.Any())
        {
            AnsiConsole.MarkupLine("[green]Wszystkie elementy są zmapowane![/]");
            AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
            return;
        }

        // Wybierz cel
        var targetChoices = targets
            .Select(t => $"{t.Id}: {t.Name}")
            .Prepend("0: << Wróć")
            .ToList();

        var targetChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Wybierz element DOCELOWY (na który zmapujesz wiele źródeł):[/]")
                .PageSize(15)
                .EnableSearch()
                .AddChoices(targetChoices));

        if (targetChoice.StartsWith("0:")) return;

        var targetId = int.Parse(targetChoice.Split(':')[0]);
        var targetItem = targets.First(t => t.Id == targetId);

        // Multi-select źródeł
        var sourceChoices = unmapped.Select(u => $"{u.Id}: {u.Name}").ToList();

        var selectedSources = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title($"[yellow]Wybierz elementy ŹRÓDŁOWE do zmapowania na: {targetItem.Name}[/]")
                .PageSize(15)
                .InstructionsText("[grey](Spacja = zaznacz, Enter = potwierdź)[/]")
                .AddChoices(sourceChoices));

        if (!selectedSources.Any())
        {
            AnsiConsole.MarkupLine("[yellow]Nie wybrano żadnych elementów.[/]");
            return;
        }

        var mappingsDict = GetMappingsDictionary(category);
        foreach (var selected in selectedSources)
        {
            var sourceId = int.Parse(selected.Split(':')[0]);
            mappingsDict[sourceId] = targetId;
        }

        AnsiConsole.MarkupLine($"[green]Zmapowano {selectedSources.Count} elementów na: {targetItem.Name}[/]");
        await Task.Delay(500);
    }

    /// <summary>
    /// Usuwa wybrane mapowanie.
    /// </summary>
    private void RemoveMapping(
        string category,
        List<(int Id, string Name)> mapped,
        Dictionary<int, int> currentMappings)
    {
        if (!currentMappings.Any())
        {
            AnsiConsole.MarkupLine("[yellow]Brak mapowań do usunięcia.[/]");
            AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
            return;
        }

        var choices = mapped
            .Where(m => currentMappings.ContainsKey(m.Id))
            .Select(m => $"{m.Id}: {m.Name}")
            .Prepend("0: << Wróć")
            .ToList();

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[yellow]Wybierz mapowania do usunięcia:[/]")
                .PageSize(15)
                .AddChoices(choices));

        if (selected.All(s => s.StartsWith("0:"))) return;

        var mappingsDict = GetMappingsDictionary(category);
        var removed = 0;

        foreach (var sel in selected.Where(s => !s.StartsWith("0:")))
        {
            var sourceId = int.Parse(sel.Split(':')[0]);
            if (mappingsDict.Remove(sourceId))
                removed++;
        }

        AnsiConsole.MarkupLine($"[green]Usunięto {removed} mapowań.[/]");
        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Naciśnij Enter...[/]").AllowEmpty());
    }

    #region Data Access

    private async Task<List<(int Id, string Name)>> GetSourceItemsAsync(string category)
    {
        var result = new List<(int Id, string Name)>();

        IEnumerable<dynamic> data = category switch
        {
            "DefElementow" => await _sourceDb.GetDefElementowAsync(),
            "DefNieobecnosci" => await _sourceDb.GetDefNieobecnosciAsync(),
            "DefListPlac" => await _sourceDb.GetDefListPlacAsync(),
            "DefDokumentow" => await _sourceDb.GetDefDokumentowAsync(),
            "UrzedySkarbowe" => await _sourceDb.GetUrzedySkarboweAsync(),
            "Wydzialy" => await _sourceDb.GetWydzialyAsync(),
            "Kalendarze" => await _sourceDb.GetKalendarzeWzorcoweAsync(),
            _ => Enumerable.Empty<dynamic>()
        };

        foreach (var item in data)
        {
            var dict = (IDictionary<string, object>)item;
            var id = Convert.ToInt32(dict["ID"]);
            var name = dict.ContainsKey("Nazwa") ? dict["Nazwa"]?.ToString() ?? "" : "";
            result.Add((id, name));
        }

        return result;
    }

    private async Task<List<(int Id, string Name)>> GetTargetItemsAsync(string category)
    {
        var result = new List<(int Id, string Name)>();

        IEnumerable<dynamic> data = category switch
        {
            "DefElementow" => await _targetDb.GetDefElementowAsync(),
            "DefNieobecnosci" => await _targetDb.GetDefNieobecnosciAsync(),
            "DefListPlac" => await _targetDb.GetDefListPlacAsync(),
            "DefDokumentow" => await _targetDb.GetDefDokumentowAsync(),
            "UrzedySkarbowe" => await _targetDb.GetUrzedySkarboweAsync(),
            "Wydzialy" => await _targetDb.GetWydzialyAsync(),
            "Kalendarze" => await _targetDb.GetKalendarzeWzorcoweAsync(),
            _ => Enumerable.Empty<dynamic>()
        };

        foreach (var item in data)
        {
            var dict = (IDictionary<string, object>)item;
            var id = Convert.ToInt32(dict["ID"]);
            var name = dict.ContainsKey("Nazwa") ? dict["Nazwa"]?.ToString() ?? "" : "";
            result.Add((id, name));
        }

        return result;
    }

    private Dictionary<int, int> GetCurrentMappings(string category)
    {
        return category switch
        {
            "DefElementow" => _mapping.DefElementow,
            "DefNieobecnosci" => _mapping.DefNieobecnosci,
            "DefListPlac" => _mapping.DefListPlac,
            "DefDokumentow" => _mapping.DefDokumentow,
            "UrzedySkarbowe" => _mapping.UrzedySkarbowe,
            "Wydzialy" => _mapping.Wydzialy,
            "Kalendarze" => _mapping.Kalendarze,
            _ => new Dictionary<int, int>()
        };
    }

    private Dictionary<int, int> GetMappingsDictionary(string category)
    {
        return category switch
        {
            "DefElementow" => _mapping.DefElementow,
            "DefNieobecnosci" => _mapping.DefNieobecnosci,
            "DefListPlac" => _mapping.DefListPlac,
            "DefDokumentow" => _mapping.DefDokumentow,
            "UrzedySkarbowe" => _mapping.UrzedySkarbowe,
            "Wydzialy" => _mapping.Wydzialy,
            "Kalendarze" => _mapping.Kalendarze,
            _ => throw new ArgumentException($"Nieznana kategoria: {category}")
        };
    }

    #endregion
}
