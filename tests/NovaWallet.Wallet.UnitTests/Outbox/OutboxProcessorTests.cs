using FluentAssertions;
using Microsoft.Extensions.Logging;
using NovaWallet.BuildingBlocks.Domain.Outbox;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Outbox;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NovaWallet.Wallet.UnitTests.Outbox;

public class OutboxProcessorTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IWalletRepository _repository = Substitute.For<IWalletRepository>();
    private readonly IIntegrationEventPublisher _publisher = Substitute.For<IIntegrationEventPublisher>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly OutboxProcessor _processor;

    public OutboxProcessorTests()
    {
        _clock.UtcNow.Returns(UtcNow);
        _processor = new OutboxProcessor(_repository, _publisher, _clock, Substitute.For<ILogger<OutboxProcessor>>());
    }

    [Fact]
    public async Task Publishes_every_pending_message_and_marks_it_processed()
    {
        var message = OutboxMessage.Create("TransferCompletedIntegrationEvent", "{}", UtcNow.AddMinutes(-1));
        _repository.GetPendingOutboxMessagesAsync(20, Arg.Any<CancellationToken>()).Returns([message]);

        var processedCount = await _processor.ProcessPendingAsync(20, CancellationToken.None);

        processedCount.Should().Be(1);
        await _publisher.Received(1).PublishAsync("TransferCompletedIntegrationEvent", "{}", Arg.Any<CancellationToken>());
        message.ProcessedOnUtc.Should().Be(UtcNow);
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_publish_failure_on_one_message_does_not_stop_the_batch_or_throw()
    {
        var failing = OutboxMessage.Create("EventA", "{}", UtcNow.AddMinutes(-2));
        var succeeding = OutboxMessage.Create("EventB", "{}", UtcNow.AddMinutes(-1));
        _repository.GetPendingOutboxMessagesAsync(20, Arg.Any<CancellationToken>()).Returns([failing, succeeding]);

        _publisher.PublishAsync("EventA", "{}", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("broker unreachable"));
        _publisher.PublishAsync("EventB", "{}", Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var act = async () => await _processor.ProcessPendingAsync(20, CancellationToken.None);

        await act.Should().NotThrowAsync("one bad message must never take down the whole poll");
        failing.ProcessedOnUtc.Should().BeNull("a failed publish must stay unprocessed so it's retried next poll");
        failing.Error.Should().Contain("broker unreachable");
        succeeding.ProcessedOnUtc.Should().Be(UtcNow, "the rest of the batch must still be processed despite the earlier failure");
    }

    [Fact]
    public async Task Does_nothing_and_does_not_save_when_there_is_nothing_pending()
    {
        _repository.GetPendingOutboxMessagesAsync(20, Arg.Any<CancellationToken>()).Returns([]);

        var processedCount = await _processor.ProcessPendingAsync(20, CancellationToken.None);

        processedCount.Should().Be(0);
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
