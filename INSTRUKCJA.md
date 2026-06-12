# EnovaMigrator - Instrukcja obsługi

Narzędzie do migracji danych kadrowo-płacowych między bazami enova365.

## Spis treści

1. [Wymagania](#wymagania)
2. [Uruchomienie](#uruchomienie)
3. [Konfiguracja połączeń](#konfiguracja-połączeń)
4. [Workflow migracji](#workflow-migracji)
5. [Menu główne](#menu-główne)
6. [Rozwiązywanie problemów](#rozwiązywanie-problemów)
7. [Pliki konfiguracyjne](#pliki-konfiguracyjne)

---

## Wymagania

- .NET 8.0 SDK
- Dostęp do dwóch baz danych SQL Server (enova365):
  - **Baza źródłowa** - z której migrujemy dane (np. biuro rachunkowe)
  - **Baza docelowa** - do której migrujemy dane (np. klient)
- Uprawnienia do odczytu w bazie źródłowej
- Uprawnienia do zapisu w bazie docelowej

## Uruchomienie

```bash
# Tryb interaktywny (zalecany)
dotnet run

# Tryb testowy (tylko diagnostyka)
dotnet run -- --test

# Dry-run (symulacja migracji bez zmian w bazie)
dotnet run -- --dry-run

# Prawdziwa migracja (bez interaktywnego menu)
dotnet run -- --migrate
```

## Konfiguracja połączeń

Przy pierwszym uruchomieniu wybierz **opcję 1** i podaj dane połączeń:

```
Server: localhost,1433
Database: nazwa_bazy
User: sa
Password: ****
```

Konfiguracja zostanie zapisana w pliku `appsettings.json`.

### Przykładowy appsettings.json

```json
{
  "ConnectionStrings": {
    "SourceDatabase": "Server=localhost,1433;Database=biuro_db;User Id=sa;Password=***;TrustServerCertificate=True;",
    "TargetDatabase": "Server=localhost,1433;Database=klient_db;User Id=sa;Password=***;TrustServerCertificate=True;"
  },
  "Migration": {
    "MappingFilePath": "mapping.json"
  }
}
```

---

## Workflow migracji

### Krok 1: Konfiguracja i test połączeń

1. Uruchom aplikację: `dotnet run`
2. Wybierz **opcję 1** - skonfiguruj połączenia
3. Wybierz **opcję 2** - przetestuj połączenia

### Krok 2: Porównanie słowników

4. Wybierz **opcję 4** - porównaj definicje (DefElementow, DefNieobecnosci, itp.)
5. Wybierz **opcję 5** - porównaj pracowników

Aplikacja automatycznie dopasuje definicje po nazwie/kodzie i zbuduje mapowania.

### Krok 3: Analiza i decyzje

6. Wybierz **opcję 9** - WYKONAJ MIGRACJĘ
7. Aplikacja przeprowadzi analizę i wyświetli wykryte problemy

Dla każdego problemu (brakująca definicja, nowy pracownik, duplikat) możesz wybrać:

| Opcja | Opis |
|-------|------|
| **UTWÓRZ w target** | Skopiuj definicję z bazy źródłowej do docelowej |
| **Mapuj na istniejącą** | Wybierz odpowiednik z bazy docelowej (z wyszukiwaniem) |
| **Ustaw NULL** | Dla opcjonalnych pól - zostaw puste |
| **Pomiń rekordy** | Nie migruj rekordów używających tej definicji |

### Krok 4: Wykonanie migracji

8. Po rozwiązaniu wszystkich problemów wybierz **WYKONAJ MIGRACJĘ**
9. Potwierdź wykonanie (zalecane: najpierw DRY-RUN)
10. Po zakończeniu sprawdź statystyki i logi

---

## Menu główne

| Opcja | Opis |
|-------|------|
| 1. Konfiguracja połączeń | Ustaw connection stringi do baz źródłowej i docelowej |
| 2. Test połączeń | Sprawdź czy połączenia działają |
| 3. Statystyki baz danych | Porównaj liczby rekordów w tabelach |
| 4. Porównaj słowniki | Porównaj i zmapuj definicje (DefElementow, DefNieobecnosci, itp.) |
| 5. Porównaj pracowników | Dopasuj pracowników po PESEL lub Imię+Nazwisko |
| 6. Zapisz mapowanie | Zapisz aktualne mapowania do pliku JSON |
| 7. Wczytaj mapowanie | Wczytaj mapowania z pliku JSON |
| 8. Analiza | Pokaż co wymaga migracji (bez decyzji) |
| 9. WYKONAJ MIGRACJĘ | Pełny proces: analiza → decyzje → migracja |
| 0. Wyjście | Zamknij aplikację |

---

## Rozwiązywanie problemów

### Nawigacja w menu decyzji

- **Strzałki góra/dół** - wybór opcji
- **Enter** - zatwierdź wybór
- **Wpisywanie tekstu** - wyszukiwanie (przy mapowaniu)
- **"<< Wróć do poprzedniego"** - cofnij się do poprzedniego problemu
- **"<< Wróć do menu głównego"** - wyjdź z rozwiązywania

### Zapisywanie postępu

Decyzje są zapisywane do pliku `migration_plan.json`:
- Automatycznie przed migracją
- Przy wyborze "Zapisz plan i wyjdź"

Przy następnym uruchomieniu możesz wczytać zapisany plan.

### Typowe problemy

#### "Invalid column name 'XYZ'"
Struktura bazy źródłowej różni się od oczekiwanej. Sprawdź wersję enova365.

#### "Could not find color or style"
Błąd w formatowaniu Spectre.Console - upewnij się, że używasz najnowszej wersji aplikacji.

#### "Brak mapowania dla Pracownik ID=X"
Pracownik ze źródła nie został zmapowany. Wróć do opcji 5 (porównaj pracowników) lub rozwiąż problem w analizie.

#### Timeout podczas migracji
Dla dużych baz danych migracja może trwać długo. Rozważ migrację w mniejszych partiach.

---

## Pliki konfiguracyjne

| Plik | Opis |
|------|------|
| `appsettings.json` | Connection stringi i ustawienia |
| `mapping.json` | Mapowania definicji i pracowników |
| `migration_plan.json` | Plan migracji z decyzjami użytkownika |
| `migration_YYYYMMDD_HHMMSS.log` | Logi z wykonanej migracji |

---

## Migrowane tabele

Aplikacja migruje następujące tabele (w tej kolejności):

1. **Pracownicy** - dane osobowe pracowników
2. **Umowy** - umowy o pracę, zlecenia, itp.
3. **ListyPlac** - listy płac
4. **Wyplaty** - wypłaty pracowników
5. **WypElementy** - elementy wypłat (składniki)
6. **Rodzina** - członkowie rodzin pracowników
7. **Nieobecnosci** - urlopy, zwolnienia, itp.
8. **Dodatki** - dodatki do wynagrodzeń
9. **Adresy** - adresy pracowników
10. **RachBankPodmiot** - rachunki bankowe
11. **PracHistorie** - historia kadrowa
12. **Kalendarze** - kalendarze pracowników
13. **HistZatrudnien** - historia zatrudnień

---

## Mapowane definicje (słowniki)

| Tabela | Opis |
|--------|------|
| DefElementow | Definicje elementów wypłat (składniki) |
| DefNieobecnosci | Definicje nieobecności (urlopy, zwolnienia) |
| DefListPlac | Definicje list płac |
| DefDokumentow | Definicje dokumentów (typy umów) |
| Wydzialy | Struktura organizacyjna |
| Kalendarze | Kalendarze wzorcowe |
| UrzedySkarbowe | Urzędy skarbowe |

---

## Uwagi bezpieczeństwa

1. **ZAWSZE wykonaj backup bazy docelowej przed migracją**
2. Najpierw przetestuj na kopii bazy (DRY-RUN)
3. Nie przechowuj haseł w repozytorium git
4. Plik `appsettings.json` jest w `.gitignore`

---

## Wsparcie

W przypadku problemów:
1. Sprawdź logi migracji (`migration_*.log`)
2. Uruchom z flagą `--test` dla diagnostyki
3. Sprawdź `migration_plan.json` dla szczegółów decyzji

---

*Wersja dokumentacji: 1.0*
*Data: 2025*
