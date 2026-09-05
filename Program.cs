using Chat.Contracts.Events;
using MassTransit;
using RabbitMQ.Client;
using Storage_Service;

var builder = Host.CreateApplicationBuilder(args);

var supabaseUrl = builder.Configuration["Supabase:Url"];
if (!Uri.TryCreate(supabaseUrl, UriKind.Absolute, out var supabaseUri))
{
    throw new Exception("Supabase-URL fehlt oder ist ungültig.");
}

var supabaseSecretKey = builder.Configuration["Supabase:SecretKey"];
if (string.IsNullOrWhiteSpace(supabaseSecretKey))
{
    throw new Exception("Supabase Secret Key fehlt.");
}

var workerCount = builder.Configuration.GetValue("Storage:WorkerCount", 3);
if (workerCount < 1)
{
    throw new Exception("Mindestens ein Worker wird benötigt.");
}

var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
var rabbitUser = builder.Configuration["RabbitMQ:Username"] ?? "admin";
var rabbitPassword = builder.Configuration["RabbitMQ:Password"] ?? "secret";

builder.Services.AddSingleton(_ =>
{
    var client = new HttpClient
    {
        BaseAddress = new Uri($"{supabaseUri.ToString().TrimEnd('/')}/rest/v1/")
    };

    client.DefaultRequestHeaders.Add("apikey", supabaseSecretKey);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("EVA-Storage-Service/1.0");
    return client;
});
builder.Services.AddSingleton<IChatMessageStore, SupabaseChatMessageStore>();

builder.Services.AddSingleton(provider =>
    new WorkerPool(
        workerCount,
        provider.GetRequiredService<IChatMessageStore>()
    )
);

builder.Services.AddMassTransit(config =>
{
    config.AddConsumer<QueueReceiver>();

    config.UsingRabbitMq((context, rabbit) =>
    {
        rabbit.Host(rabbitHost, "/", host =>
        {
            host.Username(rabbitUser);
            host.Password(rabbitPassword);
        });

        rabbit.ReceiveEndpoint("storage_queue", endpoint =>
        {
            endpoint.PrefetchCount = workerCount;
            endpoint.ConfigureConsumeTopology = false;

            endpoint.Bind<ChatMessageEvent>(binding =>
            {
                binding.ExchangeType = ExchangeType.Topic;
                binding.RoutingKey = "chat.message.published";
            });

            endpoint.UseMessageRetry(retry =>
                retry.Interval(3, TimeSpan.FromSeconds(5))
            );

            endpoint.ConfigureConsumer<QueueReceiver>(
                context,
                consumer => consumer.UseConcurrentMessageLimit(workerCount)
            );
        });
    });
});

await builder.Build().RunAsync();
