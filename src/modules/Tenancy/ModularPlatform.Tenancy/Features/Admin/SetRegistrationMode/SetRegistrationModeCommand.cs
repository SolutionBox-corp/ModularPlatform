using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.SetRegistrationMode;

/// <summary>
/// Platform-admin changes how users may join an existing tenant through its subdomain. The registration gate reads
/// this value live on every signup, so the change is effective immediately and is not copied into JWT claims.
/// </summary>
public sealed record SetRegistrationModeCommand(Guid TenantId, string RegistrationMode)
    : ICommand<SetRegistrationModeResponse>;

public sealed record SetRegistrationModeResponse(Guid TenantId, string RegistrationMode);

public sealed record SetRegistrationModeRequest(string RegistrationMode);
