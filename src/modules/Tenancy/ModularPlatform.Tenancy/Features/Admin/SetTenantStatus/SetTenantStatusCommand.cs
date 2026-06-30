using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.SetTenantStatus;

/// <summary>
/// Platform-admin lifecycle control. Only Active/Suspended are externally settable; other TenantStatus values are
/// internal provisioning/separation states and are not exposed through this endpoint.
/// </summary>
public sealed record SetTenantStatusCommand(Guid TenantId, string Status) : ICommand<SetTenantStatusResponse>;

public sealed record SetTenantStatusResponse(Guid TenantId, string Status);

public sealed record SetTenantStatusRequest(string Status);
