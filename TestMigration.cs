// Prosty test migracji do uruchomienia z wiersza poleceń
using System.Text.Json;
using EnovaMigrator.Services;
using EnovaMigrator.Configuration;

namespace EnovaMigrator;

public static class TestMigration
{
    public static async Task RunAsync(string[] args)
    {
        Console.WriteLine("=== TEST MIGRACJI ENOVA ===\n");

        // Wczytaj konfigurację
        var configPath = "appsettings.json";
        var config = new MigrationConfig();

        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("ConnectionStrings", out var cs))
            {
                if (cs.TryGetProperty("SourceDatabase", out var src))
                    config.SourceConnectionString = src.GetString() ?? "";
                if (cs.TryGetProperty("TargetDatabase", out var tgt))
                    config.TargetConnectionString = tgt.GetString() ?? "";
            }
        }

        Console.WriteLine($"Source: {MaskConnectionString(config.SourceConnectionString)}");
        Console.WriteLine($"Target: {MaskConnectionString(config.TargetConnectionString)}");
        Console.WriteLine();

        // Test połączeń
        Console.WriteLine("--- Test połączeń ---");
        var sourceDb = new DatabaseService(config.SourceConnectionString);
        var targetDb = new DatabaseService(config.TargetConnectionString);

        try
        {
            // Prosty test - próba zapytania
            await sourceDb.QueryAsync("SELECT 1");
            Console.WriteLine("Source: OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Source: BŁĄD - {ex.Message}");
            return;
        }

        try
        {
            await targetDb.QueryAsync("SELECT 1");
            Console.WriteLine("Target: OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Target: BŁĄD - {ex.Message}");
            return;
        }

        Console.WriteLine();

        // Statystyki
        Console.WriteLine("--- Statystyki baz danych ---");
        var sourceStats = await sourceDb.GetTableCountsAsync();
        var targetStats = await targetDb.GetTableCountsAsync();

        Console.WriteLine($"{"Tabela",-20} {"Source",10} {"Target",10}");
        Console.WriteLine(new string('-', 42));

        var tables = new[] { "Pracownicy", "ListyPlac", "Wyplaty", "WypElementy", "Nieobecnosci", "Umowy", "Rodzina", "Dodatki" };
        foreach (var table in tables)
        {
            var srcCount = sourceStats.TryGetValue(table, out var s) ? s : 0;
            var tgtCount = targetStats.TryGetValue(table, out var t) ? t : 0;
            Console.WriteLine($"{table,-20} {srcCount,10} {tgtCount,10}");
        }

        Console.WriteLine();

        // Porównanie definicji
        Console.WriteLine("--- Porównanie definicji ---");
        var comparisonService = new ComparisonService(sourceDb, targetDb);

        var defElementow = await comparisonService.CompareDefElementowAsync();
        var defNieobecnosci = await comparisonService.CompareDefNieobecnosciAsync();
        var defListPlac = await comparisonService.CompareDefListPlacAsync();
        var defDokumentow = await comparisonService.CompareDefDokumentowAsync();
        var urzedySkarbowe = await comparisonService.CompareUrzedySkarboweAsync();
        var wydzialy = await comparisonService.CompareWydzialyAsync();
        var pracownicy = await comparisonService.ComparePracownicyAsync();

        Console.WriteLine($"DefElementow: {defElementow.Count(x => x.IsMatched)}/{defElementow.Count} dopasowanych");
        Console.WriteLine($"DefNieobecnosci: {defNieobecnosci.Count(x => x.IsMatched)}/{defNieobecnosci.Count} dopasowanych");
        Console.WriteLine($"DefListPlac: {defListPlac.Count(x => x.IsMatched)}/{defListPlac.Count} dopasowanych");
        Console.WriteLine($"DefDokumentow: {defDokumentow.Count(x => x.IsMatched)}/{defDokumentow.Count} dopasowanych");
        Console.WriteLine($"UrzedySkarbowe: {urzedySkarbowe.Count(x => x.IsMatched)}/{urzedySkarbowe.Count} dopasowanych");
        Console.WriteLine($"Wydzialy: {wydzialy.Count(x => x.IsMatched)}/{wydzialy.Count} dopasowanych");
        Console.WriteLine($"Pracownicy: {pracownicy.Count(x => x.IsMatched)}/{pracownicy.Count} dopasowanych");

        Console.WriteLine();

        // Buduj mapowanie
        Console.WriteLine("--- Budowanie mapowania ---");
        var mapping = comparisonService.BuildMappingFromComparisons(
            defElementow, defNieobecnosci, defListPlac, wydzialy, pracownicy,
            defDokumentow, urzedySkarbowe: urzedySkarbowe);

        Console.WriteLine($"Zmapowano: {mapping.Pracownicy.Count} pracowników");
        Console.WriteLine($"Zmapowano: {mapping.DefElementow.Count} definicji elementów");
        Console.WriteLine($"Zmapowano: {mapping.DefNieobecnosci.Count} definicji nieobecności");
        Console.WriteLine($"Zmapowano: {mapping.DefListPlac.Count} definicji list płac");
        Console.WriteLine($"Zmapowano: {mapping.DefDokumentow.Count} definicji dokumentów");
        Console.WriteLine($"Zmapowano: {mapping.UrzedySkarbowe.Count} urzędów skarbowych");
        Console.WriteLine($"Zmapowano: {mapping.Wydzialy.Count} wydziałów");

        Console.WriteLine();

        // Walidacja mapowań
        Console.WriteLine("--- Walidacja mapowań ---");
        var migrationService = new MigrationService(sourceDb, targetDb, mapping, new MigrationOptions { DryRun = true });
        var validation = await migrationService.ValidateMappingsAsync();

        if (validation.IsValid)
        {
            Console.WriteLine("Wszystkie mapowania są poprawne!");
        }
        else
        {
            Console.WriteLine("Brakujące mapowania:");
            foreach (var msg in validation.GetSummary())
                Console.WriteLine($"  - {msg}");
        }

        Console.WriteLine();

        // Pobranie istniejących rekordów
        Console.WriteLine("--- Istniejące rekordy w target ---");
        var existing = await targetDb.GetExistingRecordsAsync();
        Console.WriteLine($"ListyPlac (numery): {existing.ListyPlacNumery.Count}");
        Console.WriteLine($"ListyPlac (klucze): {existing.ListyPlacKeys.Count}");
        Console.WriteLine($"Wyplaty (klucze): {existing.WyplatyKeys.Count}");
        Console.WriteLine($"WypElementy (klucze): {existing.WypElementyKeys.Count}");
        Console.WriteLine($"Nieobecnosci (klucze): {existing.NieobecnosciKeys.Count}");
        Console.WriteLine($"Umowy (numery): {existing.UmowyNumery.Count}");
        Console.WriteLine($"Umowy (klucze): {existing.UmowyKeys.Count}");

        Console.WriteLine();

        // Migracja (dry-run lub prawdziwa)
        if (args.Contains("--dry-run") || args.Contains("--migrate"))
        {
            var isDryRun = args.Contains("--dry-run");
            Console.WriteLine(isDryRun ? "--- DRY-RUN MIGRACJI ---" : "--- PRAWDZIWA MIGRACJA ---");

            if (!isDryRun)
            {
                Console.WriteLine("UWAGA: To jest prawdziwa migracja! Dane zostaną zapisane w bazie docelowej.");
                Console.WriteLine("Rozpoczynam za 3 sekundy...");
                await Task.Delay(3000);
            }

            var options = new MigrationOptions { DryRun = isDryRun, UseTransaction = !isDryRun };
            var migrationServiceRun = new MigrationService(sourceDb, targetDb, mapping, options);

            var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));
            var result = await migrationServiceRun.MigrateAllAsync(progress);

            Console.WriteLine();
            Console.WriteLine($"Status: {(result.Success ? "SUKCES" : "BŁĄD")}");
            if (!result.Success)
                Console.WriteLine($"Błąd: {result.ErrorMessage}");

            Console.WriteLine();
            Console.WriteLine(isDryRun ? "--- Statystyki DRY-RUN ---" : "--- Statystyki MIGRACJI ---");
            var stats = result.Stats;
            Console.WriteLine($"{"Tabela",-20} {"Zmigrow.",10} {"Pominięte",10} {"Błędy",10}");
            Console.WriteLine(new string('-', 52));
            Console.WriteLine($"{"Pracownicy",-20} {stats.PracownicyMigrated,10} {stats.PracownicySkipped,10} {stats.PracownicyErrors,10}");
            Console.WriteLine($"{"ListyPlac",-20} {stats.ListyPlacMigrated,10} {stats.ListyPlacSkipped,10} {stats.ListyPlacErrors,10}");
            Console.WriteLine($"{"Wyplaty",-20} {stats.WyplatyMigrated,10} {stats.WyplatySkipped,10} {stats.WyplatyErrors,10}");
            Console.WriteLine($"{"WypElementy",-20} {stats.WypElementyMigrated,10} {stats.WypElementySkipped,10} {stats.WypElementyErrors,10}");
            Console.WriteLine($"{"Nieobecnosci",-20} {stats.NieobecnosciMigrated,10} {stats.NieobecnosciSkipped,10} {stats.NieobecnosciErrors,10}");
            Console.WriteLine($"{"Umowy",-20} {stats.UmowyMigrated,10} {stats.UmowySkipped,10} {stats.UmowyErrors,10}");
            Console.WriteLine($"{"Rodzina",-20} {stats.RodzinaMigrated,10} {stats.RodzinaSkipped,10} {stats.RodzinaErrors,10}");
            Console.WriteLine($"{"Dodatki",-20} {stats.DodatkiMigrated,10} {stats.DodatkiSkipped,10} {stats.DodatkiErrors,10}");
            Console.WriteLine($"{"Adresy",-20} {stats.AdresyMigrated,10} {stats.AdresySkipped,10} {stats.AdresyErrors,10}");
            Console.WriteLine($"{"Rachunki",-20} {stats.RachunkiMigrated,10} {stats.RachunkiSkipped,10} {stats.RachunkiErrors,10}");
            Console.WriteLine($"{"PracHistorie",-20} {stats.PracHistorieMigrated,10} {stats.PracHistorieSkipped,10} {stats.PracHistorieErrors,10}");
            Console.WriteLine($"{"Kalendarze",-20} {stats.KalendarzeMigrated,10} {stats.KalendarzeSkipped,10} {stats.KalendarzeErrors,10}");
            Console.WriteLine($"{"HistZatrudnien",-20} {stats.HistZatrudnienMigrated,10} {stats.HistZatrudnienSkipped,10} {stats.HistZatrudnienErrors,10}");
            Console.WriteLine(new string('-', 52));
            Console.WriteLine($"{"RAZEM",-20} {stats.TotalMigrated,10} {"",-10} {stats.TotalErrors,10}");

            if (stats.Errors.Any())
            {
                Console.WriteLine();
                Console.WriteLine("Pierwsze 10 błędów:");
                foreach (var error in stats.Errors.Take(10))
                    Console.WriteLine($"  - {error}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== KONIEC TESTU ===");
    }

    private static string MaskConnectionString(string cs)
    {
        // Ukryj hasło w connection stringu
        var parts = cs.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
                parts[i] = "Password=***";
        }
        return string.Join(";", parts);
    }
}
