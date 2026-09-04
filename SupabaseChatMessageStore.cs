using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Chat.Contracts.Events;

namespace Storage_Service;

public sealed class SupabaseChatMessageStore : IChatMessageStore
{
    private readonly HttpClient _client;

    public SupabaseChatMessageStore(HttpClient client)
    {
        _client = client;
    }

    public async Task StoreAsync(
        ChatMessageEvent message,
        CancellationToken cancellationToken
    )
    {
        var messageId = ParseId(message.MessageId, nameof(message.MessageId));
        var senderId = ParseId(message.SenderId, nameof(message.SenderId));
        var targetId = ParseId(message.TargetId, nameof(message.TargetId));

        var roomId = await FindPrivateRoomAsync(
            senderId,
            targetId,
            cancellationToken
        );

        var storedMessage = new StoredMessage(
            messageId,
            roomId,
            senderId,
            message.Ciphertext,
            message.Timestamp
        );

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "messages?on_conflict=id"
        )
        {
            Content = JsonContent.Create(storedMessage)
        };
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            "resolution=ignore-duplicates,return=minimal"
        );

        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<Guid> FindPrivateRoomAsync(
        Guid senderId,
        Guid targetId,
        CancellationToken cancellationToken
    )
    {
        var senderRooms = await GetAsync<RoomMembership>(
            $"room_members?select=room_id&user_id=eq.{senderId:D}",
            cancellationToken
        );

        if (senderRooms.Count == 0)
        {
            throw new InvalidOperationException(
                "Kein privater Raum gefunden."
            );
        }

        var senderRoomIds = senderRooms
            .Select(membership => membership.RoomId)
            .Distinct()
            .ToArray();
        var senderRoomFilter = CreateInFilter(senderRoomIds);

        var targetRooms = await GetAsync<RoomMembership>(
            $"room_members?select=room_id&user_id=eq.{targetId:D}" +
            $"&room_id=in.({senderRoomFilter})",
            cancellationToken
        );

        if (targetRooms.Count == 0)
        {
            throw new InvalidOperationException(
                "Kein privater Raum gefunden."
            );
        }

        var sharedRoomIds = targetRooms
            .Select(membership => membership.RoomId)
            .Distinct()
            .ToArray();
        var sharedRoomFilter = CreateInFilter(sharedRoomIds);

        var privateRooms = await GetAsync<RoomReference>(
            $"rooms?select=id&id=in.({sharedRoomFilter})" +
            "&is_group=eq.false&limit=1",
            cancellationToken
        );

        return privateRooms.Count == 1
            ? privateRooms[0].Id
            : throw new InvalidOperationException(
                "Kein privater Raum gefunden."
            );
    }

    private async Task<List<T>> GetAsync<T>(
        string requestUri,
        CancellationToken cancellationToken
    )
    {
        using var response = await _client.GetAsync(
            requestUri,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<T>>(
            cancellationToken
        ) ?? [];
    }

    private static Guid ParseId(string value, string fieldName)
    {
        return Guid.TryParse(value, out var result)
            ? result
            : throw new InvalidOperationException(
                $"{fieldName} must contain a UUID."
            );
    }

    private static string CreateInFilter(IEnumerable<Guid> ids)
    {
        return string.Join(',', ids.Select(id => id.ToString("D")));
    }

    private sealed record RoomMembership(
        [property: JsonPropertyName("room_id")] Guid RoomId
    );

    private sealed record RoomReference(
        [property: JsonPropertyName("id")] Guid Id
    );

    private sealed record StoredMessage(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("room_id")] Guid RoomId,
        [property: JsonPropertyName("sender_id")] Guid SenderId,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt
    );
}
