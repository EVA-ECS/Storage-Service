using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Storage_Service.Tests.EndToEnd.Support;

// Diese Hilfen steuern nur die Testumgebung und lesen Nachweise.
// Nachrichten werden ausschließlich durch das Frontend erzeugt, nie durch diese HTTP-Clients.
internal sealed class E2eSystem(E2eSettings settings) : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? _storageId;
    public bool MustRestartStorage { get; private set; }

    public async Task PreflightAsync()
    {
        _storageId = (await ComposeAsync("ps", "-q", "storage")).Trim();
        Assert.Matches("^[a-f0-9]{12,64}$", _storageId);
        using var labels = JsonDocument.Parse(await DockerAsync("inspect", _storageId, "--format", "{{json .Config.Labels}}"));
        Assert.Equal(settings.Project, labels.RootElement.GetProperty("com.docker.compose.project").GetString());
        Assert.Equal("storage", labels.RootElement.GetProperty("com.docker.compose.service").GetString());
        Assert.Equal("true", (await DockerAsync("inspect", _storageId, "--format", "{{.State.Running}}")).Trim());
        await CheckPublishedPortAsync("rabbitmq", "15672/tcp", settings.Rabbit.Port);
        await CheckPublishedPortAsync("gateway", "8080/tcp", settings.Gateway.Port);
        // Prüfen nur die URL, niemals die Container-Schlüssel ausgeben.
        var actualUrl = await DockerAsync("inspect", _storageId, "--format",
            "{{range .Config.Env}}{{if eq (index (split . \"=\") 0) \"Supabase__Url\"}}{{println .}}{{end}}{{end}}");
        Assert.Equal("Supabase__Url=" + settings.Supabase.AbsoluteUri.TrimEnd('/'), actualUrl.Trim().TrimEnd('/'));
        using var health = await _http.GetAsync(new Uri(settings.Gateway, "health"));
        Assert.True(health.IsSuccessStatusCode, "Gateway ist nicht erreichbar.");
        using var frontend = await _http.GetAsync(settings.Frontend);
        Assert.True(frontend.IsSuccessStatusCode, "Frontend ist nicht erreichbar.");
        // Vor Browser-Login, Storage-Pause und echten Testnachrichten die DB-Migration prüfen.
        try { await SupabaseAsync("rest/v1/messages?select=receiver_id&limit=0"); }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                "Supabase.receiver_id ist noch nicht abfragbar. Zuerst Database/Migrations/20260905_add_receiver_id.sql anwenden bzw. Zugang/Schema-Cache prüfen.", error);
        }
        var bindings = await RabbitAsync("api/bindings/%2F");
        var routes = bindings.EnumerateArray().Where(x => x.GetProperty("source").GetString() == "Chat.Contracts.Events:ChatMessageEvent").ToArray();
        var route = Assert.Single(routes);
        Assert.Equal("storage_queue", route.GetProperty("destination").GetString());
        Assert.Equal("chat.message.published", route.GetProperty("routing_key").GetString());
    }

    private async Task CheckPublishedPortAsync(string service, string containerPort, int expectedPort)
    {
        var id = (await ComposeAsync("ps", "-q", service)).Trim();
        Assert.Matches("^[a-f0-9]{12,64}$", id);
        using var labels = JsonDocument.Parse(await DockerAsync("inspect", id, "--format", "{{json .Config.Labels}}"));
        Assert.Equal(settings.Project, labels.RootElement.GetProperty("com.docker.compose.project").GetString());
        using var ports = JsonDocument.Parse(await DockerAsync("inspect", id, "--format", "{{json .NetworkSettings.Ports}}"));
        Assert.Contains(ports.RootElement.GetProperty(containerPort).EnumerateArray(),
            p => p.GetProperty("HostPort").GetString() == expectedPort.ToString());
    }

    public async Task<Guid?> FindPrivateRoomAsync(Guid senderId)
    {
        Assert.NotEqual(senderId, settings.TargetId);
        var target = await SupabaseAsync($"auth/v1/admin/users/{settings.TargetId:D}");
        Assert.Equal(settings.TargetEmail.ToLowerInvariant(), target.GetProperty("email").GetString()!.ToLowerInvariant());
        var mine = await SupabaseAsync($"rest/v1/room_members?select=room_id&user_id=eq.{senderId:D}");
        var theirs = await SupabaseAsync($"rest/v1/room_members?select=room_id&user_id=eq.{settings.TargetId:D}");
        var common = mine.EnumerateArray().Select(x => x.GetProperty("room_id").GetGuid())
            .Intersect(theirs.EnumerateArray().Select(x => x.GetProperty("room_id").GetGuid())).ToArray();
        if (common.Length == 0)
        {
            Assert.Null(settings.RoomId);
            return null;
        }

        var privateRooms = await SupabaseAsync("rest/v1/rooms?select=id&is_group=eq.false&id=in.(" + string.Join(',', common) + ")");
        var exactPrivateRooms = new List<Guid>();
        foreach (var room in privateRooms.EnumerateArray())
        {
            var roomId = room.GetProperty("id").GetGuid();
            var members = await SupabaseAsync($"rest/v1/room_members?select=user_id&room_id=eq.{roomId:D}");
            var ids = members.EnumerateArray().Select(x => x.GetProperty("user_id").GetGuid()).Order().ToArray();
            if (ids.SequenceEqual(new[] { senderId, settings.TargetId }.Order()))
                exactPrivateRooms.Add(roomId);
        }

        if (exactPrivateRooms.Count == 0)
        {
            Assert.Null(settings.RoomId);
            return null;
        }

        var result = Assert.Single(exactPrivateRooms);
        if (settings.RoomId.HasValue)
            Assert.Equal(settings.RoomId.Value, result);
        return result;
    }

    public async Task CheckPrivateRoomMembersAsync(Guid senderId, Guid roomId)
    {
        var rooms = await SupabaseAsync($"rest/v1/rooms?select=id,is_group&id=eq.{roomId:D}");
        Assert.False(Assert.Single(rooms.EnumerateArray()).GetProperty("is_group").GetBoolean());
        var members = await SupabaseAsync($"rest/v1/room_members?select=user_id&room_id=eq.{roomId:D}");
        var ids = members.EnumerateArray().Select(x => x.GetProperty("user_id").GetGuid()).ToArray();
        Assert.Equal(2, ids.Length);
        Assert.Contains(senderId, ids);
        Assert.Contains(settings.TargetId, ids);
    }

    public async Task<QueueState[]> QueuesAsync()
    {
        var value = await RabbitAsync("api/queues/%2F");
        return value.EnumerateArray().Select(q => new QueueState(
            q.GetProperty("name").GetString()!, Number(q, "messages_ready"),
            Number(q, "messages_unacknowledged"), Number(q, "consumers"))).ToArray();
    }

    public static QueueState Queue(QueueState[] queues, string name) =>
        queues.SingleOrDefault(q => q.Name == name) ?? new(name, 0, 0, 0);

    public async Task<StoredRow[]> RowsAsync(string prefix)
    {
        var value = await SupabaseAsync("rest/v1/messages?select=id,room_id,sender_id,receiver_id,content,created_at&content=like." +
            Uri.EscapeDataString(prefix + "*") + "&order=created_at");
        return value.EnumerateArray().Select(x => new StoredRow(
            x.GetProperty("id").GetGuid(), x.GetProperty("room_id").GetGuid(), x.GetProperty("sender_id").GetGuid(),
            x.GetProperty("receiver_id").GetGuid(),
            x.GetProperty("content").GetString()!, x.GetProperty("created_at").GetDateTimeOffset())).ToArray();
    }

    // Entwicklungsbroker: auslesen UND wieder einreihen. Kein Purge, kein Löschen/Bestätigen zum Entfernen.
    public async Task<ObservedEvent[]> InspectQueueAsync(string name, int count)
    {
        Assert.InRange(count, 1, 100);
        Assert.Contains(name, new[] { "storage_queue", "delivery_queue" });
        var response = await RabbitAsync($"api/queues/%2F/{name}/get", new { count, ackmode = "ack_requeue_true", encoding = "auto" });
        return response.EnumerateArray().Select(item =>
        {
            using var envelope = JsonDocument.Parse(item.GetProperty("payload").GetString()!);
            var body = envelope.RootElement.GetProperty("message");
            return new ObservedEvent(
                item.GetProperty("exchange").GetString()!, item.GetProperty("routing_key").GetString()!,
                body.Deserialize<MessageEvent>(JsonOptions)!, body.Clone(),
                envelope.RootElement.GetProperty("messageType").EnumerateArray().Select(t => t.GetString()!).ToArray());
        }).ToArray();
    }

    public async Task StopStorageAsync()
    {
        MustRestartStorage = true; // Auch nach einem abgebrochenen Stop-Versuch wiederherstellen.
        await ComposeAsync("stop", "storage");
        await WaitAsync(async () => Queue(await QueuesAsync(), "storage_queue").Consumers == 0, "Storage wurde nicht gestoppt.");
    }

    public async Task StartStorageAsync()
    {
        await ComposeAsync("start", "storage");
        await WaitAsync(async () => Queue(await QueuesAsync(), "storage_queue").Consumers == 1, "Storage hat sich nicht verbunden.");
        MustRestartStorage = false;
    }

    public Task<string> StorageLogsAsync(DateTimeOffset since) => DockerAsync("logs", "--timestamps", "--since", since.ToString("O"), _storageId!);

    public static async Task WaitAsync(Func<Task<bool>> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (await condition()) return;
            await Task.Delay(500);
        }
        throw new InvalidOperationException(failure);
    }

    private Task<JsonElement> RabbitAsync(string path, object? body = null) => RequestAsync(
        new Uri(settings.Rabbit, path), new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.RabbitUser + ":" + settings.RabbitPassword))), null, body);

    private Task<JsonElement> SupabaseAsync(string path) => RequestAsync(new Uri(settings.Supabase, path), null, settings.SecretKey, null);

    private async Task<JsonElement> RequestAsync(Uri uri, AuthenticationHeaderValue? auth, string? key, object? body)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, uri);
        request.Headers.Authorization = auth;
        request.Headers.UserAgent.ParseAdd("EVA-Storage-E2E/1.0");
        if (key is not null) request.Headers.Add("apikey", key);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Testabfrage {uri.AbsolutePath}: HTTP {(int)response.StatusCode}. Zugang und Testumgebung prüfen.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    private Task<string> ComposeAsync(params string[] args) =>
        DockerAsync(new[] { "compose", "-f", settings.ComposeFile, "-p", settings.Project }.Concat(args).ToArray());

    private static async Task<string> DockerAsync(params string[] args)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Docker konnte nicht gestartet werden.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Docker-Testbefehl dauerte länger als 60 Sekunden.");
        }
        var result = await stdout;
        var errors = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Docker-Testbefehl fehlgeschlagen. Compose-Pfad, Projektname und Docker Desktop prüfen.");
        return args[0] == "logs" ? result + errors : result;
    }

    public void Dispose() => _http.Dispose();
}
