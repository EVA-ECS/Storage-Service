# Echter Anwendungstest in C#

[Zur Testübersicht](../README.md)

`Frontend -> Gateway -> RabbitMQ/storage_queue -> Storage -> Supabase -> delivery_queue`

Der Testcode liegt in [FrontendToDeliveryTests.cs](FrontendToDeliveryTests.cs).
Er benutzt xUnit und Playwright, keine simulierten Dienste.
Die technischen Hilfen sind separat unter `Support/`:

- [E2eSettings.cs](Support/E2eSettings.cs): Einstellungen und Zugangsdaten aus Umgebungsvariablen prüfen.
- [E2eSystem.cs](Support/E2eSystem.cs): Docker steuern sowie RabbitMQ und Supabase abfragen.
- [E2eModels.cs](Support/E2eModels.cs): Datenmodelle für Queue-Zähler, Datenbankzeilen und Events.
- [EndToEndFactAttribute.cs](Support/EndToEndFactAttribute.cs): echten Test nur mit `E2E_RUN=1` aktivieren.

## Voraussetzungen (einmal vorbereiten)

- .NET 8, Docker Desktop, Zugriff auf die NuGet-Pakete einschließlich des Contracts-Pakets.
- Eine **eigene lokale Testumgebung** mit laufendem Frontend, Gateway, RabbitMQ, Redis,
  UserService und Storage. Die Compose-Dienste müssen `storage`, `gateway` und `rabbitmq`
  heißen. Der Compose-Projektname muss mit `eva-storage-e2e` anfangen.
- Standard-URLs: Frontend `http://localhost:8081`, Gateway `http://localhost:8082`,
  RabbitMQ-Management `http://localhost:15673`. Andere lokale Basis-URLs sind konfigurierbar.
- Echtes Supabase mit dem Schema `rooms`, `room_members`, `profiles`, `messages`.
  Die [Migration für `messages.receiver_id`](../../Database/README.md) muss bereits angewendet sein,
  und der laufende Storage muss den neuen Code enthalten. Vor dem Versand wird die Spalte geprüft.
- Ein bestätigtes Sender-Testkonto mit bekanntem Passwort sowie ein Empfänger-Testkonto.
  Beide müssen bereits genau einen gemeinsamen privaten Raum haben. Dieser Raum hat
  `is_group=false` und genau diese beiden Mitglieder. Der Test erstellt keine Konten/Räume.
- Kein Delivery-Consumer: Der Test endet bei `delivery_queue`.
- Anfangs leere `storage_queue`, keine Fehler-/Skipped-Nachrichten, höchstens 97 alte
  Delivery-Nachrichten. Alte Nachrichten werden nicht gelöscht; mehr als 97 führt zum Abbruch.
- Microsoft Edge ist unter Windows bereits verwendbar. Alternativ Chromium installieren lassen.

**Der Test startet nicht die gesamte Anwendung aus Quellcode.** Er prüft die laufende
Gesamtanwendung und pausiert/startet nur Storage. Der lokale Starthelfer des Gesamtprojekts
kann zuvor verwendet werden. Nicht auf einen gemeinsam genutzten Broker oder eine Produktivumgebung richten.

## Ein Befehl für sechs Unit-Tests plus Anwendungstest

Im Storage-Projektordner, PowerShell:

```powershell
.\Storage-Service.Tests\Run-E2E.ps1 -ComposeFile "C:\Pfad\zur\lokalen\compose.yaml" -UseLocalStackConfig
```

`-UseLocalStackConfig` übernimmt Supabase-URL, Secret Key und RabbitMQ-Zugang nur aus dem
ausdrücklich gewählten laufenden Storage-Testcontainer. Die Werte werden nicht ausgegeben oder gespeichert.
E-Mail, Passwort, Empfänger und privater Raum werden abgefragt. Das Passwort wird verdeckt eingegeben.
Das Skript baut die Tests und führt **6 Unit-Tests + 1 End-to-End-Test** aus.

Weitere Optionen:

- `-ShowBrowser`: Browser sichtbar ausführen.
- `-Email`, `-TargetEmail`, `-TargetId`, `-RoomId`: nicht geheime Testdaten vorgeben.
- `-BrowserChannel ''`: den von Playwright unterstützten Chromium-Browser installieren/verwenden.
- `-NuGetConfig "Pfad\NuGet.Config"`: Paketquelle explizit wählen.

Unter Windows bei blockierter lokaler Skriptausführung:

```powershell
powershell -NoProfile -ExecutionPolicy RemoteSigned -File .\Storage-Service.Tests\Run-E2E.ps1 -ComposeFile "C:\Pfad\compose.yaml" -UseLocalStackConfig
```

Die Ausführungsregel gilt nur für diesen Prozess, nicht dauerhaft für Windows.

## Direkt mit dotnet test (auch für CI)

Statt des Helfers können diese Umgebungsvariablen gesetzt werden. Geheimnisse aus einem
Secret Store oder verdeckter Eingabe beziehen, nicht in Git, Befehlszeilenargumente oder Logs schreiben.

