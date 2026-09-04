using Chat.Contracts.Events;

namespace Storage_Service;

public sealed class Worker
{
    private readonly IChatMessageStore _messageStore;

    public Worker(IChatMessageStore messageStore)
    {
        _messageStore = messageStore;
    }

    public Task ProcessAsync(
        ChatMessageEvent message,
        CancellationToken cancellationToken
    )
    {
        return _messageStore.StoreAsync(message, cancellationToken);
    }
}
