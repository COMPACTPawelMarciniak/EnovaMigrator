using EnovaMigrator.Models;
using EnovaMigrator.Configuration;

namespace EnovaMigrator.Services;

public class ComparisonService
{
    private readonly DatabaseService _sourceDb;
    private readonly DatabaseService _targetDb;

    public ComparisonService(DatabaseService sourceDb, DatabaseService targetDb)
    {
        _sourceDb = sourceDb;
        _targetDb = targetDb;
    }

    public async Task<List<DefinitionComparison>> CompareDefElementowAsync()
    {
        var sourceItems = (await _sourceDb.GetDefElementowAsync()).ToList();
        var targetItems = (await _targetDb.GetDefElementowAsync()).ToList();

        var results = new List<DefinitionComparison>();

        foreach (var source in sourceItems)
        {
            var comparison = new DefinitionComparison
            {
                SourceID = source.ID,
                Name = source.Nazwa,
                Code = source.Kod
            };

            // 1. Szukaj po Kod (najlepsze dopasowanie)
            var byCode = targetItems.FirstOrDefault(t =>
                !string.IsNullOrEmpty(source.Kod) &&
                !string.IsNullOrEmpty(t.Kod) &&
                t.Kod.Equals(source.Kod, StringComparison.OrdinalIgnoreCase));

            if (byCode != null)
            {
                comparison.TargetID = byCode.ID;
                comparison.IsMatched = true;
                comparison.MatchType = "ByCode";
            }
            else
            {
                // 2. Szukaj po Nazwie
                var byName = targetItems.FirstOrDefault(t =>
                    t.Nazwa.Equals(source.Nazwa, StringComparison.OrdinalIgnoreCase));

                if (byName != null)
                {
                    comparison.TargetID = byName.ID;
                    comparison.IsMatched = true;
                    comparison.MatchType = "ByName";
                }
                else
                {
                    // 3. Szukaj po Skrócie
                    var bySkrot = targetItems.FirstOrDefault(t =>
                        !string.IsNullOrEmpty(source.Skrot) &&
                        !string.IsNullOrEmpty(t.Skrot) &&
                        t.Skrot.Equals(source.Skrot, StringComparison.OrdinalIgnoreCase));

                    if (bySkrot != null)
                    {
                        comparison.TargetID = bySkrot.ID;
                        comparison.IsMatched = true;
                        comparison.MatchType = "BySkrot";
                    }
                    else
                    {
                        comparison.IsMatched = false;
                        comparison.MatchType = "NotFound";
                    }
                }
            }

            results.Add(comparison);
        }

        return results;
    }

    public async Task<List<DefinitionComparison>> CompareDefNieobecnosciAsync()
    {
        var sourceItems = (await _sourceDb.GetDefNieobecnosciAsync()).ToList();
        var targetItems = (await _targetDb.GetDefNieobecnosciAsync()).ToList();

        var results = new List<DefinitionComparison>();

        foreach (var source in sourceItems)
        {
            var comparison = new DefinitionComparison
            {
                SourceID = source.ID,
                Name = source.Nazwa,
                Code = source.Kod
            };

            var byCode = targetItems.FirstOrDefault(t =>
                !string.IsNullOrEmpty(source.Kod) &&
                !string.IsNullOrEmpty(t.Kod) &&
                t.Kod.Equals(source.Kod, StringComparison.OrdinalIgnoreCase));

            if (byCode != null)
            {
                comparison.TargetID = byCode.ID;
                comparison.IsMatched = true;
                comparison.MatchType = "ByCode";
            }
            else
            {
                var byName = targetItems.FirstOrDefault(t =>
                    t.Nazwa.Equals(source.Nazwa, StringComparison.OrdinalIgnoreCase));

                if (byName != null)
                {
                    comparison.TargetID = byName.ID;
                    comparison.IsMatched = true;
                    comparison.MatchType = "ByName";
                }
                else
                {
                    comparison.IsMatched = false;
                    comparison.MatchType = "NotFound";
                }
            }

            results.Add(comparison);
        }

        return results;
    }

