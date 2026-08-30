# Storage Service

Der Service macht drei Dinge:

1. Nachricht aus `storage_queue` lesen.
2. In Supabase speichern.
3. An `delivery_queue` senden.

## Konfiguration

Passwörter kommen über Umgebungsvariablen und nicht in GitHub.

```text
ConnectionStrings__Supabase=<Supabase PostgreSQL connection string>
RabbitMQ__Host=rabbitmq
RabbitMQ__Username=admin
RabbitMQ__Password=<RabbitMQ password>
```

Aktuell gilt: `TargetId` ist der Raum und `Ciphertext` ist der Text.

## Starten

```powershell
dotnet restore
dotnet run
```
