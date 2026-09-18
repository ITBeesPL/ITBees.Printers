# ITBees.Printers - drukowanie natychmiastowe przez agenta Windows

Biblioteka dla aplikacji webowych ITBees (wzorzec jak w ITBees.Products / ITBees.Inpost) plus
aplikacja Windows **ITBees Print Agent**. Użytkownik panelu klika „Drukuj", a dokument (PDF)
trafia od razu na drukarkę podłączoną do jego komputera - bez okna przeglądarki i dialogu
drukowania. Kto woli, dalej pobiera PDF. Ustawienia są **osobiste**: każdy użytkownik wybiera
dla siebie, per rodzaj dokumentu, drukarkę albo PDF.

```
[panel WWW] --"Drukuj"--> [API hosta: POST /PrintJob] --SignalR (dedykowany port)--> [ITBees Print Agent] --> [drukarka]
```

## Co zawiera repozytorium

| Projekt | Rola |
|---|---|
| `ITBees.Printers` | biblioteka serwerowa (pakiet NuGet): encje, serwisy, kontrolery REST, **własny listener** dla agentów |
| `ITBees.Printers.Agent` | aplikacja Windows (tray, .NET 8, WinForms) - logowanie przez przeglądarkę, raport drukarek, cichy wydruk PDF |
| `ITBees.Printers.DevHost` | piaskownica: API z bazą w pamięci i jednym zawsze zalogowanym użytkownikiem + strona testowa (`dotnet run`, port 5190/5191) |

Protokół agent ↔ serwer (`ITBees.Printers/Protocol/*.cs`) jest kompilowany do obu projektów
(linkowane źródła), dzięki czemu biblioteka pozostaje jednym pakietem.

## Podłączenie w aplikacji hosta

```csharp
// DependencyRegistration:
new ITBees.Printers.Setup.PrintersSetup().Register(builder.Services, new PrintersSettings
{
    ServiceName = "Octopark Admin",          // nazwa pokazywana w aplikacji agenta
    AgentPort = 7081,                        // dedykowany port dla agentów; 0 = wyłączone
    ExposeOnApplicationPort = true,          // (domyślnie) te same końcówki także na porcie API
    PublicAgentUrl = null,                   // zwykle puste - patrz „Jaki adres dostaje agent"
    RequiredRole = Role.PlatformOperator,    // null = każdy zalogowany użytkownik
    DocumentTypes = { new PrintDocumentType("StockLabel", "Etykiety magazynowe 50 × 30 mm") }
});

// DbContext.OnModelCreating:
ITBees.Printers.Setup.DbModelBuilder.Register(modelBuilder);
// + wygeneruj migrację EF (4 nowe tabele: PrintAgent, PrintAgentPrinter, UserPrintSetting, PrintJob)
```

Host z jawną listą kontrolerów dopisuje: `PrintAgentRegistrationController`, `PrintAgentController`,
`PrintAgentsController`, `PrintAgentPrintersRefreshController`, `PrintSettingController`,
`PrintSettingsController`, `PrintJobController`, `PrintTestPageController`
(namespace `ITBees.Printers.Controllers`). Wszystkie mają `[Authorize]`.

Wymagania: generyczne repozytoria ITBees (`IReadOnlyRepository<>` / `IWriteOnlyRepository<>`),
`IAspCurrentUserService` (ITBees.UserManager), encja `UserAccount` w DbContext hosta.

### Końcówki agenta: dedykowany port i port aplikacji

Samo zarejestrowanie biblioteki (`PrintersSetup.Register`) uruchamia przy starcie aplikacji
usługę `PrintAgentHubHost` - drugi, minimalny host WWW z własnym kontenerem DI, który serwuje
tylko:

- `GET  /print-agent/info` - nazwa serwisu i wersja protokołu (anonimowo),
- `POST /print-agent/register` - wymiana jednorazowego kodu na token agenta (anonimowo),
- `/print-agent/hub` - hub SignalR; wymaga tokenu agenta (`Authorization: Bearer` lub `?access_token=`).

Ten sam potok (ten sam hub, ten sam rejestr połączeń) jest dostępny dwiema drogami:

1. **Dedykowany port** `AgentPort` - osobny listener Kestrel. Konfiguracja Kestrela hosta
   (`ListenAnyIP`, `ASPNETCORE_URLS`) nie jest ruszana. Zajęty port nie wywraca aplikacji.
