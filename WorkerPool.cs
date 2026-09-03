using System.Collections.Concurrent;
using Chat.Contracts.Events;
using MassTransit;
using Npgsql;

namespace Storage_Service;

public sealed class WorkerPool
{
    private readonly ConcurrentBag<Worker> _freeWorkers = [];

    public WorkerPool(int workerCount, NpgsqlDataSource database)
    {
        for (var i = 0; i < workerCount; i++)
        {
            _freeWorkers.Add(new Worker(database));
        }
    }

    public async Task ProcessAsync(ConsumeContext<ChatMessageEvent> context)
    {
        if (!_freeWorkers.TryTake(out var worker))
        {
            throw new Exception("Kein Worker frei.");
        }

        try
        {
            await worker.ProcessAsync(context);
        }
        finally
        {
            _freeWorkers.Add(worker);
        }
    }
}
