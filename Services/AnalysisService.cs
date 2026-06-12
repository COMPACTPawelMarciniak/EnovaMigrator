using EnovaMigrator.Models;
using EnovaMigrator.Configuration;

namespace EnovaMigrator.Services;

/// <summary>
/// Serwis analizujący dane źródłowe i docelowe, wykrywający wszystkie problemy
/// wymagające decyzji użytkownika przed migracją.
/// </summary>
public class AnalysisService
{
    private readonly DatabaseService _sourceDb;
    private readonly DatabaseService _targetDb;
    private readonly ComparisonService _comparisonService;
    private int _issueIdCounter = 0;

    public AnalysisService(DatabaseService sourceDb, DatabaseService targetDb)
    {
        _sourceDb = sourceDb;
        _targetDb = targetDb;
        _comparisonService = new ComparisonService(sourceDb, targetDb);
    }

    /// <summary>
    /// Wykonuje pełną analizę i zwraca plan migracji z wszystkimi wykrytymi problemami
    /// </summary>
    public async Task<MigrationPlan> AnalyzeAsync(IProgress<string>? progress = null)
    {
        var plan = new MigrationPlan
        {
            SourceDatabase = _sourceDb.GetDatabaseName(),
            TargetDatabase = _targetDb.GetDatabaseName()
        };

        progress?.Report("Pobieranie istniejących rekordów z bazy docelowej...");
        var existing = await _targetDb.GetExistingRecordsAsync();

        // 1. Porównaj definicje i wykryj brakujące mapowania
        progress?.Report("Analiza definicji elementów...");
        var defElementow = await _comparisonService.CompareDefElementowAsync();
        await AnalyzeDefinitionIssuesAsync(plan, defElementow, IssueType.MissingDefElementow, "DefElementow", progress);

        progress?.Report("Analiza definicji nieobecności...");
        var defNieobecnosci = await _comparisonService.CompareDefNieobecnosciAsync();
        await AnalyzeDefinitionIssuesAsync(plan, defNieobecnosci, IssueType.MissingDefNieobecnosci, "DefNieobecnosci", progress);

        progress?.Report("Analiza definicji list płac...");
        var defListPlac = await _comparisonService.CompareDefListPlacAsync();
        await AnalyzeDefinitionIssuesAsync(plan, defListPlac, IssueType.MissingDefListPlac, "DefListPlac", progress);

        progress?.Report("Analiza definicji dokumentów...");
        var defDokumentow = await _comparisonService.CompareDefDokumentowAsync();
        await AnalyzeDefinitionIssuesAsync(plan, defDokumentow, IssueType.MissingDefDokumentow, "DefDokumentow", progress);

        progress?.Report("Analiza urzędów skarbowych...");
        var urzedySkarbowe = await _comparisonService.CompareUrzedySkarboweAsync();
        await AnalyzeDefinitionIssuesAsync(plan, urzedySkarbowe, IssueType.MissingUrzadSkarbowy, "UrzedySkarbowe", progress);

        progress?.Report("Analiza wydziałów...");
        var wydzialy = await _comparisonService.CompareWydzialyAsync();
        await AnalyzeDefinitionIssuesAsync(plan, wydzialy, IssueType.MissingWydzial, "Wydzialy", progress);

        progress?.Report("Analiza kalendarzy wzorcowych...");
        var kalendarze = await _comparisonService.CompareKalendarzeAsync();
        await AnalyzeDefinitionIssuesAsync(plan, kalendarze, IssueType.MissingKalendarz, "Kalendarze", progress);

        // 2. Analiza pracowników
        progress?.Report("Analiza pracowników...");
        var pracownicy = await _comparisonService.ComparePracownicyAsync();
        await AnalyzePracownicyIssuesAsync(plan, pracownicy, existing, progress);

        // 3. Buduj tymczasowe mapowanie (tylko dla dopasowanych)
        var tempMapping = _comparisonService.BuildMappingFromComparisons(
            defElementow, defNieobecnosci, defListPlac, wydzialy, pracownicy,
            defDokumentow, kalendarze, urzedySkarbowe);

        // 4. Analiza duplikatów w danych
        progress?.Report("Analiza duplikatów umów...");
        await AnalyzeUmowyDuplicatesAsync(plan, existing, tempMapping, progress);

        progress?.Report("Analiza duplikatów list płac...");
        await AnalyzeListyPlacDuplicatesAsync(plan, existing, tempMapping, progress);

        // 5. Zlicz statystyki
        progress?.Report("Obliczanie statystyk...");
        await CalculateStatsAsync(plan, existing, tempMapping,
            defElementow, defNieobecnosci, defListPlac, defDokumentow,
            urzedySkarbowe, wydzialy, kalendarze, pracownicy);

        progress?.Report("Analiza zakończona.");
        return plan;
    }