2. **Port aplikacji** (`ExposeOnApplicationPort`, domyślnie włączone) - `IStartupFilter` wstawia
   na początek potoku hosta middleware, które przejmuje wyłącznie trzy powyższe ścieżki (reszta,
   łącznie z ewentualną stroną `/print-agent/connect` frontendu, trafia do aplikacji bez zmian).
   Zero zmian w `Program.cs`. Agenci wchodzą wtedy **przez adres, pod którym API już jest
   opublikowane** - to samo reverse proxy, ten sam certyfikat TLS, żadnego nowego portu do
   otwarcia w firewallu (także po stronie sieci klienta, która często blokuje nietypowe porty
   wychodzące). To jest droga zalecana dla produkcji.

W obu przypadkach agenci omijają middleware/CORS/JWT aplikacji.

### Reverse proxy (nginx) i transporty

Agent próbuje transportów po kolei - WebSocket, server-sent events (SSE), long polling - po
jednym na próbę, i zostaje przy pierwszym, który zadziała (do zamknięcia aplikacji; po starcie
znowu od WebSocketu). Sam SignalR schodzi niżej tylko wtedy, gdy transport w ogóle nie
wystartuje, a proxy psują je na różne sposoby:

- nginx bez ustawień WebSocket odpowiada na upgrade zwykłym `200` („The server returned status
  code '200' when status code '101' was expected");
- jego buforowanie odpowiedzi (domyślnie włączone) wstrzymuje strumień SSE - razem z nagłówkami -
  do końca odpowiedzi, a serwer po 15 s zrywa połączenie, bo nie dostał handshake'u („Handshake
  was canceled" / „The server disconnected before the handshake could be started"). Transport
  formalnie wystartował, więc SignalR już nie schodzi do long pollingu - robi to agent;
- proxy, które upgrade **połyka** (bez żadnej odpowiedzi), kończy próbę po 20 s.

