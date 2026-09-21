using Chat.Contracts.Events;

namespace Storage_Service;

public interface IChatMessageStore
{
    Task StoreAsync(
        ChatMessageEvent message,
        CancellationToken cancellationToken
    );
}