    private async Task AnalyzeDefinitionIssuesAsync(
        MigrationPlan plan,
        List<DefinitionComparison> comparisons,
        IssueType issueType,
        string category,
        IProgress<string>? progress)
    {
        var missing = comparisons.Where(c => !c.IsMatched).ToList();
        if (!missing.Any()) return;

        // Pobierz możliwe cele mapowania (wszystkie definicje w target)
        var targetDefinitions = await GetTargetDefinitionsAsync(category);

        foreach (var item in missing)
        {
            // Policz ile rekordów używa tej definicji
            var affectedCount = await CountAffectedRecordsAsync(category, item.SourceID);

            var issue = new MigrationIssue
            {
                Id = ++_issueIdCounter,
                Type = issueType,
                Category = category,
                SourceId = item.SourceID,
                Name = item.Name,
                Code = item.Code,
                Description = $"Brak odpowiednika w bazie docelowej",
                AffectedRecordsCount = affectedCount,
                AvailableResolutions = BuildResolutionOptions(issueType, targetDefinitions)
            };

            plan.Issues.Add(issue);
        }
    }

    private async Task AnalyzePracownicyIssuesAsync(
        MigrationPlan plan,
        List<PracownikComparison> comparisons,
        ExistingRecords existing,
        IProgress<string>? progress)
    {
        // Pobierz wszystkich pracowników ze źródła
        var allSourcePracownicy = await _sourceDb.GetPracownicyAsync();

        foreach (var prac in allSourcePracownicy)
        {
            var comparison = comparisons.FirstOrDefault(c => c.SourceID == prac.ID);
            var isMatched = comparison?.IsMatched ?? false;

            if (!isMatched)
            {
                // Sprawdź czy to nowy pracownik czy brak mapowania
                var hasData = await HasPracownikDataAsync(prac.ID);

                var issue = new MigrationIssue
                {
                    Id = ++_issueIdCounter,
                    Type = IssueType.NewPracownik,
                    Category = "Pracownicy",
                    SourceId = prac.ID,
                    Name = $"{prac.Imie} {prac.Nazwisko}",
                    Code = prac.PESEL,
                    Description = string.IsNullOrEmpty(prac.PESEL)
                        ? "Nowy pracownik bez PESEL - wymaga weryfikacji"
                        : "Nowy pracownik do migracji",
                    AffectedRecordsCount = hasData ? 1 : 0,
                    AvailableResolutions = new List<ResolutionOption>
                    {
                        new() { Type = ResolutionType.Migrate, Label = "Migruj", Description = "Utwórz nowego pracownika w bazie docelowej" },
                        new() { Type = ResolutionType.Skip, Label = "Pomiń", Description = "Nie migruj tego pracownika i jego danych" }
                    }
                };

                // Domyślnie migruj pracowników z PESEL, wymagaj decyzji dla pozostałych
                if (!string.IsNullOrEmpty(prac.PESEL))
                {
                    issue.Resolution = ResolutionType.Migrate;
                }

                plan.Issues.Add(issue);
            }
        }
    }