Każde zejście jest w dzienniku („no connection over WebSocket to … - trying server-sent events"),
a stan „Połączono (long polling)" / „Połączono (server-sent events)" w oknie agenta znaczy: działa,
ale proxy nie przepuszcza WebSocketów. Po stronie serwera biblioteka dokłada do odpowiedzi huba
`X-Accel-Buffering: no` (nginx nie buforuje wtedy SSE) i trzyma bezczynny long poll 50 s (domyślne
90 s SignalR przekracza domyślny `proxy_read_timeout` nginx 60 s, co dawałoby 504). Właściwa
poprawka to przepuszczenie WebSocketów przez proxy, np. w nginx:

```nginx
# w http { } - raz
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

# w server { } API, obok location /
location /print-agent/hub {
    proxy_pass         http://…;   # to samo co w location /
    proxy_http_version 1.1;
    proxy_set_header   Upgrade $http_upgrade;
    proxy_set_header   Connection $connection_upgrade;
    proxy_set_header   Host $host;
    proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto $scheme;
    proxy_buffering    off;
    proxy_read_timeout 120s;
}
```

### Jaki adres dostaje agent

Adres (`HubUrl`) wydaje `POST /PrintAgentRegistration`, w tej kolejności:

1. `PublicAgentUrl`, jeśli ustawione (brak schematu = `https://`; dla localhost `http://`). Używaj
   tylko, gdy agenci mają wchodzić inaczej niż API - np. dedykowany port za terminatorem TLS:
   `"https://adminapi.example.com:7443"`.
2. Przy `ExposeOnApplicationPort`: **adres, pod którym frontend sam sięga do API** - strona
   łączenia przesyła go w `PrintAgentRegistrationIm.ApiUrl` (w Angularze: `environment.webApiUrl`).
   To jedyny adres, o którym wiadomo, że działa z komputera użytkownika, łącznie ze schematem
   (aplikacja za proxy terminującym TLS widzi u siebie zwykłe `http`). Działa bez konfiguracji
   na produkcji i na localhost.
3. Dalej: pochodzenie żądania (`X-Forwarded-Proto/Host`, potem `Request.Scheme/Host`).
4. Bez `ExposeOnApplicationPort`: host żądania + `AgentPort` (dev, on-premise bez proxy) - wtedy
   port musi być osiągalny z komputerów użytkowników, a w produkcji szyfrowany (token agenta
   podróżuje w nagłówku).

## Logowanie agenta (parowanie z kontem)

Jak w narzędziach CLI logujących przez przeglądarkę:

1. `ITBees.Printers.Agent.exe --site admin.example.com` (albo „Połącz z serwisem…" w oknie
   agenta) - podaje się **adres panelu WWW**, nie API; brakujące `https://` agent dopisuje sam
   (`http://` dla localhost). Panel wystawiony pod podścieżką podaje się razem z nią
   (`kilometrowka.net/adm` → `https://kilometrowka.net/adm/print-agent/connect`); adres, który
   już wskazuje stronę łączenia (zawiera `print-agent`), jest brany bez zmian. Agent najpierw
   sprawdza, czy pod adresem w ogóle jest strona łączenia (404 bez powłoki SPA → od razu czytelny
   błąd „to nie jest adres panelu"), potem otwiera nasłuch na losowym porcie loopback i domyślną
   przeglądarkę na `{site}/print-agent/connect?port=…&state=…&machine=…`. Logowanie w toku widać
   w oknie agenta; ponowne wywołanie dla tego samego serwisu zastępuje poprzednią próbę, a
   „Anuluj logowanie" ją przerywa.
2. Strona hosta (za zwykłym logowaniem użytkownika) pokazuje nazwę komputera i prosi o
   potwierdzenie. Po kliknięciu woła `POST /PrintAgentRegistration` (autoryzowane JWT
   użytkownika) i przekierowuje przeglądarkę na
   `http://127.0.0.1:{port}/callback?state=…&code=…&hub=…&service=…`. Strona bierze z adresu
   **wyłącznie numer portu** - kod nigdy nie może trafić poza ten komputer.
3. Agent sprawdza `state`, wymienia kod na własny, długowieczny token
   (`POST {hub}/print-agent/register`) i łączy się z hubem. Serwer trzyma tylko SHA-256 tokenu;
   agent trzyma token zaszyfrowany DPAPI (`%AppData%\ITBees\PrintAgent\profiles.json`).

Hasło i sesja użytkownika nigdy nie przechodzą przez agenta. Ponowne sparowanie tego samego
komputera przejmuje istniejący wpis (drukarki i ustawienia zostają, stary token przestaje
działać). Usunięcie agenta w ustawieniach konta → `Revoked` po hubie + 401 przy kolejnym
połączeniu → agent kasuje token i prosi o ponowne logowanie. Jeden agent może być połączony z
wieloma serwisami (osobny port/token dla każdego).

## Ustawienia użytkownika i przycisk „Drukuj"

- `GET /PrintSettings` - ustawienie domyślne (`"*"`) i po jednym wpisie na `DocumentType`; każdy
  wpis niesie też wartości **efektywne** (po dziedziczeniu z domyślnego) i `isPrinterReady`.
- `PUT /PrintSetting` - `{ documentType, mode, printerGuid, copies }`; `mode`:
  `0` PDF, `1` drukuj natychmiast, `2` jak domyślne (tylko dla konkretnego typu - kasuje wpis).
- `GET /PrintAgents`, `DELETE /PrintAgent?guid=`, `POST /PrintAgentPrintersRefresh?agentGuid=`,
  `POST /PrintTestPage { printerGuid }`.
- `POST /PrintJob` - `{ documentType, documentName, contentBase64, copies? }` → `PrintJobVm`
  (`status`, `shouldFallBackToPdf`, `isFinished`); `GET /PrintJob?guid=` do odpytywania wyniku.
  Host może też drukować dokument wygenerowany po stronie serwera przez `IPrintJobService.Print(...)`.

Frontend przy „Drukuj": pobiera PDF jak dotąd → `GET /PrintSettings?documentType=` → jeśli
efektywnie „natychmiast" i drukarka gotowa, `POST /PrintJob`, w przeciwnym razie (i przy każdym
błędzie / `shouldFallBackToPdf`) otwiera PDF. Wzorzec: `ITBees.Printers.DevHost/wwwroot/index.html`
oraz `InstantPrintService` w octoparkadmin i kilometrowka-adm.

### Podłączeni hości

| Host | Panel / strona łączenia | Typy dokumentów |
|---|---|---|
| Octopark AdminApi | octoparkadmin: Ustawienia → Drukowanie, `/print-agent/connect` | `StockLabel` (etykiety magazynowe 50 × 30 mm) |
| Kilometrówka Api | kilometrowka-adm: menu „Drukowanie", `/print-agent/connect` (poza powłoką panelu, powrót po logowaniu przez własne guardy) | `InpostLabel` (etykiety InPost A6 - ITBees.Inpost `?type=A6`) |

Dokument nigdy nie jest zapisywany na serwerze - historia (`PrintJob`) trzyma tylko metadane i
jest przycinana po `PrintJobRetentionDays` (domyślnie 30).

## Agent Windows

- Ikona w zasobniku (zielona/żółta/czerwona), okno statusu z listą serwisów i dziennikiem,
  „Uruchamiaj przy starcie Windows" (klucz Run HKCU), jedna instancja na użytkownika. Ponowne
  uruchomienie **tego samego** exe przekazuje adres (`--site`) działającej kopii. Kopia uruchomiona
  **z innego miejsca** (nowszy build, aktualizacja, inny folder) zastępuje działającą - wygrywa
  uruchomiona jako ostatnia: nowe wersje zamykają się same (`--quit`), starsze (bez `--quit`) są
  kończone po 6 s; wpis autostartu wskazujący zastąpioną kopię jest przepinany na nową. Bez tego
  nowy build oddawałby polecenie staremu, który dalej działał po staremu.
- Wersja z datą builda (`1.0.0 (build 2026-09-18 17:58)`, metadane `BuildTimestamp` z csproj):
  w tytule okna, w menu ikony, w dzienniku i w panelu serwisu („wersja …" przy komputerze) -
  widać, który build naprawdę działa.
- Drukarki: WMI `Win32_Printer` (nazwa, domyślna, sterownik, port, offline, status), raport przy
  połączeniu, na żądanie i co 60 s gdy coś się zmieni.
- Wydruk: `Windows.Data.Pdf` renderuje strony w rozdzielczości drukarki →
  `System.Drawing.Printing` bez dialogów. Strona mieszcząca się na papierze drukowana 1:1
  i wyśrodkowana (etykieta 50 × 30 mm na rolce 50 × 30 mm ląduje dokładnie), większa jest
  zmniejszana do obszaru zadruku; orientacja dobierana do papieru. Kopie przez sterownik, a gdy
  ich nie obsługuje - przez powtórzenie stron.
- Połączenie: własna pętla reconnect z back-offem (2 s → 30 s) bez końca; każda próba to nowe
  połączenie z limitem 20 s na jednym transporcie (kolejność transportów i kiedy agent przechodzi
  do następnego - patrz „Reverse proxy (nginx) i transporty"); 401 = token unieważniony → stan
  „wymaga zalogowania".
- Dziennik: `%LocalAppData%\ITBees\PrintAgent\logs\agent-yyyyMMdd.log` (14 dni). Bez treści
  dokumentów, tokenów i kodów.

### Aktualizacje agenta

Opublikowany agent (jeden plik exe, np. `ITBeesFastPrintAgent.exe`) przy **każdym starcie** czyta
`https://api.itbees.pl/ITBeesFastPrintAgent/latestversion.json`. Jeśli opublikowana wersja jest
wyższa od własnej, pyta w oknie „Aktualizuj / Nie teraz". Po odmowie zapyta przy następnym
starcie. Plik generuje build (TeamCity, zaraz po `dotnet publish ... -p:Version=%build.number%`):

```json
{
  "version": "1.0.42",
  "fileName": "ITBeesFastPrintAgent.exe",
  "size": 78637439,
  "sha256": "e4e8b56a…",
  "publishedAt": "2026-09-18T18:28:43+00:00"
}
```

Wymagane są `version` i `fileName` (exe leży obok JSON-a) albo `downloadUrl` (pełny lub względny
adres). `size` i `sha256` są sprawdzane, jeśli są podane. Na serwer wgrywa się najpierw exe,
a JSON na końcu - inaczej klient może zobaczyć wersję, której pliku jeszcze nie ma.

Przebieg po kliknięciu „Aktualizuj":

1. Pobranie do `ITBeesFastPrintAgent.update.exe` obok działającego exe (z paskiem postępu,
   anulowalne). Sprawdzane są nagłówek `MZ`, rozmiar i SHA-256. Przy błędzie komunikat zostaje
   w oknie, a plik jest usuwany.
2. Agent zamyka się (rozłącza serwisy, zwalnia blokadę jednej instancji) i uruchamia
   `….update.exe --apply-update "<ścieżka exe>" <pid>`.
3. Nowa wersja czeka na koniec starego procesu, kopiuje się do `<exe>.tmp`, jednym `rename`
   nadpisuje stary exe (ta sama ścieżka, więc wpis „Uruchamiaj przy starcie Windows" dalej
   działa) i startuje go z `--minimized --updated`. Pokazuje się dymek „Zaktualizowano do wersji
   …", a ten proces usuwa `.update.exe`.
4. Jeśli cokolwiek w kroku 2 lub 3 się nie uda, startuje dotychczasowy exe z `--update-failed`
   (dymek „Aktualizacja nie powiodła się"). Użytkownik nigdy nie zostaje bez działającego agenta.

Dlaczego nie „zmień nazwę działającego exe i podłóż nowy": aplikacja single-file doładowuje
biblioteki ze swojego exe leniwie. Po podmianie pliku pod jej ścieżką proces nie potrafił już
nawet uruchomić innego procesu. Argument `--apply-update` to kontrakt między kolejnymi wersjami
i nie należy go zmieniać.

Ograniczenia: aktualizuje się tylko build single-file (build z `bin\` to exe + DLL-e, pomija
sprawdzanie). Folder z exe musi być zapisywalny - w `Program Files` okno pokaże błąd z linkiem do
ręcznego pobrania. Tylko https (http wyłącznie dla serwera testowego na tym komputerze).
SHA-256 z tego samego serwera chroni przed uszkodzonym pobraniem, nie przed podmianą na
serwerze - tę lukę zamknęłoby podpisywanie exe (Authenticode) i sprawdzanie podpisu przed
uruchomieniem. Zamknięcie i ponowny start z pobraną wersją trwa kilkanaście sekund (m.in.
skanowanie antywirusowe nowego 75-megabajtowego exe) i w tym czasie ikony nie ma w zasobniku.

Zmienne środowiskowe do diagnostyki / testów: `ITBEES_PRINT_AGENT_DATA_DIR` (osobny katalog
profili), `ITBEES_PRINT_AGENT_NO_BROWSER=1` (adres logowania do dziennika zamiast przeglądarki),
`ITBEES_PRINT_AGENT_PRINT_TO_DIR` (drukarki „do pliku", np. Microsoft Print to PDF, zapisują tam
zamiast pytać o nazwę pliku), `ITBEES_PRINT_AGENT_DRY_RUN=1` (dokument jest parsowany i
renderowany, ale nie trafia do spoolera - z `PRINT_TO_DIR` strony zapisują się jako PNG),
`ITBEES_PRINT_AGENT_TRACE=1` (dziennik klienta SignalR - negocjacja, transporty, handshake - do
pliku dziennika jako linie `[TRC]`, bez tokenów i treści dokumentów; tak widać, co robi proxy po
drodze), `ITBEES_PRINT_AGENT_TRANSPORTS` (np. `LongPolling` albo `ServerSentEvents,LongPolling` -
tylko te transporty, bez schodzenia po kolei), `ITBEES_PRINT_AGENT_UPDATE_URL` (inny adres
`latestversion.json`, np. `http://127.0.0.1:5399/ITBeesFastPrintAgent/latestversion.json`, albo
`off` - bez sprawdzania aktualizacji). Tryb
„na sucho" jest wart używania w testach: każdy prawdziwy wydruk Windows (przy włączonym
„Zezwalaj systemowi Windows na zarządzanie drukarką domyślną") robi z użytej drukarki drukarkę
domyślną.

## Piaskownica

```
dotnet run --project ITBees.Printers.DevHost        # API + strona testowa: http://localhost:5190, agenci: 5190 i 5191
ITBees.Printers.Agent.exe --site localhost:5190
```

Druga instancja obok działającej: `--ApiPort=5290 --AgentPort=5291`; `--AgentPort=0` wyłącza
dedykowany port, `--ExposeOnApplicationPort=false` zostawia agentom tylko jego.

Strona `index.html` piaskownicy pokazuje agentów i drukarki, ustawienia, drukuje stronę testową i
dowolny wgrany PDF. Baza jest w pamięci - po restarcie agent dostaje 401 i loguje się ponownie.
