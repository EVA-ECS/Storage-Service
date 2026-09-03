using Chat.Contracts.Events;
using MassTransit;
using Npgsql;

namespace Storage_Service;

public sealed class Worker
{
    private readonly NpgsqlDataSource _database;

    public Worker(NpgsqlDataSource database)
    {
        _database = database;
    }

    public async Task ProcessAsync(ConsumeContext<ChatMessageEvent> context)
    {
        var message = context.Message;

        const string sql = """
            insert into public.messages
                (id, room_id, sender_id, content, created_at)
            values
                (@id, @room, @sender, @content, @created)
            on conflict (id) do nothing;
            """;

        await using var command = _database.CreateCommand(sql);
        command.Parameters.AddWithValue("id", Guid.Parse(message.MessageId));
        command.Parameters.AddWithValue("room", Guid.Parse(message.TargetId));
        command.Parameters.AddWithValue("sender", Guid.Parse(message.SenderId));
        command.Parameters.AddWithValue("content", message.Ciphertext);
        command.Parameters.AddWithValue("created", message.Timestamp);

        // Erst speichern.
        await command.ExecuteNonQueryAsync(context.CancellationToken);

        // Danach weitergeben.
        var deliveryQueue = await context.GetSendEndpoint(
            new Uri("queue:delivery_queue")
        );

        await deliveryQueue.Send(message, context.CancellationToken);
    }
}