    private async Task AnalyzeUmowyDuplicatesAsync(
        MigrationPlan plan,
        ExistingRecords existing,
        MappingData mapping,
        IProgress<string>? progress)
    {
        var sourceUmowy = await _sourceDb.QueryAsync("SELECT ID, Pracownik, NumerPelny, Data FROM Umowy");

        foreach (var umowa in sourceUmowy)
        {
            var dict = (IDictionary<string, object>)umowa;
            var sourcePracownik = dict["Pracownik"] != null ? Convert.ToInt32(dict["Pracownik"]) : 0;
            var numerPelny = dict["NumerPelny"]?.ToString();
            var dataOd = dict["Data"] as DateTime?;

            // Sprawdź czy mamy mapowanie pracownika
            if (!mapping.Pracownicy.TryGetValue(sourcePracownik, out var targetPracownik))
                continue; // Brak mapowania - będzie obsłużone przez MissingPracownik

            // Sprawdź duplikat po NumerPelny
            if (!string.IsNullOrEmpty(numerPelny))
            {
                var numerKey = BusinessKeyHelper.BuildKey(targetPracownik, numerPelny);
                if (existing.UmowyNumery.Contains(numerKey))
                {
                    var issue = new MigrationIssue
                    {
                        Id = ++_issueIdCounter,
                        Type = IssueType.DuplicateUmowa,
                        Category = "Umowy",
                        SourceId = Convert.ToInt32(dict["ID"]),
                        Name = numerPelny,
                        Description = $"Umowa o tym numerze już istnieje w bazie docelowej",
                        AffectedRecordsCount = 1,
                        AvailableResolutions = new List<ResolutionOption>
                        {
                            new() { Type = ResolutionType.Skip, Label = "Pomiń", Description = "Nie migruj tej umowy" },
                            // new() { Type = ResolutionType.Override, Label = "Nadpisz", Description = "Zastąp istniejącą umowę" }
                        },
                        Resolution = ResolutionType.Skip // Domyślnie pomijaj duplikaty
                    };

                    plan.Issues.Add(issue);
                }
            }
        }
    }

    private async Task AnalyzeListyPlacDuplicatesAsync(
        MigrationPlan plan,
        ExistingRecords existing,
        MappingData mapping,
        IProgress<string>? progress)
    {
        var sourceListy = await _sourceDb.QueryAsync("SELECT ID, NumerPelny, Definicja, OkresFrom, OkresTo FROM ListyPlac");
        var duplicateCount = 0;

        foreach (var lista in sourceListy)
        {
            var dict = (IDictionary<string, object>)lista;
            var numerPelny = dict["NumerPelny"]?.ToString();

            if (!string.IsNullOrEmpty(numerPelny) && existing.ListyPlacNumery.Contains(numerPelny))
            {
                duplicateCount++;
            }
        }

        // Zamiast tworzyć issue dla każdej listy, stwórz jedno zbiorcze
        if (duplicateCount > 0)
        {
            var issue = new MigrationIssue
            {
                Id = ++_issueIdCounter,
                Type = IssueType.DuplicateListaPlac,
                Category = "ListyPlac",
                Name = "Duplikaty list płac",
                Description = $"{duplicateCount} list płac już istnieje w bazie docelowej (ten sam NumerPelny)",
                AffectedRecordsCount = duplicateCount,
                AvailableResolutions = new List<ResolutionOption>
                {
                    new() { Type = ResolutionType.Skip, Label = "Pomiń wszystkie", Description = "Nie migruj duplikatów" }
                },
                Resolution = ResolutionType.Skip // Domyślnie pomijaj
            };

            plan.Issues.Add(issue);
        }
    }

    private async Task<List<(int Id, string Name)>> GetTargetDefinitionsAsync(string category)
    {
        var result = new List<(int Id, string Name)>();

        try
        {
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
        }
        catch
        {
            // Ignoruj błędy
        }

        return result;
    }

