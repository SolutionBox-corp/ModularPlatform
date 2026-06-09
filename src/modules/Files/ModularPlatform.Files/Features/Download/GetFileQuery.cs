using ModularPlatform.Cqrs;

namespace ModularPlatform.Files.Features.Download;

/// <summary>Resolves the stored-blob coordinates for a file the CALLER owns (RLS-scoped → 404 for anyone else).</summary>
public sealed record GetFileQuery(Guid FileId) : IQuery<FileContentDescriptor>;

public sealed record FileContentDescriptor(string StorageKey, string FileName, string ContentType);
