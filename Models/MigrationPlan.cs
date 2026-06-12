namespace EnovaMigrator.Models;

/// <summary>
/// Typ problemu wykrytego podczas analizy
/// </summary>
public enum IssueType
{
    // Brakujące mapowania definicji
    MissingDefElementow,
    MissingDefNieobecnosci,
    MissingDefListPlac,
    MissingDefDokumentow,
    MissingUrzadSkarbowy,
    MissingWydzial,
    MissingKalendarz,

    // Pracownicy
    MissingPracownik,
    NewPracownik,

    // Duplikaty/konflikty
    DuplicateUmowa,
    DuplicateListaPlac,
    DuplicateWyplata,
    DuplicateNieobecnosc,
    DuplicateRodzina,
    DuplicateDodatek,
    DuplicateAdres,
    DuplicateRachunek,
    DuplicatePracHistoria,
    DuplicateKalendarz,
    DuplicateHistZatrudnienia
}

/// <summary>
/// Możliwe rozwiązania problemu
/// </summary>
public enum ResolutionType
{
    None,           // Brak decyzji
    Skip,           // Pomiń rekord/rekordy używające tej definicji
    MapTo,          // Mapuj na inny ID w target
    SetNull,        // Ustaw NULL (dla opcjonalnych FK)
    Migrate,        // Migruj normalnie (dla nowych pracowników)
    Create,         // Utwórz brakującą definicję w target (skopiuj ze source)
    Override        // Nadpisz istniejący rekord (przyszłość)
}

/// <summary>
/// Pojedynczy problem wykryty podczas analizy
/// </summary>
public class MigrationIssue
{
    public int Id { get; set; }
    public IssueType Type { get; set; }

    /// <summary>
    /// Kategoria problemu (np. "DefElementow", "Umowy")
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// ID w bazie źródłowej (jeśli dotyczy)
    /// </summary>
    public int? SourceId { get; set; }

    /// <summary>
    /// Nazwa/opis elementu (np. "Umowa-Zlecenie", "Jan Kowalski")
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Kod/Symbol (np. "UMW", "PESEL")
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Dodatkowy opis problemu
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Ile rekordów jest dotkniętych tym problemem
    /// </summary>
    public int AffectedRecordsCount { get; set; }

    /// <summary>
    /// Dostępne opcje rozwiązania
    /// </summary>
    public List<ResolutionOption> AvailableResolutions { get; set; } = new();

    /// <summary>
    /// Wybrane rozwiązanie
    /// </summary>
    public ResolutionType Resolution { get; set; } = ResolutionType.None;

    /// <summary>
    /// Dodatkowe dane rozwiązania (np. ID do mapowania)
    /// </summary>
    public int? ResolutionTargetId { get; set; }

    /// <summary>
    /// Nazwa wybranego celu mapowania
    /// </summary>
    public string? ResolutionTargetName { get; set; }
}

/// <summary>
/// Opcja rozwiązania problemu
/// </summary>
public class ResolutionOption
{
    public ResolutionType Type { get; set; }
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// Dla MapTo - ID w target
    /// </summary>
    public int? TargetId { get; set; }

    /// <summary>
    /// Dla MapTo - nazwa w target
    /// </summary>
    public string? TargetName { get; set; }
}

/// <summary>
/// Plan migracji zawierający wszystkie wykryte problemy i decyzje
/// </summary>
public class MigrationPlan
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string SourceDatabase { get; set; } = "";
    public string TargetDatabase { get; set; } = "";

    /// <summary>
    /// Wszystkie wykryte problemy
    /// </summary>
    public List<MigrationIssue> Issues { get; set; } = new();

    /// <summary>
    /// Statystyki analizy
    /// </summary>
    public AnalysisStats Stats { get; set; } = new();

    /// <summary>
    /// Czy wszystkie wymagane decyzje zostały podjęte
    /// </summary>
    public bool IsComplete => Issues.All(i => i.Resolution != ResolutionType.None);

    /// <summary>
    /// Liczba problemów wymagających decyzji
    /// </summary>
    public int PendingDecisionsCount => Issues.Count(i => i.Resolution == ResolutionType.None);

    /// <summary>
    /// Problemy pogrupowane według typu
    /// </summary>
    public IEnumerable<IGrouping<IssueType, MigrationIssue>> IssuesByType =>
        Issues.GroupBy(i => i.Type);

    /// <summary>
    /// Problemy pogrupowane według kategorii
    /// </summary>
    public IEnumerable<IGrouping<string, MigrationIssue>> IssuesByCategory =>
        Issues.GroupBy(i => i.Category);
}

/// <summary>
/// Statystyki z analizy
/// </summary>
public class AnalysisStats
{
    // Liczby rekordów w source
    public int SourcePracownicy { get; set; }
    public int SourceUmowy { get; set; }
    public int SourceListyPlac { get; set; }
    public int SourceWyplaty { get; set; }
    public int SourceWypElementy { get; set; }
    public int SourceNieobecnosci { get; set; }
    public int SourceRodzina { get; set; }
    public int SourceDodatki { get; set; }
    public int SourceAdresy { get; set; }
    public int SourceRachunki { get; set; }
    public int SourcePracHistorie { get; set; }
    public int SourceKalendarze { get; set; }
    public int SourceHistZatrudnien { get; set; }

    // Liczby do migracji (po odfiltrowaniu duplikatów)
    public int ToMigratePracownicy { get; set; }
    public int ToMigrateUmowy { get; set; }
    public int ToMigrateListyPlac { get; set; }
    public int ToMigrateWyplaty { get; set; }
    public int ToMigrateWypElementy { get; set; }
    public int ToMigrateNieobecnosci { get; set; }
    public int ToMigrateRodzina { get; set; }
    public int ToMigrateDodatki { get; set; }
    public int ToMigrateAdresy { get; set; }
    public int ToMigrateRachunki { get; set; }
    public int ToMigratePracHistorie { get; set; }
    public int ToMigrateKalendarze { get; set; }
    public int ToMigrateHistZatrudnien { get; set; }

    // Podsumowanie mapowań definicji
    public int DefElementowMatched { get; set; }
    public int DefElementowTotal { get; set; }
    public int DefNieobecnosciMatched { get; set; }
    public int DefNieobecnosciTotal { get; set; }
    public int DefListPlacMatched { get; set; }
    public int DefListPlacTotal { get; set; }
    public int DefDokumentowMatched { get; set; }
    public int DefDokumentowTotal { get; set; }
    public int UrzedySkarboweMatched { get; set; }
    public int UrzedySkarboweTotal { get; set; }
    public int WydzialyMatched { get; set; }
    public int WydzialyTotal { get; set; }
    public int KalendarzeMatched { get; set; }
    public int KalendarzeTotal { get; set; }
    public int PracownicyMatched { get; set; }
    public int PracownicyTotal { get; set; }

    public int TotalToMigrate => ToMigratePracownicy + ToMigrateUmowy + ToMigrateListyPlac +
        ToMigrateWyplaty + ToMigrateWypElementy + ToMigrateNieobecnosci + ToMigrateRodzina +
        ToMigrateDodatki + ToMigrateAdresy + ToMigrateRachunki + ToMigratePracHistorie +
        ToMigrateKalendarze + ToMigrateHistZatrudnien;
}

/// <summary>
/// Wynik zastosowania decyzji - co faktycznie zrobić z rekordem
/// </summary>
public class RecordAction
{
    public bool ShouldMigrate { get; set; }
    public string? SkipReason { get; set; }
    public Dictionary<string, object?> FieldOverrides { get; set; } = new();
}