    private async Task<int> CountAffectedRecordsAsync(string category, int sourceId)
    {
        try
        {
            var sql = category switch
            {
                "DefElementow" => $"SELECT COUNT(*) FROM WypElementy WHERE Definicja = {sourceId}",
                "DefNieobecnosci" => $"SELECT COUNT(*) FROM Nieobecnosci WHERE Definicja = {sourceId}",
                "DefListPlac" => $"SELECT COUNT(*) FROM ListyPlac WHERE Definicja = {sourceId}",
                "DefDokumentow" => $"SELECT COUNT(*) FROM Umowy WHERE Definicja = {sourceId}",
                "UrzedySkarbowe" => $"SELECT COUNT(*) FROM PracHistorie WHERE PodatkiUrzadSkarbowy = {sourceId}",
                "Wydzialy" => $"SELECT COUNT(*) FROM Pracownicy WHERE Wydzial = {sourceId}",
                "Kalendarze" => $"SELECT COUNT(*) FROM Pracownicy WHERE Kalendarz = {sourceId}",
                _ => null
            };

            if (sql != null)
            {
                var result = await _sourceDb.QueryAsync(sql);
                var first = result.FirstOrDefault();
                if (first != null)
                {
                    var dict = (IDictionary<string, object>)first;
                    return Convert.ToInt32(dict.Values.First());
                }
            }
        }
        catch
        {
            // Ignoruj błędy
        }

        return 0;
    }

    private async Task<bool> HasPracownikDataAsync(int pracownikId)
    {
        try
        {
            var count = await _sourceDb.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM Umowy WHERE Pracownik = {pracownikId}");
            if (count > 0) return true;

            count = await _sourceDb.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM Wyplaty WHERE Pracownik = {pracownikId}");
            if (count > 0) return true;

            count = await _sourceDb.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM Nieobecnosci WHERE Zrodlo = {pracownikId} AND ZrodloType LIKE 'Pracowni%'");
            if (count > 0) return true;
        }
        catch
        {
            // Ignoruj błędy
        }

        return false;
    }

    private List<ResolutionOption> BuildResolutionOptions(IssueType issueType, List<(int Id, string Name)> targetDefinitions)
    {
        var options = new List<ResolutionOption>();

        // 1. UTWÓRZ - zawsze pierwsza opcja (skopiuj definicję ze source do target)
        options.Add(new ResolutionOption
        {
            Type = ResolutionType.Create,
            Label = ">>> UTWÓRZ w target <<<",
            Description = "Skopiuj tę definicję z bazy źródłowej do docelowej"
        });

        // 2. MAPUJ - na istniejące definicje w target
        if (targetDefinitions.Count > 0)
        {
            options.Add(new ResolutionOption
            {
                Type = ResolutionType.MapTo,
                Label = "Mapuj na istniejącą...",
                Description = $"Wybierz z {targetDefinitions.Count} definicji w bazie docelowej",
                TargetId = null
            });
        }

        // 3. SetNull - dla opcjonalnych FK
        if (issueType == IssueType.MissingUrzadSkarbowy ||
            issueType == IssueType.MissingWydzial ||
            issueType == IssueType.MissingKalendarz)
        {
            options.Add(new ResolutionOption
            {
                Type = ResolutionType.SetNull,
                Label = "Ustaw NULL",
                Description = "Migruj rekordy z pustą wartością tego pola"
            });
        }

        // 4. POMIŃ - zawsze na końcu
        options.Add(new ResolutionOption
        {
            Type = ResolutionType.Skip,
            Label = "Pomiń rekordy",
            Description = "Nie migruj rekordów używających tej definicji"
        });

        return options;
    }

