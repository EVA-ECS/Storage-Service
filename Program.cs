using Chat.Contracts.Events;
using MassTransit;
using Npgsql;
using RabbitMQ.Client;
using Storage_Service;

var builder = Host.CreateApplicationBuilder(args);

var databaseConnection = builder.Configuration.GetConnectionString("Supabase");
if (string.IsNullOrWhiteSpace(databaseConnection))
{
    throw new InvalidOperationException("Supabase connection is missing.");
}

var workerCount = builder.Configuration.GetValue("Storage:WorkerCount", 3);
if (workerCount < 1)
{
    throw new InvalidOperationException("WorkerCount must be at least 1.");
}

var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
var rabbitUser = builder.Configuration["RabbitMQ:Username"] ?? "admin";
var rabbitPassword = builder.Configuration["RabbitMQ:Password"] ?? "secret";

builder.Services.AddSingleton(
    _ => NpgsqlDataSource.Create(databaseConnection)
);
builder.Services.AddSingleton(serviceProvider =>
    new WorkerPool(
        workerCount,
        serviceProvider.GetRequiredService<NpgsqlDataSource>(),
        serviceProvider.GetRequiredService<ILoggerFactory>()
    )
);

builder.Services.AddMassTransit(configuration =>
{
    configuration.AddConsumer<QueueReceiver>();

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
            endpoint.PrefetchCount = workerCount;

            endpoint.Bind<ChatMessageEvent>(binding =>
            {
                binding.ExchangeType = ExchangeType.Topic;
                binding.RoutingKey = "chat.message.published";
            });

            endpoint.UseMessageRetry(retry =>
            {
                retry.Interval(3, TimeSpan.FromSeconds(5));
            });

            endpoint.ConfigureConsumer<QueueReceiver>(
                context,
                consumer => consumer.UseConcurrentMessageLimit(workerCount)
            );
        });
    });
});

await builder.Build().RunAsync();