    public async Task<List<DefinitionComparison>> CompareDefListPlacAsync()
    {
        var sourceItems = (await _sourceDb.GetDefListPlacAsync()).ToList();
        var targetItems = (await _targetDb.GetDefListPlacAsync()).ToList();

        var results = new List<DefinitionComparison>();

        foreach (var source in sourceItems)
        {
            var comparison = new DefinitionComparison
            {
                SourceID = source.ID,
                Name = source.Nazwa,
                Code = source.Symbol
            };

            var byName = targetItems.FirstOrDefault(t =>
                t.Nazwa.Equals(source.Nazwa, StringComparison.OrdinalIgnoreCase));

            if (byName != null)
            {
                comparison.TargetID = byName.ID;
                comparison.IsMatched = true;
                comparison.MatchType = "ByName";
            }
            else
            {
                comparison.IsMatched = false;
                comparison.MatchType = "NotFound";
            }

            results.Add(comparison);
        }

        return results;
    }

    public async Task<List<DefinitionComparison>> CompareWydzialyAsync()
    {
        var sourceItems = (await _sourceDb.GetWydzialyAsync()).ToList();
        var targetItems = (await _targetDb.GetWydzialyAsync()).ToList();

        var results = new List<DefinitionComparison>();

        foreach (var source in sourceItems)
        {
            var comparison = new DefinitionComparison
            {
                SourceID = source.ID,
                Name = source.Nazwa,
                Code = source.Symbol
            };

            var bySymbol = targetItems.FirstOrDefault(t =>
                !string.IsNullOrEmpty(source.Symbol) &&
                !string.IsNullOrEmpty(t.Symbol) &&
                t.Symbol.Equals(source.Symbol, StringComparison.OrdinalIgnoreCase));

            if (bySymbol != null)
            {
                comparison.TargetID = bySymbol.ID;
                comparison.IsMatched = true;
                comparison.MatchType = "BySymbol";
            }
            else
            {
                var byName = targetItems.FirstOrDefault(t =>
                    t.Nazwa.Equals(source.Nazwa, StringComparison.OrdinalIgnoreCase));

                if (byName != null)
                {
                    comparison.TargetID = byName.ID;
                    comparison.IsMatched = true;
                    comparison.MatchType = "ByName";
                }
                else
                {
                    comparison.IsMatched = false;
                    comparison.MatchType = "NotFound";
                }
            }

            results.Add(comparison);
        }

        return results;
    }

    public async Task<List<PracownikComparison>> ComparePracownicyAsync()
    {
        var sourceItems = (await _sourceDb.GetPracownicyWithPeselAsync()).ToList();
        var targetItems = (await _targetDb.GetPracownicyAsync()).ToList();

        var results = new List<PracownikComparison>();

        foreach (var source in sourceItems)
        {
            var comparison = new PracownikComparison
            {
                SourceID = source.ID,
                PESEL = source.PESEL ?? "",
                SourceNazwisko = source.Nazwisko,
                SourceImie = source.Imie
            };

            // Szukaj po PESEL
            var byPesel = targetItems.FirstOrDefault(t =>
                !string.IsNullOrEmpty(source.PESEL) &&
                !string.IsNullOrEmpty(t.PESEL) &&
                t.PESEL == source.PESEL);

            if (byPesel != null)
            {
                comparison.TargetID = byPesel.ID;
                comparison.TargetNazwisko = byPesel.Nazwisko;
                comparison.TargetImie = byPesel.Imie;
                comparison.IsMatched = true;
                comparison.MatchType = "ByPESEL";
            }
            else
            {
                // Szukaj po Nazwisko + Imie
                var byName = targetItems.FirstOrDefault(t =>
                    t.Nazwisko.Equals(source.Nazwisko, StringComparison.OrdinalIgnoreCase) &&
                    t.Imie.Equals(source.Imie, StringComparison.OrdinalIgnoreCase));

                if (byName != null)
                {
                    comparison.TargetID = byName.ID;
                    comparison.TargetNazwisko = byName.Nazwisko;
                    comparison.TargetImie = byName.Imie;
                    comparison.IsMatched = true;
                    comparison.MatchType = "ByName";
                }
                else
                {
                    comparison.IsMatched = false;
                    comparison.MatchType = "NotFound";
                }
            }

            results.Add(comparison);
        }

        return results;
    }