    private async Task CalculateStatsAsync(
        MigrationPlan plan,
        ExistingRecords existing,
        MappingData mapping,
        List<DefinitionComparison> defElementow,
        List<DefinitionComparison> defNieobecnosci,
        List<DefinitionComparison> defListPlac,
        List<DefinitionComparison> defDokumentow,
        List<DefinitionComparison> urzedySkarbowe,
        List<DefinitionComparison> wydzialy,
        List<DefinitionComparison> kalendarze,
        List<PracownikComparison> pracownicy)
    {
        var stats = plan.Stats;

        // Statystyki mapowań definicji
        stats.DefElementowMatched = defElementow.Count(x => x.IsMatched);
        stats.DefElementowTotal = defElementow.Count;
        stats.DefNieobecnosciMatched = defNieobecnosci.Count(x => x.IsMatched);
        stats.DefNieobecnosciTotal = defNieobecnosci.Count;
        stats.DefListPlacMatched = defListPlac.Count(x => x.IsMatched);
        stats.DefListPlacTotal = defListPlac.Count;
        stats.DefDokumentowMatched = defDokumentow.Count(x => x.IsMatched);
        stats.DefDokumentowTotal = defDokumentow.Count;
        stats.UrzedySkarboweMatched = urzedySkarbowe.Count(x => x.IsMatched);
        stats.UrzedySkarboweTotal = urzedySkarbowe.Count;
        stats.WydzialyMatched = wydzialy.Count(x => x.IsMatched);
        stats.WydzialyTotal = wydzialy.Count;
        stats.KalendarzeMatched = kalendarze.Count(x => x.IsMatched);
        stats.KalendarzeTotal = kalendarze.Count;
        stats.PracownicyMatched = pracownicy.Count(x => x.IsMatched);
        stats.PracownicyTotal = pracownicy.Count;

        // Liczby rekordów w source
        var counts = await _sourceDb.GetTableCountsAsync();
        stats.SourcePracownicy = counts.GetValueOrDefault("Pracownicy", 0);
        stats.SourceUmowy = counts.GetValueOrDefault("Umowy", 0);
        stats.SourceListyPlac = counts.GetValueOrDefault("ListyPlac", 0);
        stats.SourceWyplaty = counts.GetValueOrDefault("Wyplaty", 0);
        stats.SourceWypElementy = counts.GetValueOrDefault("WypElementy", 0);
        stats.SourceNieobecnosci = counts.GetValueOrDefault("Nieobecnosci", 0);
        stats.SourceRodzina = counts.GetValueOrDefault("Rodzina", 0);
        stats.SourceDodatki = counts.GetValueOrDefault("Dodatki", 0);
        stats.SourceAdresy = counts.GetValueOrDefault("Adresy", 0);
        stats.SourceRachunki = counts.GetValueOrDefault("RachBankPodmiot", 0);
        stats.SourcePracHistorie = counts.GetValueOrDefault("PracHistorie", 0);
        stats.SourceKalendarze = counts.GetValueOrDefault("Kalendarze", 0);
        stats.SourceHistZatrudnien = counts.GetValueOrDefault("HistZatrudnien", 0);

        // Uproszczone liczby do migracji (dokładne obliczenie wymagałoby więcej logiki)
        stats.ToMigratePracownicy = stats.SourcePracownicy - existing.PracownicyPesel.Count;
        stats.ToMigrateListyPlac = stats.SourceListyPlac - existing.ListyPlacNumery.Count;
        stats.ToMigrateWyplaty = stats.SourceWyplaty - existing.WyplatyKeys.Count;
        stats.ToMigrateWypElementy = stats.SourceWypElementy - existing.WypElementyKeys.Count;
        stats.ToMigrateNieobecnosci = stats.SourceNieobecnosci - existing.NieobecnosciKeys.Count;
        stats.ToMigrateUmowy = stats.SourceUmowy;
        stats.ToMigrateRodzina = stats.SourceRodzina;
        stats.ToMigrateDodatki = stats.SourceDodatki - existing.DodatkiKeys.Count;
        stats.ToMigrateAdresy = stats.SourceAdresy - existing.AdresyKeys.Count;
        stats.ToMigrateRachunki = stats.SourceRachunki - existing.RachunkiKeys.Count;
        stats.ToMigratePracHistorie = stats.SourcePracHistorie - existing.PracHistorieKeys.Count;
        stats.ToMigrateKalendarze = stats.SourceKalendarze - existing.KalendarzeKeys.Count;
        stats.ToMigrateHistZatrudnien = stats.SourceHistZatrudnien - existing.HistZatrudnienKeys.Count;
    }

