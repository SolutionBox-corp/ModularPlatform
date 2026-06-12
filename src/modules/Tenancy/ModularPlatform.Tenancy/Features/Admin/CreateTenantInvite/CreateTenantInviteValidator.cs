using FluentValidation;

namespace ModularPlatform.Tenancy.Features.Admin.CreateTenantInvite;

internal sealed class CreateTenantInviteValidator : AbstractValidator<CreateTenantInviteCommand>
{
    public CreateTenantInviteValidator()
    {
        RuleFor(c => c.ExpiresInDays)
            .InclusiveBetween(1, 30).WithErrorCode("tenant.invite.expiry_out_of_range");
    }
}
