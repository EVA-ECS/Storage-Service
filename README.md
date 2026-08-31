# Storage Service

Der Service macht drei Dinge:

1. Nachricht aus `storage_queue` lesen.
2. Mit einem freien Worker in Supabase speichern.
3. An `delivery_queue` senden.

Mehrere Worker können Nachrichten gleichzeitig speichern. Freie Worker werden
in einem `ConcurrentBag` verwaltet.

## Konfiguration

Passwörter kommen über Umgebungsvariablen und nicht in GitHub.

```text
ConnectionStrings__Supabase=<Supabase PostgreSQL connection string>
RabbitMQ__Host=rabbitmq
RabbitMQ__Username=admin
RabbitMQ__Password=<RabbitMQ password>
Storage__WorkerCount=3
```

Aktuell gilt: `TargetId` ist der Raum und `Ciphertext` ist der Text.

## Starten

```powershell
dotnet restore
dotnet run
```
