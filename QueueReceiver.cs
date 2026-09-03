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
        // MassTransit acknowledges the RabbitMQ message only after this method
        // completes. Persist first, then forward to Delivery.
        await _workerPool.ProcessAsync(
            context.Message,
            context.CancellationToken
        );

        var deliveryQueue = await context.GetSendEndpoint(
            new Uri("queue:delivery_queue")
        );

        await deliveryQueue.Send(context.Message, context.CancellationToken);
    }
}
