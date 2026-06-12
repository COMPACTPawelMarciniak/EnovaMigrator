namespace EnovaMigrator.Models;

public class Pracownik
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public string Kod { get; set; } = string.Empty;
    public string Nazwisko { get; set; } = string.Empty;
    public string Imie { get; set; } = string.Empty;
    public string? PESEL { get; set; }
    public int? Wydzial { get; set; }
    public int Typ { get; set; }
}

public class DefElementow
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public string Nazwa { get; set; } = string.Empty;
    public string? Skrot { get; set; }
    public string? Kod { get; set; }
    public int? Kolejnosc { get; set; }
}

public class DefNieobecnosci
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public string Nazwa { get; set; } = string.Empty;
    public string? Skrot { get; set; }
    public string? Kod { get; set; }
}

public class DefListPlac
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public string Nazwa { get; set; } = string.Empty;
    public string? Symbol { get; set; }
}

public class Wydzial
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public string Nazwa { get; set; } = string.Empty;
    public string? Symbol { get; set; }
}

public class ListaPlac
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public int? Definicja { get; set; }
    public string? NumerPelny { get; set; }
    public DateTime? Data { get; set; }
    public DateTime? OkresFrom { get; set; }
    public DateTime? OkresTo { get; set; }
    public int? Wydzial { get; set; }
    public string? Seria { get; set; }
}

public class Wyplata
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public int? ListaPlac { get; set; }
    public int? Pracownik { get; set; }
}

public class WypElement
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public int? Pracownik { get; set; }
    public int? Wyplata { get; set; }
    public int? Wydzial { get; set; }
    public int? Definicja { get; set; }
    public string? Nazwa { get; set; }
    public DateTime? OkresFrom { get; set; }
    public DateTime? OkresTo { get; set; }
    public decimal? Wartosc { get; set; }
    // ... pozostałe pola będą dodawane dynamicznie
}

public class Nieobecnosc
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public int? Zrodlo { get; set; }          // FK do Pracownika/Umowy (polimorficzne)
    public string? ZrodloType { get; set; }
    public int? Definicja { get; set; }
    public DateTime? OkresFrom { get; set; }
    public DateTime? OkresTo { get; set; }
}

public class PracHistoria
{
    public int ID { get; set; }
    public int Pracownik { get; set; }
    public DateTime? AktualnoscFrom { get; set; }
    public DateTime? AktualnoscTo { get; set; }
    // ... 330+ pozostałych pól
}

public class Rodzina
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public int Pracownik { get; set; }
    public string? Nazwisko { get; set; }
    public string? Imie { get; set; }
    public string? PESEL { get; set; }
    public DateTime? UrodzonyData { get; set; }
    public int? StPokrewienstwa { get; set; }
}

public class RachunekBankowy
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public int Podmiot { get; set; }
    public string? PodmiotType { get; set; }
    public string? RachunekNumerNumer { get; set; }
    public int? RachunekBank { get; set; }
}

public class Adres
{
    public int ID { get; set; }
    public int Host { get; set; }
    public string? HostType { get; set; }
    public int? Typ { get; set; }
    public string? AdresMiejscowosc { get; set; }
    public string? AdresUlica { get; set; }
    public string? AdresNrDomu { get; set; }
}

public class Dodatek
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public int Pracownik { get; set; }
    public string? Nazwa { get; set; }
}

public class Kalendarz
{
    public int ID { get; set; }
    public Guid Guid { get; set; }
    public string? Nazwa { get; set; }
    public int? Pracownik { get; set; }
}

// Klasa do porównania słowników
public class DefinitionComparison
{
    public int SourceID { get; set; }
    public int? TargetID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsMatched { get; set; }
    public string MatchType { get; set; } = string.Empty; // "Exact", "ByName", "ByCode", "NotFound"
}

public class PracownikComparison
{
    public int SourceID { get; set; }
    public int? TargetID { get; set; }
    public string PESEL { get; set; } = string.Empty;
    public string SourceNazwisko { get; set; } = string.Empty;
    public string SourceImie { get; set; } = string.Empty;
    public string? TargetNazwisko { get; set; }
    public string? TargetImie { get; set; }
    public bool IsMatched { get; set; }
    public string MatchType { get; set; } = string.Empty;
}
