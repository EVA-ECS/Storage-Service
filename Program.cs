using Chat.Contracts.Events;
using MassTransit;
using Npgsql;
using RabbitMQ.Client;

var builder = Host.CreateApplicationBuilder(args);

var databaseConnection = builder.Configuration.GetConnectionString("Supabase");
if (string.IsNullOrWhiteSpace(databaseConnection))
{
    throw new InvalidOperationException("Supabase connection is missing.");
}

var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
var rabbitUser = builder.Configuration["RabbitMQ:Username"] ?? "admin";
var rabbitPassword = builder.Configuration["RabbitMQ:Password"] ?? "secret";

builder.Services.AddSingleton(
    _ => NpgsqlDataSource.Create(databaseConnection)
);

builder.Services.AddMassTransit(configuration =>
{
    configuration.AddConsumer<StorageConsumer>();

    configuration.UsingRabbitMq((context, rabbit) =>
    {
        rabbit.Host(rabbitHost, "/", host =>
        {
            host.Username(rabbitUser);
            host.Password(rabbitPassword);
        });

        rabbit.ReceiveEndpoint("storage_queue", endpoint =>
        {
            endpoint.ConfigureConsumeTopology = false;
            endpoint.Bind<ChatMessageEvent>(binding =>
            {
                binding.ExchangeType = ExchangeType.Topic;
                binding.RoutingKey = "chat.message.published";
            });

            endpoint.UseMessageRetry(retry =>
            {
                retry.Interval(3, TimeSpan.FromSeconds(5));
            });

            endpoint.ConfigureConsumer<StorageConsumer>(context);
        });
    });
});

await builder.Build().RunAsync();

public sealed class StorageConsumer : IConsumer<ChatMessageEvent>
{
    private readonly NpgsqlDataSource _database;

    public StorageConsumer(NpgsqlDataSource database)
    {
        _database = database;
    }

    public async Task Consume(ConsumeContext<ChatMessageEvent> context)
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

        await using var command = _database.CreateCommand(sql);
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("room_id", roomId);
        command.Parameters.AddWithValue("sender_id", senderId);
        command.Parameters.AddWithValue("content", message.Ciphertext);
        command.Parameters.AddWithValue("created_at", message.Timestamp);

        // Erst speichern.
        await command.ExecuteNonQueryAsync(context.CancellationToken);

        // Danach weiterleiten.
        var deliveryQueue = await context.GetSendEndpoint(
            new Uri("queue:delivery_queue")
        );

        await deliveryQueue.Send(message, context.CancellationToken);
    }
}