    public async Task<List<DefinitionComparison>> CompareDefDokumentowAsync()
    {
        var sourceItems = (await _sourceDb.GetDefDokumentowAsync()).ToList();
        var targetItems = (await _targetDb.GetDefDokumentowAsync()).ToList();

        var results = new List<DefinitionComparison>();

        foreach (var source in sourceItems)
        {
            var dict = (IDictionary<string, object>)source;
            var comparison = new DefinitionComparison
            {
                SourceID = (int)dict["ID"],
                Name = dict["Nazwa"]?.ToString() ?? "",
                Code = dict["Symbol"]?.ToString()
            };

            // Szukaj po Symbolu
            var byCode = targetItems.FirstOrDefault(t =>
            {
                var tDict = (IDictionary<string, object>)t;
                var srcCode = dict["Symbol"]?.ToString();
                var tgtCode = tDict["Symbol"]?.ToString();
                return !string.IsNullOrEmpty(srcCode) && !string.IsNullOrEmpty(tgtCode) &&
                       tgtCode.Equals(srcCode, StringComparison.OrdinalIgnoreCase);
            });

            if (byCode != null)
            {
                var tDict = (IDictionary<string, object>)byCode;
                comparison.TargetID = (int)tDict["ID"];
                comparison.IsMatched = true;
                comparison.MatchType = "ByCode";
            }
            else
            {
                // Szukaj po Nazwie
                var byName = targetItems.FirstOrDefault(t =>
                {
                    var tDict = (IDictionary<string, object>)t;
                    var tgtName = tDict["Nazwa"]?.ToString() ?? "";
                    return tgtName.Equals(comparison.Name, StringComparison.OrdinalIgnoreCase);
                });

                if (byName != null)
                {
                    var tDict = (IDictionary<string, object>)byName;
                    comparison.TargetID = (int)tDict["ID"];
                    comparison.IsMatched = true;
                    comparison.MatchType = "ByName";
                }
                else
                {
                    comparison.IsMatched = false;
                    comparison.MatchType = "NotFound";
                }
            }

            results.Add(comparison);
        }

        return results;
    }

    public async Task<List<DefinitionComparison>> CompareKalendarzeAsync()
    {
        var sourceItems = (await _sourceDb.GetKalendarzeWzorcoweAsync()).ToList();
        var targetItems = (await _targetDb.GetKalendarzeWzorcoweAsync()).ToList();

        var results = new List<DefinitionComparison>();

        foreach (var source in sourceItems)
        {
            var dict = (IDictionary<string, object>)source;
            var comparison = new DefinitionComparison
            {
                SourceID = (int)dict["ID"],
                Name = dict["Nazwa"]?.ToString() ?? "",
                Code = null
            };

            // Szukaj po Nazwie
            var byName = targetItems.FirstOrDefault(t =>
            {
                var tDict = (IDictionary<string, object>)t;
                var tgtName = tDict["Nazwa"]?.ToString() ?? "";
                return tgtName.Equals(comparison.Name, StringComparison.OrdinalIgnoreCase);
            });

            if (byName != null)
            {
                var tDict = (IDictionary<string, object>)byName;
                comparison.TargetID = (int)tDict["ID"];
                comparison.IsMatched = true;
                comparison.MatchType = "ByName";
            }
            else
            {
                comparison.IsMatched = false;
                comparison.MatchType = "NotFound";
            }

            results.Add(comparison);
        }

        return results;
    }

