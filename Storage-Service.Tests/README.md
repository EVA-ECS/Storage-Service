# Tests – hier anfangen

Dieses Projekt enthält **6 Unit-Tests und 1 vollständigen Anwendungstest** in C# mit xUnit.
Die Dateien unter `Support` und das PowerShell-Skript sind Hilfen, keine weiteren Tests.

## Ordnerübersicht

```text
Storage-Service.Tests/
├── README.md                         ← diese Übersicht
├── Storage-Service.Tests.csproj       ← .NET-Testprojekt und Pakete
├── Run-E2E.ps1                        ← Starthelfer für alle sieben Tests
├── Unit/                             ← sechs schnelle Tests mit simulierten Daten
│   ├── SupabaseChatMessageStoreTests.cs
│   ├── ReceiverMappingTests.cs        ← Empfänger-ID und beide Richtungen
│   └── WorkerPoolTests.cs
└── EndToEnd/                          ← echte vollständige Anwendung
    ├── README.md                     ← Einrichtung und geprüfte Anforderungen
    ├── FrontendToDeliveryTests.cs     ← eigentlicher Anwendungstest
    └── Support/                      ← technische Hilfen
        ├── E2eSettings.cs            ← Einstellungen prüfen
        ├── E2eSystem.cs              ← Docker, RabbitMQ und Supabase abfragen
        ├── E2eModels.cs              ← Daten für Vergleiche und Nachweise
        └── EndToEndFactAttribute.cs   ← echten Test nur gezielt aktivieren
```

Zusätzlich entstehen `bin/`, `obj/` und `TestResults/` automatisch. Das sind Build-Dateien
und Testergebnisse, kein Testquellcode. Sie werden von Git ignoriert.

## Was möchtest du ansehen?

| Thema | Datei öffnen | Was steht dort? |
|---|---|---|
| Speichern und Fehler | [SupabaseChatMessageStoreTests.cs](Unit/SupabaseChatMessageStoreTests.cs) | Drei Tests: privater Raum, fehlender Raum, Supabase-Fehler |
| Drei parallele Worker | [WorkerPoolTests.cs](Unit/WorkerPoolTests.cs) | Ein Test mit drei gleichzeitig aktiven Workern und einem Testspeicher |
| Empfänger korrekt speichern | [ReceiverMappingTests.cs](Unit/ReceiverMappingTests.cs) | Zwei Tests: A → B und B → A korrekt zuordnen; ungültige Empfänger-ID ablehnen |
| Frontend bis Delivery-Queue | [FrontendToDeliveryTests.cs](EndToEnd/FrontendToDeliveryTests.cs) | Ein echter Test: Browser, Gateway, RabbitMQ, Storage, Supabase, `delivery_queue` |
| Anwendungstest vorbereiten | [Anleitung](EndToEnd/README.md) | Voraussetzungen, Zugangsdaten, Start und Grenzen des Tests |
| Alle sieben Tests starten | [Run-E2E.ps1](Run-E2E.ps1) | Einstellungen vorbereiten und anschließend `dotnet test` aufrufen |
| Datenbank vorbereiten | [Migration und Altbestände](../Database/README.md) | `receiver_id` ergänzen; keine automatische Änderung an Supabase |
| Migration isoliert testen | [Run-MigrationTest.ps1](../Database/Tests/Run-MigrationTest.ps1) | Temporäre lokale PostgreSQL-Datenbank; Supabase bleibt unverändert |
| Technischen Hilfscode verstehen | [Einstellungen](EndToEnd/Support/E2eSettings.cs), [Systemzugriffe](EndToEnd/Support/E2eSystem.cs), [Datenmodelle](EndToEnd/Support/E2eModels.cs), [Aktivierung](EndToEnd/Support/EndToEndFactAttribute.cs) | Hilfen für den Anwendungstest; hier stehen keine zusätzlichen Testfälle |

## Tests ausführen

Alle Befehle im **Storage-Projektordner**, eine Ebene oberhalb dieser Datei, ausführen.
Voraussetzung: .NET 8 und Zugriff auf die NuGet-Pakete einschließlich des Contracts-Pakets.
Falls `dotnet` nicht im PATH liegt, den vollständigen Pfad zur lokalen `dotnet.exe` verwenden.

### Nur die sechs Unit-Tests

```powershell
dotnet test .\Storage-Service.Tests\Storage-Service.Tests.csproj --filter "Category=Unit"
```

Erwartung bei Erfolg: **6 bestanden, 0 fehlgeschlagen**. Dafür sind weder Docker noch echte
Supabase-Zugangsdaten nötig. Die Speicherung wird durch Testspeicher/HTTP-Antworten simuliert.

Ohne Filter werden dieselben sechs Tests ausgeführt und der Anwendungstest ohne `E2E_RUN=1`
zusätzlich als **übersprungen** angezeigt. Die vorhandenen Testnamen und bisherigen Befehle bleiben gültig.

### Sechs Unit-Tests und der echte Anwendungstest

Zuerst die [Testumgebung vorbereiten](EndToEnd/README.md). Die echte Anwendung muss laufen.

```powershell
.\Storage-Service.Tests\Run-E2E.ps1 -ComposeFile "C:\Pfad\zur\lokalen\compose.yaml" -UseLocalStackConfig
```

Der Helfer fragt fehlende Testdaten ab, das Passwort verdeckt. Bei Erfolg: **7 bestanden,
0 fehlgeschlagen, 0 übersprungen**. Mit `-ShowBrowser` kannst du den Browserablauf sehen.
Der Test pausiert Storage kurz und erzeugt drei echte Testnachrichten. Er löscht keine vorhandenen Daten.

## Nachweise finden

Der Starthelfer schreibt für jeden Anwendungstest einen eigenen Ordner unter `TestResults/E2E-.../`:

- `before.json`: drei Nachrichten warten in `storage_queue`, noch keine passenden Supabase-Einträge oder neuen Delivery-Events.
- `after.json`: Supabase-Einträge (auch `ReceiverId`), Delivery-Events und Queue-Zähler nach der Verarbeitung; `result: "passed"` nur nach erfolgreichen Prüfungen.
- `storage.log`: die drei Worker und pro Nachricht Speicherung vor Weiterleitung.

Vergleiche die Nachrichten-IDs in den drei Dateien, nicht nur die Gesamtzahl in der Queue.
Ein fehlgeschlagener Lauf kann unvollständige Nachweise hinterlassen; maßgeblich ist auch das Terminalergebnis.
Der Test endet bei `delivery_queue`. History-Laden mit beiden Benutzer-Sitzungen, RLS-Zugriffsrechte,
Zustellung beim Empfänger sowie Ausfall-/Retry-Szenarien sind nicht enthalten.
Der neue Empfänger-Test prüft die gespeicherten Felder in beiden Richtungen, keinen History-Endpunkt.

## Navigation in VS Code

- Im Explorer `Storage-Service.Tests` öffnen und hier mit `README.md` anfangen.
- Mit **Strg+Umschalt+V** die Markdown-Vorschau öffnen; dort sind die Dateilinks anklickbar.
- Mit **Strg+P** einen Dateinamen eingeben, z. B. `WorkerPoolTests`.
- In einer C#-Datei zeigt **Strg+Umschalt+O** die Tests und Hilfsmethoden dieser Datei.
