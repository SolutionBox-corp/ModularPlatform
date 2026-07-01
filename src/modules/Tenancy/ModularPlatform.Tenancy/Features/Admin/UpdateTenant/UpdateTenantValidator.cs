using FluentValidation;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Tenancy.Features.Admin.UpdateTenant;

internal sealed class UpdateTenantValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithErrorCode("tenant.name.required")
            .MaximumLength(256).WithErrorCode("tenant.name.too_long");

        RuleFor(c => c.Subdomain).NotEmpty().WithErrorCode("tenant.subdomain.required")
            .Matches("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$").WithErrorCode("tenant.subdomain.invalid")
            .Must(s => s is null || !ReservedSubdomains.All.Contains(s.Trim())).WithErrorCode("tenant.subdomain.reserved");
    }
}
