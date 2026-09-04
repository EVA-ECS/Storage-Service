# Storage Service

Der Service macht drei Dinge:

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
