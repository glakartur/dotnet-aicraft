# Automatyczne wykrywanie zmian zbioru plików w daemonie — wymagania

**Data:** 2026-11-15
**Status:** Szkic (wynik brainstormu, wymaga potwierdzenia)
**Poziom zakresu:** Standard

## Problem

Użytkownicy muszą ręcznie uruchomić `dotnet aicraft server reload` po dodaniu nowych plików `.cs` do projektu. Typowy przypadek: agent wykonuje refactor, który tworzy nowe pliki (np. wydziela klasę). Następne zapytania (`refs`, `symbols`, `callers`, `unused`, `diagnostics`) pracują na starym zbiorze plików.

Skutek jest groźny, bo nie ma błędu. Daemon daje **cichą złą odpowiedź**: nie widzi nowych symboli, pokazuje fałszywe "unused" i fałszywe błędy kompilacji (brakujący typ, który jest już w nowym pliku). Agent lub człowiek może na tej podstawie podjąć złą decyzję (np. usunąć kod).

## Przyczyna (potwierdzona w kodzie)

Daemon ma dwa watchery w `src/AICraftDaemon/Daemon/DaemonServer.cs`:

1. **Watcher plików `*.cs`** (`StartFileWatcher`) — słucha zdarzeń `Changed` i `Created`. Funkcja `ApplyFileChangeAsync` szuka dokumentu po ścieżce. Jeżeli dokumentu nie ma w solucji (nowy plik), funkcja kończy działanie bez akcji (`if (docIds.IsEmpty) return;`). Watcher nie słucha zdarzeń `Deleted` ani `Renamed`.
2. **Watcher metadanych** (`StartMetadataWatcher`) — przy zmianie `.sln`, `.csproj`, `.vbproj`, `.fsproj`, `Directory.Build.props/targets` robi pełny reload solucji (debounce 500 ms).

Wniosek: zmiana *treści* istniejącego pliku działa. Zmiana *zbioru* plików `.cs` (dodanie, usunięcie, zmiana nazwy, przeniesienie) nie działa. W projektach SDK-style pliki `.cs` są dołączane przez glob, więc `.csproj` się nie zmienia i watcher metadanych też nie reaguje.

Dodatkowe luki, które zauważyłem w trakcie analizy:
- Lista kandydatów do reloadu nie zawiera `.slnx`, mimo że auto-discovery obsługuje `.slnx` (do weryfikacji w planie).
- Lista nie zawiera `Directory.Packages.props` (Central Package Management).

## Rozważane podejścia

**A. Inkrementalna aktualizacja zbioru dokumentów (rekomendowane).** Daemon reaguje na dodanie, usunięcie i zmianę nazwy pliku `.cs`. Dodaje lub usuwa dokument w projekcie, do którego plik należy. Nie przeładowuje MSBuild.
- Zalety: szybko (ms), zapytania od razu widzą nowy plik, brak okna "Loading".
- Wady: daemon musi ustalić projekt właściciela; ryzyko rozjazdu z regułami MSBuild (np. `<Compile Remove>`, jawne listy `Compile` w projektach legacy, linkowane pliki, multi-targeting).
- Ryzyko ograniczamy przez fallback do pełnego reloadu, gdy przypisanie jest niepewne.

**B. Pełny reload przy każdej zmianie zbioru plików.** Dodanie, usunięcie lub zmiana nazwy `.cs` wywołuje ten sam mechanizm co zmiana `.csproj` (debounce + reload).
- Zalety: najprostsze, zawsze zgodne z MSBuild, ponowne użycie istniejącego mechanizmu.
- Wady: pełny reload dużej solucji trwa sekundy lub dłużej; refactor tworzy wiele plików naraz — zapytania w tym czasie czekają lub dostają `Loading`.

**C. Leniwe sprawdzenie przy zapytaniu (podejście odwrotne).** Daemon nie reaguje na zdarzenia. Przed zapytaniem porównuje zbiór plików na dysku z solucją i w razie różnicy synchronizuje.
- Zalety: odporne na zgubione zdarzenia `FileSystemWatcher` (przepełnienie bufora, WSL2, dyski sieciowe).
- Wady: koszt skanowania dysku przy każdym zapytaniu; większe opóźnienie pierwszego zapytania po zmianie.

**Rekomendacja:** A z fallbackiem do B. Opcjonalnie element C jako siatka bezpieczeństwa przy przepełnieniu bufora watchera (zdarzenie `Error`). Zmiana rozszerza istniejącą funkcję (watcher), nie tworzy nowej.

## Wymagania

**R1. Dodanie pliku.** Gdy użytkownik lub agent tworzy nowy plik `.cs` w katalogu projektu z solucji, następne zapytanie (po czasie debounce) uwzględnia symbole z tego pliku. Użytkownik nie uruchamia `server reload`.

