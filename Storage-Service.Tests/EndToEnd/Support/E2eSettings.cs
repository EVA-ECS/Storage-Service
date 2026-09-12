namespace Storage_Service.Tests.EndToEnd.Support;

// Liest und prüft die Einstellungen. Geheimnisse stehen nur in Umgebungsvariablen.
internal sealed class E2eSettings
{
    public string Email { get; } = Required("E2E_EMAIL");
    public string Password { get; } = Required("E2E_PASSWORD");
    public string TargetEmail { get; } = Required("E2E_TARGET_EMAIL");
    public Guid TargetId { get; } = Guid.Parse(Required("E2E_TARGET_ID"));
    public Guid? RoomId { get; } = OptionalGuid("E2E_ROOM_ID");
    public string SecretKey { get; } = Required("SUPABASE_SECRET_KEY");
    public Uri Supabase { get; } = new(Required("SUPABASE_URL").TrimEnd('/') + "/");
    public Uri Frontend { get; } = LocalUrl("E2E_FRONTEND_URL", "http://localhost:8081/");
    public Uri Gateway { get; } = LocalUrl("E2E_GATEWAY_URL", "http://localhost:8082/");
    public Uri Rabbit { get; } = LocalUrl("E2E_RABBIT_URL", "http://localhost:15673/");
    public string RabbitUser { get; } = Environment.GetEnvironmentVariable("E2E_RABBIT_USER") ?? "admin";
    public string RabbitPassword { get; } = Required("E2E_RABBIT_PASSWORD");
    public string ComposeFile { get; } = Path.GetFullPath(Required("E2E_COMPOSE_FILE"));
    public string Project { get; } = Environment.GetEnvironmentVariable("E2E_PROJECT") ?? "eva-storage-e2e";
    public string BrowserChannel { get; } = Environment.GetEnvironmentVariable("E2E_BROWSER_CHANNEL") ?? "";
    public bool Headed { get; } = Environment.GetEnvironmentVariable("E2E_HEADED") == "1";

    public void Validate()
    {
        if (Environment.GetEnvironmentVariable("E2E_ALLOW_TEST_WRITES") != "1")
            throw new InvalidOperationException("E2E_ALLOW_TEST_WRITES=1 fehlt: Dieser Test sendet drei echte Testnachrichten.");
        if (!File.Exists(ComposeFile) || !Project.StartsWith("eva-storage-e2e", StringComparison.Ordinal))
            throw new InvalidOperationException("Ein eigener Compose-Teststack mit Projektname eva-storage-e2e... ist erforderlich.");
        if ((Supabase.Scheme != "https" && !Supabase.IsLoopback) || Supabase.AbsolutePath != "/")
            throw new InvalidOperationException("SUPABASE_URL muss die Projekt-Basis-URL sein, ohne /rest/v1 und mit HTTPS.");
        if (Email.Equals(TargetEmail, StringComparison.OrdinalIgnoreCase) || RoomId == TargetId)
            throw new InvalidOperationException("Sender, Empfänger und optionaler privater Raum müssen korrekt und getrennt konfiguriert sein.");
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value) ? value :
            throw new InvalidOperationException($"Umgebungsvariable {name} fehlt. Siehe EndToEnd/README.md.");
    }

    private static Guid? OptionalGuid(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);
    }

    private static Uri LocalUrl(string name, string fallback)
    {
        var uri = new Uri((Environment.GetEnvironmentVariable(name) ?? fallback).TrimEnd('/') + "/");
        if (!uri.IsLoopback || uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/")
            throw new InvalidOperationException($"{name}: Nur lokale Basis-URLs sind für diesen Test erlaubt.");
        return uri;
    }
}
