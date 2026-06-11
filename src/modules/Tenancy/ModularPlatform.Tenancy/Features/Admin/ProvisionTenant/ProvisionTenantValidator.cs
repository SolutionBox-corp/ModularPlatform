using FluentValidation;

namespace ModularPlatform.Tenancy.Features.Admin.ProvisionTenant;

internal sealed class ProvisionTenantValidator : AbstractValidator<ProvisionTenantCommand>
{
    private static readonly string[] Reserved = ["admin", "www", "api"];

    public ProvisionTenantValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithErrorCode("tenant.name.required")
            .MaximumLength(256).WithErrorCode("tenant.name.too_long");

        RuleFor(c => c.Subdomain).NotEmpty().WithErrorCode("tenant.subdomain.required")
            .Matches("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$").WithErrorCode("tenant.subdomain.invalid")
            .Must(s => !Reserved.Contains(s?.Trim().ToLowerInvariant())).WithErrorCode("tenant.subdomain.reserved");
    }
}
