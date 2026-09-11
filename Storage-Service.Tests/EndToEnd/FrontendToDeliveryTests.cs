using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Storage_Service.Tests.EndToEnd.Support;
using Xunit;
using Xunit.Abstractions;

namespace Storage_Service.Tests.EndToEnd;

// Ein echter Test, keine Mocks: Frontend -> Gateway -> Rabbit -> Storage -> Supabase -> delivery_queue.
public sealed class FrontendToDeliveryTests(ITestOutputHelper output)
{
    [EndToEndFact]
    [Trait("Category", "EndToEnd")]
    public async Task Drei_Frontendnachrichten_Werden_Gespeichert_Und_Danach_Weitergeleitet()
    {
        var config = new E2eSettings();
        config.Validate();
        var started = DateTimeOffset.UtcNow;
        var prefix = $"E2E-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-";
        var evidence = Path.Combine(Environment.GetEnvironmentVariable("E2E_RESULTS_DIR") ??
            Path.Combine(AppContext.BaseDirectory, "TestResults"), prefix.TrimEnd('-'));
        Directory.CreateDirectory(evidence);
        output.WriteLine($"Testlauf: {prefix} | Nachweise: {evidence}");

        using var system = new E2eSystem(config);
        await system.PreflightAsync();
        var baseline = await system.QueuesAsync();
        Assert.Equal(new QueueState("storage_queue", 0, 0, 1), E2eSystem.Queue(baseline, "storage_queue"));
        var oldDelivery = E2eSystem.Queue(baseline, "delivery_queue");
        Assert.Equal(0, oldDelivery.Consumers);
        Assert.Equal(0, oldDelivery.Unacked);
        Assert.InRange(oldDelivery.Ready, 0, 97);
        Assert.All(baseline.Where(q => q.Name.EndsWith("_error") || q.Name.EndsWith("_skipped")), q => Assert.Equal(0, q.Ready + q.Unacked));

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = !config.Headed,
            Channel = string.IsNullOrEmpty(config.BrowserChannel) ? null : config.BrowserChannel
        });
        await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(30000);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentFrames = new ConcurrentQueue<(Guid TargetId, string Text)>();
        var acknowledgments = 0;
        page.WebSocket += (_, socket) =>
        {
            var uri = new Uri(socket.Url); // Die URL enthält ein Token: niemals loggen oder als Nachweis speichern.
            if (uri.Host != config.Gateway.Host || uri.Port != config.Gateway.Port || uri.AbsolutePath != "/ws") return;
            socket.FrameSent += (_, frame) =>
            {
                if (frame.Text is not { } text) return;
                using var json = JsonDocument.Parse(text);
                var root = json.RootElement;
                if (root.TryGetProperty("type", out var type) && type.GetString() == "presence.heartbeat") connected.TrySetResult(true);
                if (root.TryGetProperty("text", out var message) && message.GetString() is { } value && value.StartsWith(prefix, StringComparison.Ordinal))
                    sentFrames.Enqueue((root.GetProperty("targetId").GetGuid(), value));
            };
            socket.FrameReceived += (_, frame) =>
            {
                if (frame.Text is not { } text) return;
                using var json = JsonDocument.Parse(text);
                if (json.RootElement.TryGetProperty("status", out var status) && status.GetString() == "published")
                    Interlocked.Increment(ref acknowledgments);
            };
        };

        output.WriteLine("[1/6] Im echten Frontend anmelden und Teilnehmer prüfen.");
        await page.GotoAsync(new Uri(config.Frontend, "sign-in").AbsoluteUri);
        await page.GetByPlaceholder("email@beispiel.de", new() { Exact = true }).FillAsync(config.Email);
        try { await page.GetByPlaceholder("••••••••", new() { Exact = true }).FillAsync(config.Password); }
        catch { throw new InvalidOperationException("Passwortfeld konnte nicht ausgefüllt werden; Eingabe wird nicht protokolliert."); }
        var login = await page.RunAndWaitForResponseAsync(
            () => page.GetByText("Anmelden", new() { Exact = true }).ClickAsync(),
            r => r.Url == new Uri(config.Gateway, "api/auth/login").AbsoluteUri && r.Request.Method == "POST");
        Assert.True(login.Ok, $"Frontend-Login fehlgeschlagen: HTTP {login.Status}. Testzugang prüfen; kein Passwort wird ausgegeben.");
        using var session = JsonDocument.Parse(await login.TextAsync());
        var senderId = session.RootElement.GetProperty("user").GetProperty("userId").GetGuid();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var roomBefore = await system.FindPrivateRoomAsync(senderId);
        output.WriteLine(roomBefore.HasValue
            ? $"Vorhandener privater Raum wird wiederverwendet: {roomBefore.Value:D}"
            : "Noch kein privater Raum vorhanden; Storage muss ihn beim Speichern erstellen.");
        await page.GetByLabel($"Chat mit {config.TargetEmail}", new() { Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Textbox, new() { Name = "Nachricht", Exact = true })).ToBeVisibleAsync();

        try
        {
            output.WriteLine("[2/6] Storage pausieren; drei Nachrichten per Frontend senden.");
            await system.StopStorageAsync();
            var texts = Enumerable.Range(1, 3).Select(i => $"{prefix}{i:D2} - Testnachricht").ToArray();
            foreach (var text in texts)
            {
                await page.GetByRole(AriaRole.Textbox, new() { Name = "Nachricht", Exact = true }).FillAsync(text);
                await page.GetByLabel("Nachricht senden", new() { Exact = true }).ClickAsync();
                await Assertions.Expect(page.GetByText(text, new() { Exact = true })).ToBeVisibleAsync();
            }
            await E2eSystem.WaitAsync(() => Task.FromResult(sentFrames.Count == 3 && Volatile.Read(ref acknowledgments) == 3), "Drei Gateway-Bestätigungen fehlen.");
            Assert.All(sentFrames, frame => Assert.Equal(config.TargetId, frame.TargetId));
            Assert.Equal(texts, sentFrames.Select(frame => frame.Text));
            await E2eSystem.WaitAsync(async () => E2eSystem.Queue(await system.QueuesAsync(), "storage_queue").Ready == 3, "Die drei Frontend-Nachrichten fehlen in storage_queue.");

            output.WriteLine("[3/6] Vor Verarbeitung: nur storage_queue, weder Supabase-Eintrag noch neue Delivery-Nachricht.");
            var paused = await system.QueuesAsync();
            Assert.Equal(new QueueState("storage_queue", 3, 0, 0), E2eSystem.Queue(paused, "storage_queue"));
            Assert.Equal(oldDelivery, E2eSystem.Queue(paused, "delivery_queue"));
            Assert.Empty(await system.RowsAsync(prefix));
            var incoming = await system.InspectQueueAsync("storage_queue", 3);
            Assert.Equal(3, incoming.Length);
            Assert.Equal(3, incoming.Select(e => e.Message.MessageId).Distinct().Count());
            Assert.All(incoming, e =>
            {
                Assert.Equal("Chat.Contracts.Events:ChatMessageEvent", e.Exchange);
                Assert.Equal("chat.message.published", e.RoutingKey);
                Assert.Equal(senderId, e.Message.SenderId);
                Assert.Equal(config.TargetId, e.Message.TargetId);
                Assert.Contains(e.Message.Ciphertext, texts);
            });
            await WriteEvidenceAsync(evidence, "before.json", new { prefix, paused, incoming, databaseRows = 0, oldDelivery });

            output.WriteLine("[4/6] Storage starten; auf Speicherung und Weiterleitung warten.");
            await system.StartStorageAsync();
            await E2eSystem.WaitAsync(async () =>
            {
                var queues = await system.QueuesAsync();
                return E2eSystem.Queue(queues, "storage_queue") is { Ready: 0, Unacked: 0, Consumers: 1 }
                    && E2eSystem.Queue(queues, "delivery_queue").Ready == oldDelivery.Ready + 3
                    && (await system.RowsAsync(prefix)).Length == 3;
            }, "Storage hat nicht genau drei neue Nachrichten gespeichert und weitergeleitet. Logs/Fehlerqueue prüfen.");

            output.WriteLine("[5/6] IDs, Sender, Empfänger, privater Raum, Inhalt, Zeitstempel und unveränderte Delivery-Events vergleichen.");
            var rows = await system.RowsAsync(prefix);
            var finalQueues = await system.QueuesAsync();
            var delivery = (await system.InspectQueueAsync("delivery_queue", oldDelivery.Ready + 3))
                .Where(e => e.Message.Ciphertext.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal(3, delivery.Length);
            var roomId = Assert.Single(rows.Select(row => row.RoomId).Distinct());
            await system.CheckPrivateRoomMembersAsync(senderId, roomId);
            if (roomBefore.HasValue)
                Assert.Equal(roomBefore.Value, roomId);
            Assert.Equal(new QueueState("delivery_queue", oldDelivery.Ready + 3, 0, 0), E2eSystem.Queue(finalQueues, "delivery_queue"));
            Assert.All(finalQueues.Where(q => q.Name.EndsWith("_error") || q.Name.EndsWith("_skipped")), q => Assert.Equal(0, q.Ready + q.Unacked));
            var logs = await system.StorageLogsAsync(started);
            foreach (var original in incoming)
            {
                var message = original.Message;
                var row = Assert.Single(rows, r => r.Id == message.MessageId);
                var forwarded = Assert.Single(delivery, e => e.Message.MessageId == message.MessageId);
                Assert.Equal(roomId, row.RoomId);
                Assert.NotEqual(message.TargetId, row.RoomId);
                Assert.Equal(message.SenderId, row.SenderId);
                Assert.Equal(message.TargetId, row.ReceiverId);
                Assert.NotEqual(row.RoomId, row.ReceiverId);
                Assert.Equal(message.Ciphertext, row.Content);
                Assert.InRange(Math.Abs((row.CreatedAt - message.Timestamp).Ticks), 0L, 10L);
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original.Body.GetRawText()), JsonNode.Parse(forwarded.Body.GetRawText())), "Fachlicher Event-Inhalt wurde verändert.");
                Assert.Equal(original.MessageType, forwarded.MessageType);
                var saved = logs.IndexOf($"Nachricht {message.MessageId} wurde gespeichert.", StringComparison.Ordinal);
                var sent = logs.IndexOf($"Nachricht {message.MessageId} an delivery_queue gesendet.", StringComparison.Ordinal);
                Assert.True(saved >= 0 && sent > saved, $"Speicherung vor Weiterleitung für {message.MessageId} nicht nachgewiesen.");
            }
            var starts = Regex.Matches(logs, @"\[Worker (\d+)\] Speichert Nachricht ([0-9a-f-]+)\.")
                .Where(m => incoming.Any(e => e.Message.MessageId.ToString() == m.Groups[2].Value)).ToArray();
            Assert.Equal(3, starts.Length);
            Assert.Equal(3, starts.Select(m => m.Groups[1].Value).Distinct().Count());
            var firstSave = incoming.Min(e => logs.IndexOf($"Nachricht {e.Message.MessageId} wurde gespeichert.", StringComparison.Ordinal));
            Assert.All(starts, m => Assert.True(m.Index < firstSave, "Die drei Worker arbeiteten nicht überlappend."));
            await WriteEvidenceAsync(evidence, "after.json", new { prefix, rows, delivery, finalQueues, result = "passed" });
            var relevantLogs = logs.Split('\n').Where(line => incoming.Any(e => line.Contains(e.Message.MessageId.ToString(), StringComparison.Ordinal)));
            await File.WriteAllLinesAsync(Path.Combine(evidence, "storage.log"), relevantLogs);
            output.WriteLine("[6/6] BESTANDEN: 3 Frontend-Nachrichten -> 3 parallele Worker -> 3 Supabase-Einträge -> 3 unveränderte Delivery-Events.");
            output.WriteLine($"Delivery vorher: {oldDelivery.Ready}; danach: {oldDelivery.Ready + 3}. Testdaten bleiben zur Kontrolle erhalten.");
        }
        finally
        {
            if (system.MustRestartStorage)
            {
                output.WriteLine("Storage wird nach der Testpause wieder gestartet.");
                await system.StartStorageAsync();
            }
        }
    }

    private static Task WriteEvidenceAsync(string directory, string file, object data) =>
        File.WriteAllTextAsync(Path.Combine(directory, file), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
}