| Variable | Zweck |
|---|---|
| `E2E_RUN=1` | Den sonst übersprungenen Anwendungstest aktivieren |
| `E2E_ALLOW_TEST_WRITES=1` | Drei echte Frontend-Testnachrichten und die Storage-Pause erlauben |
| `E2E_COMPOSE_FILE` | Absoluter Pfad der lokalen Test-Compose-Datei |
| `E2E_EMAIL`, `E2E_PASSWORD` | Sender-Testkonto |
| `E2E_TARGET_EMAIL`, `E2E_TARGET_ID` | Empfänger-Testkonto |
| `E2E_ROOM_ID` | Bestehender gemeinsamer privater Raum |
| `SUPABASE_URL`, `SUPABASE_SECRET_KEY` | Supabase-Testprojekt und Backend-Key |
| `E2E_RABBIT_PASSWORD` | Passwort des lokalen RabbitMQ-Testbrokers |
| `E2E_RABBIT_USER` | Optional, Standard `admin` |
| `E2E_PROJECT` | Optional, Standard `eva-storage-e2e` |
| `E2E_FRONTEND_URL`, `E2E_GATEWAY_URL`, `E2E_RABBIT_URL` | Optional, lokale Basis-URLs |
| `E2E_BROWSER_CHANNEL` | Optional `msedge` oder `chrome`; sonst installierter Playwright-Chromium |
| `E2E_HEADED=1` | Optional sichtbarer Browser |
| `E2E_RESULTS_DIR` | Optional Ausgabeordner für nicht geheime Nachweise |

```powershell
dotnet test .\Storage-Service.Tests\Storage-Service.Tests.csproj -c Release --filter "Category=EndToEnd"
```

Ohne `E2E_RUN=1` werden beim normalen `dotnet test` sechs Unit-Tests ausgeführt;
der eine Anwendungstest wird ausdrücklich als **übersprungen**, nicht bestanden, angezeigt.
Mit `E2E_RUN=1` führen fehlende Konfiguration oder nicht erreichbare Dienste zu einem fehlgeschlagenen Test.

## Was geprüft wird

1. Anmeldung durch die echten Formularfelder und bestätigte WebSocket-Verbindung zum Gateway.
2. Passender vorhandener privater Raum für Sender und Empfänger.
3. Storage pausieren, drei eindeutig markierte Nachrichten über das Frontend senden.
4. Drei Gateway-Bestätigungen und drei Events ausschließlich in `storage_queue`.
   Noch keine passenden Supabase-Einträge und keine neuen Delivery-Nachrichten.
5. Storage starten, drei echte Speicherungen und genau drei zusätzliche Delivery-Events abwarten.
6. IDs, Sender, Empfänger, Raum, Inhalt, Zeitstempel und kompletten fachlichen Event-Inhalt vergleichen.
   Insbesondere: Supabase `receiver_id` muss dem `TargetId` des Events entsprechen, nicht der Raum-ID.
7. Logs nachweisen: drei überlappend arbeitende Worker; pro Nachricht Speicherung vor Weiterleitung.
8. Leere Storage-Queue, keine Fehler-/Skipped-Nachrichten und kein Delivery-Consumer.

Jeder Lauf bekommt eine neue Kennung. Bei bereits drei Delivery-Nachrichten sind nach einem
weiteren erfolgreichen Lauf sechs korrekt: **drei alte plus drei neue**, keine behaupteten Duplikate.
Der Test vergleicht die Kennungen und Events, nicht nur einen globalen Zähler.

## Sicherheit, Ergebnisse und Grenzen

- Keine Nachrichten werden gelöscht oder Queues geleert. Queue-Inhalte werden ausgelesen und
  mit `ack_requeue_true` wieder eingereiht; Reihenfolge/Redelivery-Status können sich dabei ändern.
- Drei Testnachrichten bleiben in Supabase und im lokalen Delivery-Broker zur Kontrolle erhalten.
- Storage wird nach einer Testpause auch bei einem Fehler wieder gestartet (`finally`). Bei einem
  hart beendeten Testprozess ggf. selbst `docker compose ... start storage` ausführen.
- Die eigenen Browser-Sitzungen sind isoliert; die offene Sitzung in VS Code/Codex wird nicht benutzt.
- Keine Browser-Traces, Login-Tokens oder Passwörter werden gespeichert. Kein `DEBUG=pw:api`
  mit echten Zugangsdaten verwenden. Im ignorierten `TestResults`-Ordner stehen nur Testevents und Logs.
- Die Nachweisdateien entstehen für diesen Lauf neu; alte manuelle Nachweise werden nicht als Test benutzt.
- Nicht geprüft: History-Laden mit beiden Benutzer-Sitzungen, Zustellung/Anzeige beim Empfänger, Netzwerk-/Datenbankausfälle, Retry-/Crash-Sicherheit,
  alle RLS-Regeln oder genau einmalige Zustellung unter Wiederholungen.

Quellen: [Playwright .NET](https://playwright.dev/dotnet/docs/intro),
[RabbitMQ HTTP API](https://www.rabbitmq.com/docs/http-api-reference).
