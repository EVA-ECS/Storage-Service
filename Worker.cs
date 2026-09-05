using Chat.Contracts.Events;

namespace Storage_Service;

public sealed class Worker
{
    private readonly IChatMessageStore _messageStore;

    public Worker(int id, IChatMessageStore messageStore)
    {
        Id = id;
        _messageStore = messageStore;
    }

    public int Id { get; }

    public async Task ProcessAsync(
        ChatMessageEvent message,
        CancellationToken cancellationToken
    )
    {
        Console.WriteLine(
            $"[Worker {Id}] Speichert Nachricht {message.MessageId}."
        );

        await _messageStore.StoreAsync(message, cancellationToken);

        Console.WriteLine(
            $"[Worker {Id}] Nachricht {message.MessageId} wurde gespeichert."
        );
    }
}
