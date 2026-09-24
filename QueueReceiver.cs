using Chat.Contracts.Events;
using MassTransit;

namespace Storage_Service;

public sealed class QueueReceiver : IConsumer<ChatMessageEvent>
{
    private readonly IChatMessageStore _messageStore;

    public QueueReceiver(IChatMessageStore messageStore)
    {
        _messageStore = messageStore;
    }

    public async Task Consume(ConsumeContext<ChatMessageEvent> context)
    {
        var msg = context.Message;
        Console.WriteLine($"[QueueReceiver] Verarbeite Nachricht {msg.MessageId}...");

        // Direkt in Supabase speichern
        await _messageStore.StoreAsync(msg, context.CancellationToken);

        Console.WriteLine($"[QueueReceiver] Nachricht {msg.MessageId} erfolgreich in Supabase gespeichert!");

        // Optional: An die delivery_queue weiterleiten für Echtzeit-Zustellung
        var deliveryQueue = await context.GetSendEndpoint(new Uri("queue:delivery_queue"));
        await deliveryQueue.Send(msg, context.CancellationToken);
    }
}