using System.Collections.Concurrent;
using Chat.Contracts.Events;

namespace Storage_Service;

public sealed class WorkerPool
{
    private readonly ConcurrentBag<Worker> _freeWorkers = [];

    public WorkerPool(int workerCount, IChatMessageStore messageStore)
    {
        for (var i = 0; i < workerCount; i++)
        {
            _freeWorkers.Add(new Worker(messageStore));
        }
    }

    public async Task ProcessAsync(
        ChatMessageEvent message,
        CancellationToken cancellationToken
    )
    {
        if (!_freeWorkers.TryTake(out var worker))
        {
            throw new Exception("Kein Worker frei.");
        }

        try
        {
            await worker.ProcessAsync(message, cancellationToken);
        }
        finally
        {
            _freeWorkers.Add(worker);
        }
    }
}
