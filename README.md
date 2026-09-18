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

W obu przypadkach agenci omijają middleware/CORS/JWT aplikacji. Gdy proxy nie przepuszcza
upgrade'u WebSocket dla tej ścieżki, klient SignalR sam schodzi na SSE / long polling.

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
   (`http://` dla localhost). Agent najpierw sprawdza, czy pod adresem w ogóle jest strona
   łączenia (404 bez powłoki SPA → od razu czytelny błąd „to nie jest adres panelu"), potem
   otwiera nasłuch na losowym porcie loopback i domyślną przeglądarkę na
   `{site}/print-agent/connect?port=…&state=…&machine=…` (jeśli `--site` ma własną ścieżkę,
   używana jest ona zamiast domyślnej). Logowanie w toku widać w oknie agenta; ponowne wywołanie
   dla tego samego serwisu zastępuje poprzednią próbę, a „Anuluj logowanie" ją przerywa.
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
dotnet run --project ITBees.Printers.DevHost        # API + strona testowa: http://localhost:5190, agenci: 5190 i 5191
ITBees.Printers.Agent.exe --site localhost:5190
```

Druga instancja obok działającej: `--ApiPort=5290 --AgentPort=5291`; `--AgentPort=0` wyłącza
dedykowany port, `--ExposeOnApplicationPort=false` zostawia agentom tylko jego.

Strona `index.html` piaskownicy pokazuje agentów i drukarki, ustawienia, drukuje stronę testową i
dowolny wgrany PDF. Baza jest w pamięci - po restarcie agent dostaje 401 i loguje się ponownie.