    public async Task<List<DefinitionComparison>> CompareUrzedySkarboweAsync()
    {
        var sourceItems = (await _sourceDb.GetUrzedySkarboweAsync()).ToList();
        var targetItems = (await _targetDb.GetUrzedySkarboweAsync()).ToList();

        var results = new List<DefinitionComparison>();

        foreach (var source in sourceItems)
        {
            var dict = (IDictionary<string, object>)source;
            var comparison = new DefinitionComparison
            {
                SourceID = (int)dict["ID"],
                Name = dict["Nazwa"]?.ToString() ?? "",
                Code = dict["Kod"]?.ToString()
            };

            // Szukaj po Kodzie (najlepsze dopasowanie - kody urzędów są unikalne)
            var byCode = targetItems.FirstOrDefault(t =>
            {
                var tDict = (IDictionary<string, object>)t;
                var srcCode = dict["Kod"]?.ToString();
                var tgtCode = tDict["Kod"]?.ToString();
                return !string.IsNullOrEmpty(srcCode) && !string.IsNullOrEmpty(tgtCode) &&
                       tgtCode.Equals(srcCode, StringComparison.OrdinalIgnoreCase);
            });

            if (byCode != null)
            {
                var tDict = (IDictionary<string, object>)byCode;
                comparison.TargetID = (int)tDict["ID"];
                comparison.IsMatched = true;
                comparison.MatchType = "ByCode";
            }
            else
            {
                // Szukaj po Nazwie
                var byName = targetItems.FirstOrDefault(t =>
                {
                    var tDict = (IDictionary<string, object>)t;
                    var tgtName = tDict["Nazwa"]?.ToString() ?? "";
                    return tgtName.Equals(comparison.Name, StringComparison.OrdinalIgnoreCase);
                });

                if (byName != null)
                {
                    var tDict = (IDictionary<string, object>)byName;
                    comparison.TargetID = (int)tDict["ID"];
                    comparison.IsMatched = true;
                    comparison.MatchType = "ByName";
                }
                else
                {
                    comparison.IsMatched = false;
                    comparison.MatchType = "NotFound";
                }
            }

            results.Add(comparison);
        }

        return results;
    }

    public MappingData BuildMappingFromComparisons(
        List<DefinitionComparison> defElementow,
        List<DefinitionComparison> defNieobecnosci,
        List<DefinitionComparison> defListPlac,
        List<DefinitionComparison> wydzialy,
        List<PracownikComparison> pracownicy,
        List<DefinitionComparison>? defDokumentow = null,
        List<DefinitionComparison>? kalendarze = null,
        List<DefinitionComparison>? urzedySkarbowe = null)
    {
        var mapping = new MappingData();

        foreach (var item in defElementow.Where(x => x.IsMatched && x.TargetID.HasValue))
            mapping.DefElementow[item.SourceID] = item.TargetID!.Value;

        foreach (var item in defNieobecnosci.Where(x => x.IsMatched && x.TargetID.HasValue))
            mapping.DefNieobecnosci[item.SourceID] = item.TargetID!.Value;

        foreach (var item in defListPlac.Where(x => x.IsMatched && x.TargetID.HasValue))
            mapping.DefListPlac[item.SourceID] = item.TargetID!.Value;

        foreach (var item in wydzialy.Where(x => x.IsMatched && x.TargetID.HasValue))
            mapping.Wydzialy[item.SourceID] = item.TargetID!.Value;

        foreach (var item in pracownicy.Where(x => x.IsMatched && x.TargetID.HasValue))
        {
            mapping.Pracownicy[item.SourceID] = item.TargetID!.Value;
            if (!string.IsNullOrEmpty(item.PESEL))
                mapping.PracownicyByPesel[item.PESEL] = item.TargetID!.Value;
        }

        if (defDokumentow != null)
        {
            foreach (var item in defDokumentow.Where(x => x.IsMatched && x.TargetID.HasValue))
                mapping.DefDokumentow[item.SourceID] = item.TargetID!.Value;
        }

        if (kalendarze != null)
        {
            foreach (var item in kalendarze.Where(x => x.IsMatched && x.TargetID.HasValue))
                mapping.Kalendarze[item.SourceID] = item.TargetID!.Value;
        }

        if (urzedySkarbowe != null)
        {
            foreach (var item in urzedySkarbowe.Where(x => x.IsMatched && x.TargetID.HasValue))
                mapping.UrzedySkarbowe[item.SourceID] = item.TargetID!.Value;
        }

        return mapping;
    }
}
