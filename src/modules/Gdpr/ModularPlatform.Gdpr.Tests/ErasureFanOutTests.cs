using Microsoft.Extensions.Logging.Abstractions;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Contracts;
using ModularPlatform.Gdpr.Messaging;
using Shouldly;

namespace ModularPlatform.Gdpr.Tests;

public sealed class ErasureFanOutTests
{
    [Fact]
    public async Task Throwing_eraser_does_not_block_other_erasers_or_crypto_shred()
    {
        var userId = Guid.CreateVersion7();
        var healthy = new HealthyEraser();
        var broken = new BrokenEraser();
        var dispatcher = new CapturingDispatcher();
        var handler = new UserErasureRequestedHandler();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            handler.Handle(
                new UserErasureRequested(Guid.CreateVersion7(), DateTimeOffset.UtcNow, userId),
                [broken, healthy],
                dispatcher,
                NullLogger<UserErasureRequestedHandler>.Instance,
                CancellationToken.None));

        ex.Message.ShouldContain("1 module eraser(s) failed");
        broken.Called.ShouldBeTrue();
        healthy.ErasedUserId.ShouldBe(userId);
        dispatcher.SentCommands.ShouldContain("ShredSubjectKeyCommand");
    }

    private sealed class HealthyEraser : IErasePersonalData
    {
        public string ModuleName => "Healthy";
        public Guid? ErasedUserId { get; private set; }

        public Task EraseAsync(Guid userId, CancellationToken ct)
        {
            ErasedUserId = userId;
            return Task.CompletedTask;
        }
    }

    private sealed class BrokenEraser : IErasePersonalData
    {
        public string ModuleName => "Broken";
        public bool Called { get; private set; }

        public Task EraseAsync(Guid userId, CancellationToken ct)
        {
            Called = true;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class CapturingDispatcher : IDispatcher
    {
        public List<string> SentCommands { get; } = [];

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            SentCommands.Add(command.GetType().Name);
            return Task.FromResult(default(TResult)!);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }
}
