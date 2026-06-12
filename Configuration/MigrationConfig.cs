namespace EnovaMigrator.Configuration;

public class MigrationConfig
{
    public string SourceConnectionString { get; set; } = string.Empty;
    public string TargetConnectionString { get; set; } = string.Empty;
    public string MappingFilePath { get; set; } = "mapping.json";
}

public class MappingData
{
    // Mapowania encji: SourceID -> TargetID
    public Dictionary<int, int> Pracownicy { get; set; } = new();
    public Dictionary<int, int> DefElementow { get; set; } = new();
    public Dictionary<int, int> DefNieobecnosci { get; set; } = new();
    public Dictionary<int, int> DefListPlac { get; set; } = new();
    public Dictionary<int, int> DefDokumentow { get; set; } = new();
    public Dictionary<int, int> Wydzialy { get; set; } = new();
    public Dictionary<int, int> Kalendarze { get; set; } = new();
    public Dictionary<int, int> UrzedySkarbowe { get; set; } = new();

    // Mapowania dla migrowanych rekordów
    public Dictionary<int, int> ListyPlac { get; set; } = new();
    public Dictionary<int, int> Wyplaty { get; set; } = new();
    public Dictionary<int, int> Umowy { get; set; } = new();

    // Mapowanie po PESEL dla pracowników
    public Dictionary<string, int> PracownicyByPesel { get; set; } = new();

    // Mapowanie po GUID (uniwersalne)
    public Dictionary<Guid, int> ByGuid { get; set; } = new();

    // Listy pomijanych rekordów (SourceID) - na podstawie decyzji użytkownika
    public HashSet<int> SkipPracownicy { get; set; } = new();
    public HashSet<int> SkipUmowy { get; set; } = new();
    public HashSet<int> SkipListyPlac { get; set; } = new();
    public HashSet<int> SkipWyplaty { get; set; } = new();
    public HashSet<int> SkipWypElementy { get; set; } = new();
    public HashSet<int> SkipNieobecnosci { get; set; } = new();
    public HashSet<int> SkipRodzina { get; set; } = new();
    public HashSet<int> SkipDodatki { get; set; } = new();
    public HashSet<int> SkipAdresy { get; set; } = new();
    public HashSet<int> SkipRachunki { get; set; } = new();
    public HashSet<int> SkipPracHistorie { get; set; } = new();
    public HashSet<int> SkipKalendarze { get; set; } = new();
    public HashSet<int> SkipHistZatrudnien { get; set; } = new();

    // Definicje których rekordy należy pominąć (brak mapowania i decyzja Skip)
    public HashSet<int> SkipDefElementow { get; set; } = new();
    public HashSet<int> SkipDefNieobecnosci { get; set; } = new();
    public HashSet<int> SkipDefListPlac { get; set; } = new();
    public HashSet<int> SkipDefDokumentow { get; set; } = new();
    public HashSet<int> SkipUrzedySkarbowe { get; set; } = new();
    public HashSet<int> SkipWydzialy { get; set; } = new();
    public HashSet<int> SkipKalendarzeWzorcowe { get; set; } = new();

    // Definicje do utworzenia w target (skopiowania ze source)
    public HashSet<int> CreateDefElementow { get; set; } = new();
    public HashSet<int> CreateDefNieobecnosci { get; set; } = new();
    public HashSet<int> CreateDefListPlac { get; set; } = new();
    public HashSet<int> CreateDefDokumentow { get; set; } = new();
    public HashSet<int> CreateUrzedySkarbowe { get; set; } = new();
    public HashSet<int> CreateWydzialy { get; set; } = new();
    public HashSet<int> CreateKalendarze { get; set; } = new();

    // Definicje dla których FK ma być ustawione na NULL (zamiast mapowania)
    public HashSet<int> SetNullDefElementow { get; set; } = new();
    public HashSet<int> SetNullDefNieobecnosci { get; set; } = new();
    public HashSet<int> SetNullDefListPlac { get; set; } = new();
    public HashSet<int> SetNullDefDokumentow { get; set; } = new();
    public HashSet<int> SetNullUrzedySkarbowe { get; set; } = new();
    public HashSet<int> SetNullWydzialy { get; set; } = new();
    public HashSet<int> SetNullKalendarze { get; set; } = new();
}

// Klasa do śledzenia co już istnieje w bazie docelowej (po kluczach biznesowych)
public class ExistingRecords
{
    // Pracownicy: PESEL lub Imie|Nazwisko
    public HashSet<string> PracownicyPesel { get; set; } = new();      // PESEL
    public HashSet<string> PracownicyKeys { get; set; } = new();       // Imie|Nazwisko
    public Dictionary<string, int> PracownicyPeselToId { get; set; } = new();  // PESEL -> ID w target
    public Dictionary<string, int> PracownicyKeysToId { get; set; } = new();   // Imie|Nazwisko -> ID w target

    // ListyPlac: NumerPelny lub Definicja|OkresFrom|OkresTo|Wydzial
    public HashSet<string> ListyPlacNumery { get; set; } = new();
    public HashSet<string> ListyPlacKeys { get; set; } = new();        // Definicja|OkresFrom|OkresTo|Wydzial

    // Wyplaty: ListaPlac|Pracownik (po zmapowanych ID w target)
    public HashSet<string> WyplatyKeys { get; set; } = new();          // ListaPlac|Pracownik

    // WypElementy: Wyplata|Definicja|OkresFrom|OkresTo
    public HashSet<string> WypElementyKeys { get; set; } = new();      // Wyplata|Definicja|OkresFrom|OkresTo

    // Nieobecnosci: Pracownik|Definicja|OkresFrom|OkresTo
    public HashSet<string> NieobecnosciKeys { get; set; } = new();     // Pracownik|Definicja|OkresFrom|OkresTo

    // Umowy: Pracownik|NumerPelny lub Pracownik|Data
    public HashSet<string> UmowyNumery { get; set; } = new();          // Pracownik|NumerPelny
    public HashSet<string> UmowyKeys { get; set; } = new();            // Pracownik|Data

    // Rodzina: Pracownik|PESEL lub Pracownik|Imie|Nazwisko|DataUrodzenia
    public HashSet<string> RodzinaPesel { get; set; } = new();         // Pracownik|PESEL
    public HashSet<string> RodzinaKeys { get; set; } = new();          // Pracownik|Imie|Nazwisko|DataUrodzenia

    // Dodatki: Pracownik|Nazwa (nazwa dodatku jest unikalna dla pracownika)
    public HashSet<string> DodatkiKeys { get; set; } = new();          // Pracownik|Nazwa

    // Klucze złożone dla pozostałych tabel
    public HashSet<string> AdresyKeys { get; set; } = new();           // Host|HostType|Typ
    public HashSet<string> RachunkiKeys { get; set; } = new();         // Podmiot|PodmiotType|Numer
    public HashSet<string> PracHistorieKeys { get; set; } = new();     // Pracownik|AktualnoscFrom
    public HashSet<string> KalendarzeKeys { get; set; } = new();       // Pracownik|Nazwa
    public HashSet<string> HistZatrudnienKeys { get; set; } = new();   // Pracownik|DataOd
}
