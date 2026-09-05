# Storage Service

Der Service macht vier Dinge:

1. Nachricht aus `storage_queue` lesen.
2. Über die Supabase Data API den privaten Raum von Sender und Empfänger finden.
3. Mit einem freien Worker die Nachricht idempotent in Supabase speichern.
4. Erst danach an `delivery_queue` senden.

Mehrere Worker können Nachrichten gleichzeitig speichern. Freie Worker werden
in einem `ConcurrentBag` verwaltet.

## Konfiguration

Passwörter kommen über Umgebungsvariablen und nicht in GitHub.

```text
Supabase__Url=<Supabase project URL>
Supabase__SecretKey=<backend-only Supabase secret key>
RabbitMQ__Host=rabbitmq
RabbitMQ__Username=admin
RabbitMQ__Password=<RabbitMQ password>
Storage__WorkerCount=3
```

`TargetId` ist die Benutzer-ID des Empfängers. Der Storage Service ermittelt
über `room_members` den gemeinsamen privaten Raum (`rooms.is_group = false`).
`Ciphertext` wird im vorhandenen Feld `messages.content` gespeichert.
Zusätzlich wird `TargetId` in `messages.receiver_id` gespeichert; es ersetzt nicht `room_id`.
Vor dem Einsatz dieses Codes die [Datenbank-Migration und Hinweise zu Altbeständen](Database/README.md)
prüfen und anwenden. Das Delivery-Event bleibt unverändert.

Existiert für Sender und Empfänger kein privater Raum, wird die Verarbeitung
abgebrochen. Der Storage Service erstellt selbst keine Räume und sendet die
Nachricht in diesem Fall nicht an `delivery_queue`.

Der Secret Key darf nur im Backend verwendet und niemals in Frontend-Code,
Logs oder Git eingecheckt werden.

## Starten

```powershell
dotnet restore
dotnet run
```

## Tests

**Einstieg:** [Testübersicht mit Ordnerstruktur, Dateilinks und Startbefehlen](Storage-Service.Tests/README.md).
Die Unit-Tests liegen unter `Storage-Service.Tests/Unit/`, der vollständige Anwendungstest
unter `Storage-Service.Tests/EndToEnd/`. Technische E2E-Hilfen sind im Unterordner `Support/` getrennt.

Die sechs Unit-Tests starten (der zusätzliche Anwendungstest bleibt ohne Freigabe übersprungen):

```powershell
dotnet test .\Storage-Service.Tests\Storage-Service.Tests.csproj
```

Die sechs Unit-Tests prüfen:

1. Nachricht im gemeinsamen privaten Raum speichern.
2. Ohne privaten Raum keine Nachricht speichern.
3. Supabase-Fehler an den Worker weitergeben.
4. Drei Nachrichten mit drei Workern parallel verarbeiten.
5. Sender und Empfänger für A → B sowie B → A korrekt speichern.
6. Ungültige Empfänger-ID vor jeder HTTP-Abfrage ablehnen.

Bei Erfolg zeigt das Terminal `6` bestandene Tests, `0` Fehler und `1` übersprungenen Anwendungstest. Die Unit-Tests
verwenden simulierte Supabase-Antworten. Echtes RabbitMQ, Supabase und
`delivery_queue` werden damit noch nicht geprüft.

### Vollständiger Anwendungstest

Zusätzlich gibt es einen gezielt aktivierbaren C#-Test mit xUnit und Playwright für:

`Frontend -> Gateway -> RabbitMQ -> Storage -> Supabase -> delivery_queue`

Er sendet drei Nachrichten über das echte Frontend, prüft die wartenden Events vor dem
Storage-Start und danach Speicherung, private Raumzuordnung, drei parallele Worker sowie
unveränderte Delivery-Events einschließlich des Vergleichs von `receiver_id` mit `TargetId`.
Die Anwendung und die migrierte Datenbank müssen dafür bereitstehen; echte Testzugänge sind nötig.

```powershell
.\Storage-Service.Tests\Run-E2E.ps1 -ComposeFile "C:\Pfad\zur\lokalen\compose.yaml" -UseLocalStackConfig
```

Der Helfer fragt das Passwort verdeckt ab und führt sechs Unit-Tests plus den Anwendungstest aus.
Testnachrichten bleiben zur Kontrolle erhalten; der Test löscht keine vorhandenen Daten.
Siehe [Einrichtung und geprüfte Anforderungen](Storage-Service.Tests/EndToEnd/README.md).
