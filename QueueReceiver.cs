using Chat.Contracts.Events;
using MassTransit;

namespace Storage_Service;

public sealed class QueueReceiver : IConsumer<ChatMessageEvent>
{
    private readonly WorkerPool _workerPool;

    public QueueReceiver(WorkerPool workerPool)
    {
        _workerPool = workerPool;
    }

    public async Task Consume(ConsumeContext<ChatMessageEvent> context)
    {
        Console.WriteLine(
            $"[QueueReceiver] Nachricht {context.Message.MessageId} empfangen."
        );

        await _workerPool.ProcessAsync(
            context.Message,
            context.CancellationToken
        );

        var deliveryQueue = await context.GetSendEndpoint(
            new Uri("queue:delivery_queue")
        );

        await deliveryQueue.Send(context.Message, context.CancellationToken);

        Console.WriteLine(
            $"[QueueReceiver] Nachricht {context.Message.MessageId} an delivery_queue gesendet."
        );
    }
}
