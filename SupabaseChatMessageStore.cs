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

    public async Task StoreAsync(ChatMessageEvent message, CancellationToken cancellationToken)
    {
        var messageId = ParseId(message.MessageId, nameof(message.MessageId));
        var senderId = ParseId(message.SenderId, nameof(message.SenderId));
        var targetId = ParseId(message.TargetId, nameof(message.TargetId));

        if (senderId == targetId)
        {
            throw new InvalidOperationException("SenderId und TargetId müssen unterschiedlich sein.");
        }

        var roomId = await GetOrCreatePrivateRoomAsync(senderId, targetId, cancellationToken);

        var storedMessage = new StoredMessage(
            messageId,
            roomId,
            senderId,
            targetId,
            message.Ciphertext,
            message.Timestamp
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "messages?on_conflict=id")
        {
            Content = JsonContent.Create(storedMessage)
        };
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=ignore-duplicates,return=minimal");

        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<Guid> GetOrCreatePrivateRoomAsync(Guid senderId, Guid targetId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "rpc/get_or_create_private_room")
        {
            Content = JsonContent.Create(new PrivateRoomRequest(senderId, targetId))
        };

        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rooms = await response.Content.ReadFromJsonAsync<List<RoomReference>>(cancellationToken) ?? [];

        return rooms.Count == 1
            ? rooms[0].RoomId
            : throw new InvalidOperationException("Supabase hat keinen eindeutigen privaten Raum geliefert.");
    }

    private static Guid ParseId(string value, string fieldName)
    {
        return Guid.TryParse(value, out var result)
            ? result
            : throw new InvalidOperationException($"{fieldName} must contain a UUID.");
    }

    private sealed record PrivateRoomRequest(
        [property: JsonPropertyName("p_sender_id")] Guid SenderId,
        [property: JsonPropertyName("p_target_id")] Guid TargetId
    );

    private sealed record RoomReference(
        [property: JsonPropertyName("room_id")] Guid RoomId
    );

    private sealed record StoredMessage(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("room_id")] Guid RoomId,
        [property: JsonPropertyName("sender_id")] Guid SenderId,
        [property: JsonPropertyName("receiver_id")] Guid ReceiverId,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt
    );
}