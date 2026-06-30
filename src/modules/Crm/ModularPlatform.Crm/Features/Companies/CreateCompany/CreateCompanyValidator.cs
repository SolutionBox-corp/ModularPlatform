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
        RuleFor(x => x.IdentificationNumber).MaximumLength(32).WithErrorCode("crm.company.identification_number.too_long");
        RuleFor(x => x.TaxIdentificationNumber).MaximumLength(32).WithErrorCode("crm.company.tax_identification_number.too_long");
        RuleFor(x => x.RegisteredAddress).MaximumLength(512).WithErrorCode("crm.company.registered_address.too_long");
        RuleFor(x => x.City).MaximumLength(128).WithErrorCode("crm.company.city.too_long");
        RuleFor(x => x.PostalCode).MaximumLength(32).WithErrorCode("crm.company.postal_code.too_long");
        RuleFor(x => x.Country).MaximumLength(128).WithErrorCode("crm.company.country.too_long");
        RuleFor(x => x.Notes).MaximumLength(8192).WithErrorCode("crm.company.notes.too_long");
    }
}