    /// <summary>
    /// Buduje mapowanie na podstawie porównań i podjętych decyzji
    /// </summary>
    public async Task<MappingData> BuildMappingFromPlanAsync(MigrationPlan plan)
    {
        // Najpierw pobierz standardowe porównania
        var defElementow = await _comparisonService.CompareDefElementowAsync();
        var defNieobecnosci = await _comparisonService.CompareDefNieobecnosciAsync();
        var defListPlac = await _comparisonService.CompareDefListPlacAsync();
        var defDokumentow = await _comparisonService.CompareDefDokumentowAsync();
        var urzedySkarbowe = await _comparisonService.CompareUrzedySkarboweAsync();
        var wydzialy = await _comparisonService.CompareWydzialyAsync();
        var kalendarze = await _comparisonService.CompareKalendarzeAsync();
        var pracownicy = await _comparisonService.ComparePracownicyAsync();

        // Zbuduj bazowe mapowanie
        var mapping = _comparisonService.BuildMappingFromComparisons(
            defElementow, defNieobecnosci, defListPlac, wydzialy, pracownicy,
            defDokumentow, kalendarze, urzedySkarbowe);

        // Przetwarzaj wszystkie decyzje z planu
        foreach (var issue in plan.Issues.Where(i => i.Resolution != ResolutionType.None && i.SourceId.HasValue))
        {
            var sourceId = issue.SourceId!.Value;

            switch (issue.Resolution)
            {
                case ResolutionType.MapTo when issue.ResolutionTargetId.HasValue:
                    // Dodaj mapowanie
                    var targetId = issue.ResolutionTargetId!.Value;
                    switch (issue.Type)
                    {
                        case IssueType.MissingDefElementow:
                            mapping.DefElementow[sourceId] = targetId;
                            break;
                        case IssueType.MissingDefNieobecnosci:
                            mapping.DefNieobecnosci[sourceId] = targetId;
                            break;
                        case IssueType.MissingDefListPlac:
                            mapping.DefListPlac[sourceId] = targetId;
                            break;
                        case IssueType.MissingDefDokumentow:
                            mapping.DefDokumentow[sourceId] = targetId;
                            break;
                        case IssueType.MissingUrzadSkarbowy:
                            mapping.UrzedySkarbowe[sourceId] = targetId;
                            break;
                        case IssueType.MissingWydzial:
                            mapping.Wydzialy[sourceId] = targetId;
                            break;
                        case IssueType.MissingKalendarz:
                            mapping.Kalendarze[sourceId] = targetId;
                            break;
                    }
                    break;

                case ResolutionType.Skip:
                    // Dodaj do listy pomijanych
                    switch (issue.Type)
                    {
                        case IssueType.MissingDefElementow:
                            mapping.SkipDefElementow.Add(sourceId);
                            break;
                        case IssueType.MissingDefNieobecnosci:
                            mapping.SkipDefNieobecnosci.Add(sourceId);
                            break;
                        case IssueType.MissingDefListPlac:
                            mapping.SkipDefListPlac.Add(sourceId);
                            break;
                        case IssueType.MissingDefDokumentow:
                            mapping.SkipDefDokumentow.Add(sourceId);
                            break;
                        case IssueType.MissingUrzadSkarbowy:
                            mapping.SkipUrzedySkarbowe.Add(sourceId);
                            break;
                        case IssueType.MissingWydzial:
                            mapping.SkipWydzialy.Add(sourceId);
                            break;
                        case IssueType.MissingKalendarz:
                            mapping.SkipKalendarzeWzorcowe.Add(sourceId);
                            break;
                        case IssueType.NewPracownik:
                        case IssueType.MissingPracownik:
                            mapping.SkipPracownicy.Add(sourceId);
                            break;
                        case IssueType.DuplicateUmowa:
                            mapping.SkipUmowy.Add(sourceId);
                            break;
                        case IssueType.DuplicateListaPlac:
                            mapping.SkipListyPlac.Add(sourceId);
                            break;
                        case IssueType.DuplicateWyplata:
                            mapping.SkipWyplaty.Add(sourceId);
                            break;
                        case IssueType.DuplicateNieobecnosc:
                            mapping.SkipNieobecnosci.Add(sourceId);
                            break;
                        case IssueType.DuplicateRodzina:
                            mapping.SkipRodzina.Add(sourceId);
                            break;
                        case IssueType.DuplicateDodatek:
                            mapping.SkipDodatki.Add(sourceId);
                            break;
                        case IssueType.DuplicateAdres:
                            mapping.SkipAdresy.Add(sourceId);
                            break;
                        case IssueType.DuplicateRachunek:
                            mapping.SkipRachunki.Add(sourceId);
                            break;
                        case IssueType.DuplicatePracHistoria:
                            mapping.SkipPracHistorie.Add(sourceId);
                            break;
                        case IssueType.DuplicateKalendarz:
                            mapping.SkipKalendarze.Add(sourceId);
                            break;
                        case IssueType.DuplicateHistZatrudnienia:
                            mapping.SkipHistZatrudnien.Add(sourceId);
                            break;
                    }
                    break;

                case ResolutionType.SetNull:
                    // Dodaj do listy definicji dla których FK ma być NULL
                    switch (issue.Type)
                    {
                        case IssueType.MissingDefElementow:
                            mapping.SetNullDefElementow.Add(sourceId);
                            break;
                        case IssueType.MissingDefNieobecnosci:
                            mapping.SetNullDefNieobecnosci.Add(sourceId);
                            break;
                        case IssueType.MissingDefListPlac:
                            mapping.SetNullDefListPlac.Add(sourceId);
                            break;
                        case IssueType.MissingDefDokumentow:
                            mapping.SetNullDefDokumentow.Add(sourceId);
                            break;
                        case IssueType.MissingUrzadSkarbowy:
                            mapping.SetNullUrzedySkarbowe.Add(sourceId);
                            break;
                        case IssueType.MissingWydzial:
                            mapping.SetNullWydzialy.Add(sourceId);
                            break;
                        case IssueType.MissingKalendarz:
                            mapping.SetNullKalendarze.Add(sourceId);
                            break;
                    }
                    break;

                case ResolutionType.Migrate:
                    // Dla nowych pracowników - oznacz do migracji (nie do skip)
                    // Nic specjalnego nie robimy - pracownik zostanie utworzony
                    break;

                case ResolutionType.Create:
                    // Dodaj do listy definicji do utworzenia
                    switch (issue.Type)
                    {
                        case IssueType.MissingDefElementow:
                            mapping.CreateDefElementow.Add(sourceId);
                            break;
                        case IssueType.MissingDefNieobecnosci:
                            mapping.CreateDefNieobecnosci.Add(sourceId);
                            break;
                        case IssueType.MissingDefListPlac:
                            mapping.CreateDefListPlac.Add(sourceId);
                            break;
                        case IssueType.MissingDefDokumentow:
                            mapping.CreateDefDokumentow.Add(sourceId);
                            break;
                        case IssueType.MissingUrzadSkarbowy:
                            mapping.CreateUrzedySkarbowe.Add(sourceId);
                            break;
                        case IssueType.MissingWydzial:
                            mapping.CreateWydzialy.Add(sourceId);
                            break;
                        case IssueType.MissingKalendarz:
                            mapping.CreateKalendarze.Add(sourceId);
                            break;
                    }
                    break;
            }
        }

        return mapping;
    }
}
