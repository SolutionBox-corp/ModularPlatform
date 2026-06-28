using ModularPlatform.Cqrs;

namespace ModularPlatform.Files.Features.Links.UnlinkFile;

public sealed record UnlinkFileCommand(Guid LinkId, Guid UserId) : ICommand;
