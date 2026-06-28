using ModularPlatform.Cqrs;

namespace ModularPlatform.Notifications.Features.Notifications.MarkAllRead;

public sealed record MarkAllReadCommand(Guid UserId) : ICommand<MarkAllReadResponse>;

public sealed record MarkAllReadResponse(int Marked);
