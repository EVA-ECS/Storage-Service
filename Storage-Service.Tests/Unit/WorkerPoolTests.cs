using System.Diagnostics;
using Chat.Contracts.Events;
using Xunit;
using Xunit.Abstractions;

namespace Storage_Service.Tests;

// Parallelität des Worker-Pools mit einem Testspeicher, ohne RabbitMQ oder Supabase.
[Trait("Category", "Unit")]
public sealed class WorkerPoolTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DreiNachrichtenWerdenParallelGespeichert()
    {
        var testStore = new TestMessageStore();
        var workerPool = new WorkerPool(3, testStore);
        var nachrichten = Enumerable.Range(1, 3)
            .Select(CreateMessage)
            .ToArray();

        var zeit = Stopwatch.StartNew();

        await Task.WhenAll(
            nachrichten.Select(message => workerPool.ProcessAsync(
                message,
                CancellationToken.None
            ))
        );

        zeit.Stop();

        Assert.Equal(3, testStore.StoredCount);
        Assert.Equal(3, testStore.MaxParallel);

        output.WriteLine($"Gespeicherte Nachrichten: {testStore.StoredCount}");
        output.WriteLine($"Gleichzeitig aktive Worker: {testStore.MaxParallel}");
        output.WriteLine($"Testdauer: {zeit.ElapsedMilliseconds} ms");
    }

    private static ChatMessageEvent CreateMessage(int number)
    {
        return new ChatMessageEvent(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            $"Testnachricht {number}",
            DateTime.UtcNow
        );
    }

    private sealed class TestMessageStore : IChatMessageStore
    {
        private int _active;
        private int _maxParallel;
        private int _storedCount;

        public int MaxParallel => _maxParallel;
        public int StoredCount => _storedCount;

        public async Task StoreAsync(
            ChatMessageEvent message,
            CancellationToken cancellationToken
        )
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);

            try
            {
                await Task.Delay(200, cancellationToken);
                Interlocked.Increment(ref _storedCount);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMaximum(int current)
        {
            while (true)
            {
                var previous = Volatile.Read(ref _maxParallel);
                if (current <= previous)
                {
                    return;
                }

                if (Interlocked.CompareExchange(
                    ref _maxParallel,
                    current,
                    previous
                ) == previous)
                {
                    return;
                }
            }
        }
    }
}