**R2. Usunięcie pliku.** Gdy plik `.cs` zniknie z dysku, daemon usuwa go z solucji. Zapytania nie zwracają symboli ani lokalizacji z usuniętego pliku.

**R3. Zmiana nazwy i przeniesienie.** Zmiana nazwy lub przeniesienie pliku `.cs` (także między projektami) daje taki sam wynik jak usunięcie starej ścieżki i dodanie nowej.

**R4. Seria zmian.** Refactor, który w krótkim czasie tworzy lub usuwa wiele plików, powoduje jedną spójną aktualizację, nie serię częściowych stanów. Zapytanie w trakcie aktualizacji dostaje stan spójny (stary lub nowy), nie mieszany.

**R5. Fallback do pełnego reloadu.** Gdy daemon nie może pewnie ustalić, czy plik należy do projektu (np. brak projektu nadrzędnego, projekt z jawną listą `Compile`, kilka projektów obejmuje ten sam katalog), daemon wykonuje pełny reload zamiast zgadywać.

**R6. Pliki pomijane.** Daemon dalej pomija artefakty builda (`obj/`, `bin/`, `*.g.cs`, `*.g.i.cs`). Pliki tymczasowe edytorów i narzędzi (np. zapisy atomowe przez plik tymczasowy i rename) nie powodują błędnego dodania ani usunięcia dokumentu.

**R7. Zgubione zdarzenia.** Gdy watcher zgłosi błąd lub przepełnienie bufora, daemon wykonuje pełną synchronizację (reload), żeby nie zostać w cichym, nieaktualnym stanie.

**R8. Kompletność listy metadanych.** Zmiana `.slnx` i `Directory.Packages.props` wywołuje pełny reload, tak jak zmiana `.sln` i `.csproj`.

**R9. Dokumentacja.** Sekcja "Keeping the daemon in sync" w `plugins/dotnet-aicraft/skills/dotnet-aicraft/SKILL.md` (i `references/commands/server.md`) mówi, że dodanie, usunięcie i zmiana nazwy plików są wykrywane automatycznie. `server reload` zostaje jako narzędzie awaryjne.

**R10. Logi.** Daemon zapisuje w logu każde dodanie, usunięcie i fallback do reloadu (z przyczyną), tak jak teraz zapisuje `Updated: <plik>`.

## Kryteria sukcesu

- Scenariusz "refactor wydziela klasę do nowego pliku → `refs`/`symbols` na nowej klasie" daje poprawny wynik bez `server reload`, w czasie porównywalnym z obecną aktualizacją treści pliku (poniżej ~1 s po zapisie).
- `unused` i `diagnostics --severity error` nie zgłaszają fałszywych wyników po dodaniu lub usunięciu pliku.
- Testy integracyjne pokrywają: dodanie, usunięcie, rename, przeniesienie między projektami, serię wielu plików, fallback (plik poza projektem).

## Poza zakresem

- Pliki inne niż `.cs` (`.vb`, `.fs`, `.razor`, `.cshtml`, `.xaml`) — obecny watcher treści też obsługuje tylko `*.cs`.
- Pełna ewaluacja reguł MSBuild (`Compile Include/Remove`, warunki) przy dodaniu pliku — w przypadkach niepewnych działa fallback (R5).
- Zmiany w zewnętrznych referencjach (NuGet restore, nowe DLL) poza tym, co pokrywa R8.

## Założenia do potwierdzenia

1. Większość projektów użytkowników to projekty SDK-style z domyślnym globem `**/*.cs`. Dlatego plik w katalogu projektu (poza `obj/`, `bin/`) jest częścią projektu. Inne przypadki obsługuje fallback (R5).
2. Projekt właściciela to najbliższy katalog nadrzędny pliku, który zawiera plik projektu z solucji. Gdy żaden projekt nie pasuje, plik jest poza solucją i daemon go ignoruje (bez reloadu).
3. Krótki czas, w którym nowy plik jeszcze nie jest widoczny (debounce ok. 400–500 ms), jest akceptowalny. Nie blokujemy zapytań do zakończenia debounce.
4. Zmiana zbioru plików nie zmienia zachowania `rename` — rename dalej zapisuje przez `TryApplyChanges` i odświeża solucję jak teraz.
5. Gdy jest wątpliwość, poprawność ma priorytet nad szybkością: lepiej wykonać pełny reload niż zostawić nieaktualną solucję.
6. Brak luki `.slnx` w liście kandydatów do reloadu oceniłem na podstawie kodu `IsMetadataReloadCandidate`. Plan musi to potwierdzić (np. czy `.slnx` nie jest obsłużony w innym miejscu).
7. Dodanie `Directory.Packages.props` do listy reloadu (R8) jest tanie i zgodne z celem, więc włączyłem je do zakresu bez pytania.

## Pytania otwarte dla użytkownika

Brak. Wszystkie decyzje kierunkowe mają bezpieczne założenia (sekcja powyżej).

## Następny krok

`/ce-plan` na podstawie tego dokumentu.
