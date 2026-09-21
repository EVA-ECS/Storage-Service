using System.Net;
using System.Text.Json;
using Chat.Contracts.Events;
using Xunit;

namespace Storage_Service.Tests;

// Speicherlogik mit simulierten HTTP-Antworten, ohne echte Supabase-Verbindung.
[Trait("Category", "Unit")]
public sealed class SupabaseChatMessageStoreTests
{
    [Fact]
    public async Task StoreAsync_GetsOrCreatesRoomAndStoresMessage()
    {
        var senderId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var requests = new List<CapturedRequest>();

        using var client = CreateClient(async request =>
        {
            requests.Add(await CapturedRequest.CreateAsync(request));
            var pathAndQuery = request.RequestUri!.PathAndQuery;

            if (pathAndQuery.Contains("/rpc/get_or_create_private_room"))
            {
                return JsonResponse($"[{{\"room_id\":\"{roomId:D}\"}}]");
            }

            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var store = new SupabaseChatMessageStore(client);
        var timestamp = new DateTime(2026, 9, 3, 20, 15, 0, DateTimeKind.Utc);

        await store.StoreAsync(
            new ChatMessageEvent(
                messageId.ToString("D"),
                senderId.ToString("D"),
                targetId.ToString("D"),
                "encrypted-payload",
                timestamp
            ),
            CancellationToken.None
        );

        Assert.Equal(2, requests.Count);
        Assert.All(requests, item => Assert.Equal(HttpMethod.Post, item.Method));
        var roomRequest = Assert.Single(
            requests,
            item => item.Uri.AbsolutePath.EndsWith("/rpc/get_or_create_private_room")
        );
        using var roomDocument = JsonDocument.Parse(roomRequest.Body!);
        Assert.Equal(
            senderId,
            roomDocument.RootElement.GetProperty("p_sender_id").GetGuid()
        );
        Assert.Equal(
            targetId,
            roomDocument.RootElement.GetProperty("p_target_id").GetGuid()
        );

        var insert = Assert.Single(
            requests,
            item => item.Uri.AbsolutePath.EndsWith("/messages")
        );
        Assert.Contains("messages?on_conflict=id", insert.Uri.PathAndQuery);
        Assert.Contains("resolution=ignore-duplicates", insert.Prefer);

        using var document = JsonDocument.Parse(insert.Body!);
        var root = document.RootElement;
        Assert.Equal(messageId, root.GetProperty("id").GetGuid());
        Assert.Equal(roomId, root.GetProperty("room_id").GetGuid());
        Assert.Equal(senderId, root.GetProperty("sender_id").GetGuid());
        Assert.Equal(targetId, root.GetProperty("receiver_id").GetGuid());
        Assert.NotEqual(roomId, root.GetProperty("receiver_id").GetGuid());
        Assert.Equal("encrypted-payload", root.GetProperty("content").GetString());
    }

    [Fact]
    public async Task StoreAsync_DoesNotStoreWhenRoomProvisioningFails()
    {
        var requests = new List<CapturedRequest>();
        using var client = CreateClient(async request =>
        {
            requests.Add(await CapturedRequest.CreateAsync(request));
            return new HttpResponseMessage(HttpStatusCode.Conflict);
        });
        var store = new SupabaseChatMessageStore(client);
        var message = CreateMessage();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            store.StoreAsync(message, CancellationToken.None)
        );

        Assert.Single(requests);
        Assert.EndsWith(
            "/rpc/get_or_create_private_room",
            requests[0].Uri.AbsolutePath
        );
    }

    [Fact]
    public async Task StoreAsync_PropagatesSupabaseErrors()
    {
        var roomId = Guid.NewGuid();
        var requestNumber = 0;
        using var client = CreateClient(_ => Task.FromResult(
            ++requestNumber == 1
                ? JsonResponse($"[{{\"room_id\":\"{roomId:D}\"}}]")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        ));
        var store = new SupabaseChatMessageStore(client);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            store.StoreAsync(CreateMessage(), CancellationToken.None)
        );

        Assert.Equal(2, requestNumber);
    }

    private static ChatMessageEvent CreateMessage()
    {
        return new ChatMessageEvent(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            "payload",
            DateTime.UtcNow
        );
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder
    )
    {
        return new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://example.test/rest/v1/")
        };
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return responder(request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string Prefer,
        string? Body
    )
    {
        public static async Task<CapturedRequest> CreateAsync(
            HttpRequestMessage request
        )
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync();
            var prefer = request.Headers.TryGetValues(
                "Prefer",
                out var values
            )
                ? string.Join(',', values)
                : string.Empty;

            return new CapturedRequest(
                request.Method,
                request.RequestUri!,
                prefer,
                body
            );
        }
    }
}
