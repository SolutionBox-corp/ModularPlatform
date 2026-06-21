using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Marketing.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Marketing.Features.Vibe.StartConversation;

/// <summary>
/// Creates an empty vibe-chat conversation owned by the caller. A simple write — no event to publish — so it commits
/// through the outbox context (the platform's single write seam) and returns the new id.
/// </summary>
internal sealed class StartConversationHandler(IDbContextOutbox<MarketingDbContext> outbox)
    : ICommandHandler<StartConversationCommand, StartConversationResponse>
{
    public async Task<StartConversationResponse> Handle(StartConversationCommand command, CancellationToken ct)
    {
        var title = string.IsNullOrWhiteSpace(command.Title) ? "New conversation" : command.Title.Trim();

        var conversation = new VibeConversation
        {
            UserId = command.UserId,
            Title = title,
        };
        outbox.DbContext.VibeConversations.Add(conversation);
        await outbox.SaveChangesAndFlushMessagesAsync();

        return new StartConversationResponse(conversation.Id);
    }
}
