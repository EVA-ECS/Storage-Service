using System.Collections.Concurrent;
using Chat.Contracts.Events;
using MassTransit;
using Npgsql;

namespace Storage_Service;

public sealed class WorkerPool : IDisposable
{
    private readonly ConcurrentBag<Worker> _freeWorkers = [];
    private readonly SemaphoreSlim _workerAvailable;

    public WorkerPool(
        int workerCount,
        NpgsqlDataSource database,
        ILoggerFactory loggerFactory
    )
    {
        _workerAvailable = new SemaphoreSlim(workerCount, workerCount);

        for (var number = 1; number <= workerCount; number++)
        {
            _freeWorkers.Add(
                new Worker(
                    number,
                    database,
                    loggerFactory.CreateLogger<Worker>()
                )
            );
        }
    }

    public async Task ProcessAsync(ConsumeContext<ChatMessageEvent> context)
    {
        await _workerAvailable.WaitAsync(context.CancellationToken);

        if (!_freeWorkers.TryTake(out var worker))
        {
            _workerAvailable.Release();
            throw new InvalidOperationException("No free worker was found.");
        }

        try
        {
            await worker.ProcessAsync(context);
        }
        finally
        {
            _freeWorkers.Add(worker);
            _workerAvailable.Release();
        }
    }

    public void Dispose()
    {
        _workerAvailable.Dispose();
    }
}
