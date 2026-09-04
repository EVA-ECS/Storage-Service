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
            _freeWorkers.Add(new Worker(i + 1, messageStore));
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

        Console.WriteLine(
            $"[WorkerPool] Nachricht {message.MessageId} geht an Worker {worker.Id}."
        );

        try
        {
            await worker.ProcessAsync(message, cancellationToken);
        }
        finally
        {
            _freeWorkers.Add(worker);
            Console.WriteLine(
                $"[WorkerPool] Worker {worker.Id} ist wieder frei."
            );
        }
    }
}
