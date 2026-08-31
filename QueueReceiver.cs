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

    public Task Consume(ConsumeContext<ChatMessageEvent> context)
    {
        return _workerPool.ProcessAsync(context);
    }
}
