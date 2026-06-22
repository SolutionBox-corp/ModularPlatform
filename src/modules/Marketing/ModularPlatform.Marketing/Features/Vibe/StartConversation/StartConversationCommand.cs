using ModularPlatform.Cqrs;

namespace ModularPlatform.Marketing.Features.Vibe.StartConversation;

/// <summary>Starts a new vibe-marketing chat thread for the calling user. <c>Title</c> is optional (defaults applied).</summary>
public sealed record StartConversationCommand(Guid UserId, string? Title) : ICommand<StartConversationResponse>;

public sealed record StartConversationResponse(Guid ConversationId);

/// <summary>Wire request. <c>Title</c> is optional — blank/missing falls back to "New conversation".</summary>
public sealed record StartConversationRequest(string? Title);
