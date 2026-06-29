using FluentValidation;

namespace ModularPlatform.Crm.Features.Companies.CreateCompany;

internal sealed class CreateCompanyValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode("crm.company.name.required")
            .MaximumLength(256).WithErrorCode("crm.company.name.too_long");

        RuleFor(x => x.Domain).MaximumLength(256).WithErrorCode("crm.company.domain.too_long");
        RuleFor(x => x.Industry).MaximumLength(128).WithErrorCode("crm.company.industry.too_long");
        RuleFor(x => x.Notes).MaximumLength(8192).WithErrorCode("crm.company.notes.too_long");
    }
}
