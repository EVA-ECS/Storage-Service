using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
            return await CreatePrivateRoomAsync(
                senderId,
                targetId,
                cancellationToken
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
            return await CreatePrivateRoomAsync(
                senderId,
                targetId,
                cancellationToken
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
            : await CreatePrivateRoomAsync(
                senderId,
                targetId,
                cancellationToken
            );
    }

    private async Task<Guid> CreatePrivateRoomAsync(
        Guid senderId,
        Guid targetId,
        CancellationToken cancellationToken
    )
    {
        var roomId = CreatePrivateRoomId(senderId, targetId);

        await PostAsync(
            "rooms?on_conflict=id",
            new NewPrivateRoom(roomId, "Direct message", false, senderId),
            "resolution=ignore-duplicates,return=minimal",
            cancellationToken
        );

        await PostAsync(
            "room_members?on_conflict=room_id,user_id",
            new[]
            {
                new NewRoomMembership(roomId, senderId),
                new NewRoomMembership(roomId, targetId)
            },
            "resolution=ignore-duplicates,return=minimal",
            cancellationToken
        );

        return roomId;
    }

    private async Task PostAsync<T>(
        string requestUri,
        T body,
        string prefer,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Prefer", prefer);

        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
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

    private static Guid CreatePrivateRoomId(Guid firstUserId, Guid secondUserId)
    {
        var userIds = new[]
        {
            firstUserId.ToString("D"),
            secondUserId.ToString("D")
        };
        Array.Sort(userIds, StringComparer.Ordinal);

        var input = Encoding.UTF8.GetBytes(
            $"eva-private-room-v1:{userIds[0]}:{userIds[1]}"
        );
        var hash = SHA256.HashData(input);
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record RoomMembership(
        [property: JsonPropertyName("room_id")] Guid RoomId
    );

    private sealed record RoomReference(
        [property: JsonPropertyName("id")] Guid Id
    );

    private sealed record NewPrivateRoom(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("is_group")] bool IsGroup,
        [property: JsonPropertyName("created_by")] Guid CreatedBy
    );

    private sealed record NewRoomMembership(
        [property: JsonPropertyName("room_id")] Guid RoomId,
        [property: JsonPropertyName("user_id")] Guid UserId
    );

    private sealed record StoredMessage(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("room_id")] Guid RoomId,
        [property: JsonPropertyName("sender_id")] Guid SenderId,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt
    );
}
