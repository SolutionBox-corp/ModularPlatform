using FluentValidation;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Tenancy.Features.Admin.ProvisionTenant;

internal sealed class ProvisionTenantValidator : AbstractValidator<ProvisionTenantCommand>
{
    public ProvisionTenantValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithErrorCode("tenant.name.required")
            .MaximumLength(256).WithErrorCode("tenant.name.too_long");

        RuleFor(c => c.Subdomain).NotEmpty().WithErrorCode("tenant.subdomain.required")
            .Matches("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$").WithErrorCode("tenant.subdomain.invalid")
            // Shared with TenantResolutionMiddleware via ReservedSubdomains — the two can never drift now.
            .Must(s => s is null || !ReservedSubdomains.All.Contains(s.Trim())).WithErrorCode("tenant.subdomain.reserved");
    }
}
