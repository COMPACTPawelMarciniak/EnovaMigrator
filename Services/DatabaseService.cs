using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;
using EnovaMigrator.Models;
using EnovaMigrator.Configuration;

namespace EnovaMigrator.Services;

public class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private SqlConnection? _connection;

    public DatabaseService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public SqlConnection GetConnection()
    {
        if (_connection == null || _connection.State != ConnectionState.Open)
        {
            _connection = new SqlConnection(_connectionString);
            _connection.Open();
        }
        return _connection;
    }

    public bool TestConnection()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GetDatabaseName()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn.Database;
        }
        catch
        {
            return "Blad polaczenia";
        }
    }

    // ============ PRACOWNICY ============
    public async Task<IEnumerable<Pracownik>> GetPracownicyAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync<Pracownik>(
            "SELECT ID, Guid, Kod, Nazwisko, Imie, PESEL, Wydzial, Typ FROM Pracownicy ORDER BY Nazwisko, Imie");
    }

    public async Task<IEnumerable<Pracownik>> GetPracownicyWithPeselAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync<Pracownik>(
            "SELECT ID, Guid, Kod, Nazwisko, Imie, PESEL, Wydzial, Typ FROM Pracownicy WHERE PESEL IS NOT NULL AND PESEL <> '' ORDER BY Nazwisko, Imie");
    }

    // ============ DEFINICJE / SLOWNIKI ============
    public async Task<IEnumerable<DefElementow>> GetDefElementowAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync<DefElementow>(
            "SELECT ID, Guid, Nazwa, Skrot, Kod, Kolejnosc FROM DefElementow ORDER BY Kolejnosc, Nazwa");
    }

    public async Task<IEnumerable<DefNieobecnosci>> GetDefNieobecnosciAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync<DefNieobecnosci>(
            "SELECT ID, Guid, Nazwa, Skrot, Kod FROM DefNieobecnosci ORDER BY Nazwa");
    }

    public async Task<IEnumerable<DefListPlac>> GetDefListPlacAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync<DefListPlac>(
            "SELECT ID, Guid, Nazwa, Symbol FROM DefListPlac ORDER BY Nazwa");
    }

    public async Task<IEnumerable<Wydzial>> GetWydzialyAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync<Wydzial>(
            "SELECT ID, Guid, Nazwa, Symbol FROM Wydzialy ORDER BY Nazwa");
    }

    // ============ LISTY PLAC ============
    public async Task<IEnumerable<ListaPlac>> GetListyPlacAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync<ListaPlac>(
            "SELECT ID, Guid, Definicja, NumerPelny, Data, OkresFrom, OkresTo, Wydzial, Seria FROM ListyPlac ORDER BY Data DESC");
    }

    public async Task<int> GetListyPlacCountAsync()
    {
        var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ListyPlac");
    }

    // ============ WYPLATY ============
    public async Task<IEnumerable<dynamic>> GetAllWyplatyAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync("SELECT * FROM Wyplaty ORDER BY ID");
    }

    public async Task<IEnumerable<dynamic>> GetWyplatyByListaPlacAsync(int listaPlacId)
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM Wyplaty WHERE ListaPlac = @listaPlacId",
            new { listaPlacId });
    }

    public async Task<int> GetWyplatyCountAsync()
    {
        var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Wyplaty");
    }

    // ============ ELEMENTY WYPLAT ============
    public async Task<IEnumerable<dynamic>> GetAllWypElementyAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync("SELECT * FROM WypElementy ORDER BY ID");
    }

    public async Task<IEnumerable<dynamic>> GetWypElementyByWyplataAsync(int wyplataId)
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM WypElementy WHERE Wyplata = @wyplataId",
            new { wyplataId });
    }

    public async Task<int> GetWypElementyCountAsync()
    {
        var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM WypElementy");
    }

    // ============ NIEOBECNOSCI ============
    public async Task<IEnumerable<dynamic>> GetAllNieobecnosciAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync("SELECT * FROM Nieobecnosci ORDER BY OkresFrom");
    }

    public async Task<int> GetNieobecnosciCountAsync()
    {
        var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Nieobecnosci");
    }

    // ============ UMOWY ============
    public async Task<IEnumerable<dynamic>> GetAllUmowyAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync("SELECT * FROM Umowy ORDER BY ID");
    }

    public async Task<int> GetUmowyCountAsync()
    {
        var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Umowy");
    }

    // ============ POZOSTALE DANE KADROWE ============
    public async Task<IEnumerable<dynamic>> GetAllRodzinaAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync("SELECT * FROM Rodzina ORDER BY ID");
    }

    public async Task<IEnumerable<dynamic>> GetRodzinaByPracownikAsync(int pracownikId)
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM Rodzina WHERE Pracownik = @pracownikId",
            new { pracownikId });
    }

    public async Task<int> GetRodzinaCountAsync()
    {
        var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Rodzina");
    }

    public async Task<IEnumerable<dynamic>> GetAllAdresyAsync(string hostType = "EnovaKadryPlace.Pracownik")
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM Adresy WHERE HostType = @hostType ORDER BY ID",
            new { hostType });
    }

    public async Task<IEnumerable<dynamic>> GetAdresyByHostAsync(int hostId, string hostType)
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM Adresy WHERE Host = @hostId AND HostType = @hostType",
            new { hostId, hostType });
    }

    public async Task<IEnumerable<dynamic>> GetAllRachunkiAsync(string podmiotType = "EnovaKadryPlace.Pracownik")
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM RachBankPodmiot WHERE PodmiotType = @podmiotType ORDER BY ID",
            new { podmiotType });
    }

    public async Task<IEnumerable<dynamic>> GetRachunkiByPodmiotAsync(int podmiotId, string podmiotType)
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM RachBankPodmiot WHERE Podmiot = @podmiotId AND PodmiotType = @podmiotType",
            new { podmiotId, podmiotType });
    }

    public async Task<IEnumerable<dynamic>> GetAllPracHistorieAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync("SELECT * FROM PracHistorie ORDER BY Pracownik, AktualnoscFrom");
    }

    public async Task<IEnumerable<dynamic>> GetPracHistorieByPracownikAsync(int pracownikId)
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM PracHistorie WHERE Pracownik = @pracownikId ORDER BY AktualnoscFrom",
            new { pracownikId });
    }

    public async Task<IEnumerable<dynamic>> GetAllDodatkiAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync("SELECT * FROM Dodatki ORDER BY ID");
    }

    public async Task<IEnumerable<dynamic>> GetDodatkiByPracownikAsync(int pracownikId)
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM Dodatki WHERE Pracownik = @pracownikId",
            new { pracownikId });
    }

    public async Task<IEnumerable<dynamic>> GetAllKalendarzePracownikowAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync("SELECT * FROM Kalendarze WHERE Pracownik IS NOT NULL ORDER BY ID");
    }

    public async Task<IEnumerable<dynamic>> GetKalendarzByPracownikAsync(int pracownikId)
    {
        var conn = GetConnection();
        return await conn.QueryAsync(
            "SELECT * FROM Kalendarze WHERE Pracownik = @pracownikId",
            new { pracownikId });
    }

    // ============ POBIERANIE ISTNIEJACYCH REKORDOW (po kluczach biznesowych) ============
    public async Task<ExistingRecords> GetExistingRecordsAsync()
    {
        var conn = GetConnection();
        var existing = new ExistingRecords();

        // Helpery do spójnego formatowania NULL
        static string FmtDate(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "<NULL>";
        static string FmtInt(int? i) => i.HasValue ? i.Value.ToString() : "<NULL>";
        static string FmtStr(string? s) => string.IsNullOrEmpty(s) ? "<EMPTY>" : s;

        // Pracownicy - po PESEL lub Imie|Nazwisko
        try
        {
            var pracData = await conn.QueryAsync<(int ID, string? PESEL, string Imie, string Nazwisko)>(
                "SELECT ID, PESEL, Imie, Nazwisko FROM Pracownicy");
            foreach (var p in pracData)
            {
                // Klucz po PESEL (jeśli dostępny)
                if (!string.IsNullOrEmpty(p.PESEL))
                {
                    existing.PracownicyPesel.Add(p.PESEL);
                    existing.PracownicyPeselToId[p.PESEL] = p.ID;
                }
                // Klucz po Imie|Nazwisko
                var nameKey = $"{FmtStr(p.Imie)}|{FmtStr(p.Nazwisko)}";
                existing.PracownicyKeys.Add(nameKey);
                existing.PracownicyKeysToId[nameKey] = p.ID;
            }
        }
        catch { /* tabela może nie istnieć */ }

        // ListyPlac - po NumerPelny lub Definicja|OkresFrom|OkresTo|Wydzial
        try
        {
            var lpData = await conn.QueryAsync<(string? NumerPelny, int Definicja, DateTime? OkresFrom, DateTime? OkresTo, int? Wydzial)>(
                "SELECT NumerPelny, Definicja, OkresFrom, OkresTo, Wydzial FROM ListyPlac");
            foreach (var lp in lpData)
            {
                if (!string.IsNullOrEmpty(lp.NumerPelny))
                    existing.ListyPlacNumery.Add(lp.NumerPelny);
                existing.ListyPlacKeys.Add($"{lp.Definicja}|{FmtDate(lp.OkresFrom)}|{FmtDate(lp.OkresTo)}|{FmtInt(lp.Wydzial)}");
            }
        }
        catch { /* tabela może nie istnieć */ }

        // Wyplaty - po ListaPlac|Pracownik
        try
        {
            var wypData = await conn.QueryAsync<(int ListaPlac, int Pracownik)>(
                "SELECT ListaPlac, Pracownik FROM Wyplaty");
            foreach (var w in wypData)
                existing.WyplatyKeys.Add($"{w.ListaPlac}|{w.Pracownik}");
        }
        catch { /* tabela może nie istnieć */ }

        // WypElementy - po Wyplata|Definicja|OkresFrom|OkresTo
        try
        {
            var weData = await conn.QueryAsync<(int Wyplata, int Definicja, DateTime? OkresFrom, DateTime? OkresTo)>(
                "SELECT Wyplata, Definicja, OkresFrom, OkresTo FROM WypElementy");
            foreach (var we in weData)
                existing.WypElementyKeys.Add($"{we.Wyplata}|{we.Definicja}|{FmtDate(we.OkresFrom)}|{FmtDate(we.OkresTo)}");
        }
        catch { /* tabela może nie istnieć */ }

        // Nieobecnosci - po Zrodlo|Definicja|OkresFrom|OkresTo (Zrodlo to pracownik dla ZrodloType zawierającego 'Pracownik')
        try
        {
            var niData = await conn.QueryAsync<(int Zrodlo, int Definicja, DateTime? OkresFrom, DateTime? OkresTo)>(
                "SELECT Zrodlo, Definicja, OkresFrom, OkresTo FROM Nieobecnosci WHERE ZrodloType LIKE 'Pracowni%'");
            foreach (var n in niData)
                existing.NieobecnosciKeys.Add($"{n.Zrodlo}|{n.Definicja}|{FmtDate(n.OkresFrom)}|{FmtDate(n.OkresTo)}");
        }
        catch { /* tabela może nie istnieć */ }

        // Umowy - po Pracownik|NumerPelny lub Pracownik|Data
        try
        {
            var umData = await conn.QueryAsync<(int Pracownik, string? NumerPelny, DateTime? Data)>(
                "SELECT Pracownik, NumerPelny, Data FROM Umowy");
            foreach (var u in umData)
            {
                if (!string.IsNullOrEmpty(u.NumerPelny))
                    existing.UmowyNumery.Add($"{u.Pracownik}|{u.NumerPelny}");
                existing.UmowyKeys.Add($"{u.Pracownik}|{FmtDate(u.Data)}");
            }
        }
        catch { /* tabela może nie istnieć */ }

        // Rodzina - po Pracownik|PESEL lub Pracownik|Imie|Nazwisko|DataUrodzenia
        try
        {
            var rodData = await conn.QueryAsync<(int Pracownik, string? PESEL, string? Imie, string? Nazwisko, DateTime? DataUrodzenia)>(
                "SELECT Pracownik, PESEL, Imie, Nazwisko, DataUrodzenia FROM Rodzina");
            foreach (var r in rodData)
            {
                if (!string.IsNullOrEmpty(r.PESEL))
                    existing.RodzinaPesel.Add($"{r.Pracownik}|{r.PESEL}");
                existing.RodzinaKeys.Add($"{r.Pracownik}|{FmtStr(r.Imie)}|{FmtStr(r.Nazwisko)}|{FmtDate(r.DataUrodzenia)}");
            }
        }
        catch { /* tabela może nie istnieć */ }

        // Dodatki - po Pracownik|Nazwa
        try
        {
            var dodData = await conn.QueryAsync<(int Pracownik, string? Nazwa)>(
                "SELECT Pracownik, Nazwa FROM Dodatki");
            foreach (var d in dodData)
                existing.DodatkiKeys.Add($"{d.Pracownik}|{FmtStr(d.Nazwa)}");
        }
        catch { /* tabela może nie istnieć */ }

        // Adresy - po Host|HostType|Typ
        try
        {
            var adresyData = await conn.QueryAsync<(int Host, string HostType, int? Typ)>(
                "SELECT Host, HostType, Typ FROM Adresy");
            foreach (var a in adresyData)
                existing.AdresyKeys.Add($"{a.Host}|{a.HostType}|{FmtInt(a.Typ)}");
        }
        catch { /* tabela może nie istnieć */ }

        // RachBankPodmiot - po Podmiot|PodmiotType|NumerRachunku
        try
        {
            var rachData = await conn.QueryAsync<(int Podmiot, string PodmiotType, string? Numer)>(
                "SELECT Podmiot, PodmiotType, RachunekNumerNumer FROM RachBankPodmiot");
            foreach (var r in rachData)
                existing.RachunkiKeys.Add($"{r.Podmiot}|{r.PodmiotType}|{FmtStr(r.Numer)}");
        }
        catch { /* tabela może nie istnieć */ }

        // PracHistorie - po Pracownik|AktualnoscFrom
        try
        {
            var phData = await conn.QueryAsync<(int Pracownik, DateTime? AktualnoscFrom)>(
                "SELECT Pracownik, AktualnoscFrom FROM PracHistorie");
            foreach (var ph in phData)
                existing.PracHistorieKeys.Add($"{ph.Pracownik}|{FmtDate(ph.AktualnoscFrom)}");
        }
        catch { /* tabela może nie istnieć */ }

        // Kalendarze - po Pracownik|Nazwa
        try
        {
            var kalData = await conn.QueryAsync<(string? Nazwa, int? Pracownik)>(
                "SELECT Nazwa, Pracownik FROM Kalendarze");
            foreach (var k in kalData)
            {
                if (k.Pracownik.HasValue)
                    existing.KalendarzeKeys.Add($"{k.Pracownik}|{FmtStr(k.Nazwa)}");
            }
        }
        catch { /* tabela może nie istnieć */ }

        // HistZatrudnien - po Pracownik|DataOd
        try
        {
            var hzData = await conn.QueryAsync<(int Pracownik, DateTime? DataOd)>(
                "SELECT Pracownik, DataOd FROM HistZatrudnien");
            foreach (var hz in hzData)
                existing.HistZatrudnienKeys.Add($"{hz.Pracownik}|{FmtDate(hz.DataOd)}");
        }
        catch { /* tabela może nie istnieć */ }

        return existing;
    }

    // ============ DEFINICJE DOKUMENTOW ============
    public async Task<IEnumerable<dynamic>> GetDefDokumentowAsync()
    {
        var conn = GetConnection();
        try
        {
            return await conn.QueryAsync("SELECT ID, Guid, Nazwa, Symbol FROM DefDokumentow ORDER BY Nazwa");
        }
        catch
        {
            return Enumerable.Empty<dynamic>();
        }
    }

    // ============ KALENDARZE (wzorcowe) ============
    public async Task<IEnumerable<dynamic>> GetKalendarzeWzorcoweAsync()
    {
        var conn = GetConnection();
        return await conn.QueryAsync("SELECT ID, Guid, Nazwa FROM Kalendarze WHERE Pracownik IS NULL ORDER BY Nazwa");
    }

    // ============ URZEDY SKARBOWE ============
    public async Task<IEnumerable<dynamic>> GetUrzedySkarboweAsync()
    {
        var conn = GetConnection();
        try
        {
            return await conn.QueryAsync("SELECT ID, Guid, Nazwa, Kod FROM UrzedySkarbowe ORDER BY Kod, Nazwa");
        }
        catch
        {
            return Enumerable.Empty<dynamic>();
        }
    }

    // ============ TRANSAKCJE ============
    public SqlTransaction BeginTransaction()
    {
        var conn = GetConnection();
        return conn.BeginTransaction();
    }

    public async Task<int> ExecuteInTransactionAsync(string sql, object? param, SqlTransaction transaction)
    {
        return await transaction.Connection!.ExecuteAsync(sql, param, transaction);
    }

    // ============ STATYSTYKI ============
    public async Task<Dictionary<string, int>> GetTableCountsAsync()
    {
        var conn = GetConnection();
        var counts = new Dictionary<string, int>();

        var tables = new[] {
            "Pracownicy", "Umowy", "ListyPlac", "Wyplaty", "WypElementy",
            "Nieobecnosci", "Rodzina", "Adresy", "RachBankPodmiot",
            "PracHistorie", "Dodatki", "Kalendarze", "HistZatrudnien"
        };

        foreach (var table in tables)
        {
            try
            {
                var count = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [{table}]");
                counts[table] = count;
            }
            catch
            {
                counts[table] = -1;
            }
        }

        return counts;
    }

    // ============ DYNAMICZNE ZAPYTANIA ============
    public async Task<IEnumerable<dynamic>> QueryAsync(string sql, object? param = null)
    {
        var conn = GetConnection();
        return await conn.QueryAsync(sql, param);
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null)
    {
        var conn = GetConnection();
        return await conn.ExecuteAsync(sql, param);
    }

    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null)
    {
        var conn = GetConnection();
        return await conn.ExecuteScalarAsync<T>(sql, param);
    }

    public async Task<IEnumerable<string>> GetTableColumnsAsync(string tableName)
    {
        var conn = GetConnection();
        // Wyklucz kolumny timestamp (rowversion) - nie można ich wstawiać bezpośrednio
        return await conn.QueryAsync<string>(
            @"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
              WHERE TABLE_NAME = @tableName AND DATA_TYPE <> 'timestamp'
              ORDER BY ORDINAL_POSITION",
            new { tableName });
    }

    public async Task<IEnumerable<string>> GetTableColumnsAsync(string tableName, SqlTransaction transaction)
    {
        // Wyklucz kolumny timestamp (rowversion) - nie można ich wstawiać bezpośrednio
        return await transaction.Connection!.QueryAsync<string>(
            @"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
              WHERE TABLE_NAME = @tableName AND DATA_TYPE <> 'timestamp'
              ORDER BY ORDINAL_POSITION",
            new { tableName }, transaction);
    }

    public async Task<int> GetNextIdAsync(string tableName)
    {
        var conn = GetConnection();
        var maxId = await conn.ExecuteScalarAsync<int?>($"SELECT MAX(ID) FROM [{tableName}]");
        return (maxId ?? 0) + 1;
    }

    public async Task<int> GetNextIdAsync(string tableName, SqlTransaction transaction)
    {
        var maxId = await transaction.Connection!.ExecuteScalarAsync<int?>(
            $"SELECT MAX(ID) FROM [{tableName}]", transaction: transaction);
        return (maxId ?? 0) + 1;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
