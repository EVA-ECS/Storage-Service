using System.Net;
using System.Text.Json;
using Chat.Contracts.Events;
using Xunit;

namespace Storage_Service.Tests;

public sealed class SupabaseChatMessageStoreTests
{
    [Fact]
    public async Task StoreAsync_StoresMessageInSharedPrivateRoom()
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

            if (pathAndQuery.Contains("/room_members") &&
                pathAndQuery.Contains(senderId.ToString("D")))
            {
                return JsonResponse($"[{{\"room_id\":\"{roomId:D}\"}}]");
            }

            if (pathAndQuery.Contains("/room_members") &&
                pathAndQuery.Contains(targetId.ToString("D")))
            {
                return JsonResponse($"[{{\"room_id\":\"{roomId:D}\"}}]");
            }

            if (pathAndQuery.Contains("/rooms"))
            {
                return JsonResponse($"[{{\"id\":\"{roomId:D}\"}}]");
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

        Assert.Equal(4, requests.Count);
        var insert = Assert.Single(requests, item => item.Method == HttpMethod.Post);
        Assert.Contains("messages?on_conflict=id", insert.Uri.PathAndQuery);
        Assert.Contains("resolution=ignore-duplicates", insert.Prefer);

        using var document = JsonDocument.Parse(insert.Body!);
        var root = document.RootElement;
        Assert.Equal(messageId, root.GetProperty("id").GetGuid());
        Assert.Equal(roomId, root.GetProperty("room_id").GetGuid());
        Assert.Equal(senderId, root.GetProperty("sender_id").GetGuid());
        Assert.Equal("encrypted-payload", root.GetProperty("content").GetString());
    }

    [Fact]
    public async Task StoreAsync_CreatesPrivateRoomWhenNoneExists()
    {
        var requests = new List<CapturedRequest>();
        using var client = CreateClient(async request =>
        {
            requests.Add(await CapturedRequest.CreateAsync(request));
            return request.Method == HttpMethod.Get
                ? JsonResponse("[]")
                : new HttpResponseMessage(HttpStatusCode.Created);
        });
        var store = new SupabaseChatMessageStore(client);
        var message = CreateMessage();

        await store.StoreAsync(message, CancellationToken.None);

        Assert.Equal(4, requests.Count);
        var roomInsert = Assert.Single(
            requests,
            item => item.Uri.AbsolutePath.EndsWith("/rooms")
        );
        var membershipInsert = Assert.Single(
            requests,
            item => item.Uri.AbsolutePath.EndsWith("/room_members") &&
                    item.Method == HttpMethod.Post
        );
        var messageInsert = Assert.Single(
            requests,
            item => item.Uri.AbsolutePath.EndsWith("/messages")
        );

        using var roomDocument = JsonDocument.Parse(roomInsert.Body!);
        var roomId = roomDocument.RootElement.GetProperty("id").GetGuid();
        Assert.False(roomDocument.RootElement.GetProperty("is_group").GetBoolean());

        using var membershipDocument = JsonDocument.Parse(membershipInsert.Body!);
        var memberships = membershipDocument.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, memberships.Length);
        Assert.All(
            memberships,
            membership => Assert.Equal(
                roomId,
                membership.GetProperty("room_id").GetGuid()
            )
        );

        using var messageDocument = JsonDocument.Parse(messageInsert.Body!);
        Assert.Equal(
            roomId,
            messageDocument.RootElement.GetProperty("room_id").GetGuid()
        );
    }

    [Fact]
    public async Task StoreAsync_PropagatesSupabaseErrors()
    {
        using var client = CreateClient(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        ));
        var store = new SupabaseChatMessageStore(client);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            store.StoreAsync(CreateMessage(), CancellationToken.None)
        );
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
