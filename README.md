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
    PublicAgentUrl = "https://adminapi.example.com:7443", // gdy port stoi za reverse proxy / TLS
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

### Dedykowany port

Samo zarejestrowanie biblioteki (`PrintersSetup.Register`) otwiera przy starcie aplikacji
**osobny listener Kestrel** na `AgentPort` (usługa hostowana `PrintAgentHubHost`). Konfiguracja
Kestrela hosta (`ListenAnyIP`, `ASPNETCORE_URLS`) nie jest ruszana, a agenci nie przechodzą
przez middleware/CORS/JWT aplikacji. Zajęty port nie wywraca aplikacji - loguje błąd, a przyciski
„Drukuj" po prostu otwierają PDF.

Na porcie działają tylko:

- `GET  /print-agent/info` - nazwa serwisu i wersja protokołu (anonimowo),
- `POST /print-agent/register` - wymiana jednorazowego kodu na token agenta (anonimowo),
- `/print-agent/hub` - hub SignalR; wymaga tokenu agenta (`Authorization: Bearer` lub `?access_token=`).

W produkcji port musi być osiągalny z komputerów użytkowników i szyfrowany (token agenta
podróżuje w nagłówku): najprościej terminacja TLS na reverse proxy z przekazywaniem WebSocket
(nginx: `proxy_http_version 1.1; Upgrade/Connection`) i wskazanie adresu w `PublicAgentUrl`.
Bez `PublicAgentUrl` adres jest wyprowadzany z żądania parującego (ten sam host, `AgentPort`) -
wystarcza w dev i on-premise bez proxy.

## Logowanie agenta (parowanie z kontem)

Jak w narzędziach CLI logujących przez przeglądarkę:

1. `ITBees.Printers.Agent.exe --site https://admin.example.com` - agent otwiera nasłuch na
   losowym porcie loopback i domyślną przeglądarkę na
   `{site}/print-agent/connect?port=…&state=…&machine=…` (jeśli `--site` ma własną ścieżkę,
   używana jest ona zamiast domyślnej).
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
oraz `InstantPrintService` w octoparkadmin.

Dokument nigdy nie jest zapisywany na serwerze - historia (`PrintJob`) trzyma tylko metadane i
jest przycinana po `PrintJobRetentionDays` (domyślnie 30).

## Agent Windows

- Ikona w zasobniku (zielona/żółta/czerwona), okno statusu z listą serwisów i dziennikiem,
  „Uruchamiaj przy starcie Windows" (klucz Run HKCU), jedna instancja na użytkownika (kolejne
  uruchomienie z `--site` przekazuje adres do działającej).
- Drukarki: WMI `Win32_Printer` (nazwa, domyślna, sterownik, port, offline, status), raport przy
  połączeniu, na żądanie i co 60 s gdy coś się zmieni.
- Wydruk: `Windows.Data.Pdf` renderuje strony w rozdzielczości drukarki →
  `System.Drawing.Printing` bez dialogów. Strona mieszcząca się na papierze drukowana 1:1
  i wyśrodkowana (etykieta 50 × 30 mm na rolce 50 × 30 mm ląduje dokładnie), większa jest
  zmniejszana do obszaru zadruku; orientacja dobierana do papieru. Kopie przez sterownik, a gdy
  ich nie obsługuje - przez powtórzenie stron.
- Połączenie: własna pętla reconnect z back-offem (2 s → 30 s) bez końca; 401 = token
  unieważniony → stan „wymaga zalogowania".
- Dziennik: `%LocalAppData%\ITBees\PrintAgent\logs\agent-yyyyMMdd.log` (14 dni). Bez treści
  dokumentów, tokenów i kodów.

Zmienne środowiskowe do diagnostyki / testów: `ITBEES_PRINT_AGENT_DATA_DIR` (osobny katalog
profili), `ITBEES_PRINT_AGENT_NO_BROWSER=1` (adres logowania do dziennika zamiast przeglądarki),
`ITBEES_PRINT_AGENT_PRINT_TO_DIR` (drukarki „do pliku", np. Microsoft Print to PDF, zapisują tam
zamiast pytać o nazwę pliku).

## Piaskownica

```
dotnet run --project ITBees.Printers.DevHost        # API + strona testowa: http://localhost:5190, agenci: 5191
ITBees.Printers.Agent.exe --site http://localhost:5190
```

Strona `index.html` piaskownicy pokazuje agentów i drukarki, ustawienia, drukuje stronę testową i
dowolny wgrany PDF. Baza jest w pamięci - po restarcie agent dostaje 401 i loguje się ponownie.
