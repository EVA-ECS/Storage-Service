using Chat.Contracts.Events;
using MassTransit;
using Npgsql;

namespace Storage_Service;

public sealed class Worker
{
    private readonly int _number;
    private readonly NpgsqlDataSource _database;
    private readonly ILogger<Worker> _logger;

    public Worker(
        int number,
        NpgsqlDataSource database,
        ILogger<Worker> logger
    )
    {
        _number = number;
        _database = database;
        _logger = logger;
    }

    public async Task ProcessAsync(ConsumeContext<ChatMessageEvent> context)
    {
        var message = context.Message;

        // Aktuell: TargetId = Raum, Ciphertext = Text.
        var messageId = Guid.Parse(message.MessageId);
        var roomId = Guid.Parse(message.TargetId);
        var senderId = Guid.Parse(message.SenderId);

        const string sql = """
            insert into public.messages
                (id, room_id, sender_id, content, created_at)
            values
                (@id, @room_id, @sender_id, @content, @created_at)
            on conflict (id) do nothing;
            """;

        _logger.LogInformation(
            "Worker {WorkerNumber} stores message {MessageId}",
            _number,
            messageId
        );

        await using var command = _database.CreateCommand(sql);
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("room_id", roomId);
        command.Parameters.AddWithValue("sender_id", senderId);
        command.Parameters.AddWithValue("content", message.Ciphertext);
        command.Parameters.AddWithValue("created_at", message.Timestamp);

        await command.ExecuteNonQueryAsync(context.CancellationToken);

        var deliveryQueue = await context.GetSendEndpoint(
            new Uri("queue:delivery_queue")
        );

        await deliveryQueue.Send(message, context.CancellationToken);

        _logger.LogInformation(
            "Worker {WorkerNumber} finished message {MessageId}",
            _number,
            messageId
        );
    }
}
