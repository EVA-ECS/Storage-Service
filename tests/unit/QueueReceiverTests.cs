using Chat.Contracts.Events;
using MassTransit;
using Moq;
using Xunit;

namespace Storage_Service.Tests;

public sealed class QueueReceiverTests
{
    private static ChatMessageEvent Message() => new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "encrypted", DateTime.UtcNow);

    [Fact]
    public async Task ConsumerCannotForwardBeforeTheDatabaseWriteCompletes()
    {
        var stored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var message = Message();
        var store = new Mock<IChatMessageStore>();
        store.Setup(x => x.StoreAsync(message, default)).Returns(stored.Task);
        var endpoint = new Mock<ISendEndpoint>();
        var context = new Mock<ConsumeContext<ChatMessageEvent>>();
        context.SetupGet(x => x.Message).Returns(message);
        context.Setup(x => x.GetSendEndpoint(new Uri("queue:delivery_queue"))).ReturnsAsync(endpoint.Object);
        var consuming = new QueueReceiver(store.Object).Consume(context.Object);
        Assert.False(consuming.IsCompleted);
        context.Verify(x => x.GetSendEndpoint(It.IsAny<Uri>()), Times.Never);
        stored.SetResult();
        await consuming;
        endpoint.Verify(x => x.Send(message, default), Times.Once);
    }

    [Fact]
    public async Task StorageFailurePropagatesWithoutForwarding()
    {
        var message = Message();
        var store = new Mock<IChatMessageStore>();
        store.Setup(x => x.StoreAsync(message, default)).ThrowsAsync(new IOException("database unavailable"));
        var context = new Mock<ConsumeContext<ChatMessageEvent>>(); context.SetupGet(x => x.Message).Returns(message);
        await Assert.ThrowsAsync<IOException>(() => new QueueReceiver(store.Object).Consume(context.Object));
        context.Verify(x => x.GetSendEndpoint(It.IsAny<Uri>()), Times.Never);
    }

    [Fact]
    public async Task ForwardingFailurePreventsSuccessfulConsumerCompletion()
    {
        var message = Message();
        var store = new Mock<IChatMessageStore>(); store.Setup(x => x.StoreAsync(message, default)).Returns(Task.CompletedTask);
        var endpoint = new Mock<ISendEndpoint>(); endpoint.Setup(x => x.Send(message, default)).ThrowsAsync(new IOException("broker unavailable"));
        var context = new Mock<ConsumeContext<ChatMessageEvent>>(); context.SetupGet(x => x.Message).Returns(message);
        context.Setup(x => x.GetSendEndpoint(It.IsAny<Uri>())).ReturnsAsync(endpoint.Object);
        await Assert.ThrowsAsync<IOException>(() => new QueueReceiver(store.Object).Consume(context.Object));
        store.Verify(x => x.StoreAsync(message, default), Times.Once);
    }
}
