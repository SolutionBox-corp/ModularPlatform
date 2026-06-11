using ModularPlatform.Cqrs;

namespace ModularPlatform.Files.Features.Download;

/// <summary>Resolves the stored-blob coordinates for a file the CALLER owns. Ownership is enforced at the app
/// layer (<c>UserId</c> from the token) AND by RLS — defence in depth, so a foreign id is a 404 even if RLS is
/// disabled in a deployment.</summary>
public sealed record GetFileQuery(Guid FileId, Guid UserId) : IQuery<FileContentDescriptor>;

public sealed record FileContentDescriptor(string StorageKey, string FileName, string ContentType);
