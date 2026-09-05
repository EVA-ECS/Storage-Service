using System.Net;
using System.Text.Json;
using Chat.Contracts.Events;
using Xunit;

namespace Storage_Service.Tests;

// Prüft die gespeicherten Beteiligten, nicht das Laden der History oder Supabase-RLS.
[Trait("Category", "Unit")]
public sealed class ReceiverMappingTests
{
    [Fact]
    public async Task StoreAsync_StoresCorrectReceiverInBothDirections()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        using var handler = new RecordingHandler(roomId);
        using var client = CreateClient(handler);
        var store = new SupabaseChatMessageStore(client);
        var aToB = Message(userA, userB.ToString("D"));
        var bToA = Message(userB, userA.ToString("D"));

        await store.StoreAsync(aToB, CancellationToken.None);
        await store.StoreAsync(bToA, CancellationToken.None);

        Assert.Equal(2, handler.Inserts.Count);
        AssertMessage(handler.Inserts[0], aToB, roomId, userA, userB);
        AssertMessage(handler.Inserts[1], bToA, roomId, userB, userA);
    }

    [Fact]
    public async Task StoreAsync_RejectsInvalidReceiverBeforeAnyHttpRequest()
    {
        using var handler = new RecordingHandler(Guid.NewGuid());
        using var client = CreateClient(handler);
        var store = new SupabaseChatMessageStore(client);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.StoreAsync(Message(Guid.NewGuid(), "keine-gueltige-uuid"), CancellationToken.None));

        Assert.Contains("TargetId", error.Message);
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(handler.Inserts);
    }

    private static void AssertMessage(JsonElement row, ChatMessageEvent message, Guid roomId, Guid sender, Guid receiver)
    {
        Assert.Equal(Guid.Parse(message.MessageId), row.GetProperty("id").GetGuid());
        Assert.Equal(roomId, row.GetProperty("room_id").GetGuid());
        Assert.Equal(sender, row.GetProperty("sender_id").GetGuid());
        Assert.Equal(receiver, row.GetProperty("receiver_id").GetGuid());
        Assert.NotEqual(roomId, row.GetProperty("receiver_id").GetGuid());
        Assert.Equal(message.Ciphertext, row.GetProperty("content").GetString());
    }

    private static ChatMessageEvent Message(Guid sender, string receiver) => new(
        Guid.NewGuid().ToString("D"), sender.ToString("D"), receiver, "Testnachricht", DateTime.UtcNow);

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://example.test/rest/v1/") };

    private sealed class RecordingHandler(Guid roomId) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<JsonElement> Inserts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Inserts.Add(body.RootElement.Clone());
                return new(HttpStatusCode.Created);
            }

            var property = request.RequestUri!.AbsolutePath.EndsWith("/room_members") ? "room_id" : "id";
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent($"[{{\"{property}\":\"{roomId:D}\"}}]", System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
