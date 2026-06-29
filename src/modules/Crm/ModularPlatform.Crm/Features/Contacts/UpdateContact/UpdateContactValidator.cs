using FluentValidation;
using ModularPlatform.Crm.Entities;

namespace ModularPlatform.Crm.Features.Contacts.UpdateContact;

internal sealed class UpdateContactValidator : AbstractValidator<UpdateContactCommand>
{
    public UpdateContactValidator()
    {
        // PATCH: every field is optional. A null field is "unchanged"; only validate what was supplied.
        When(x => x.FullName is not null, () =>
            RuleFor(x => x.FullName)
                .NotEmpty().WithErrorCode("crm.contact.full_name.required")
                .MaximumLength(256).WithErrorCode("crm.contact.full_name.too_long"));

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
            RuleFor(x => x.Email)
                .EmailAddress().WithErrorCode("crm.contact.email.invalid")
                .MaximumLength(256).WithErrorCode("crm.contact.email.too_long"));

        RuleFor(x => x.Phone).MaximumLength(64).WithErrorCode("crm.contact.phone.too_long");
        RuleFor(x => x.Company).MaximumLength(256).WithErrorCode("crm.contact.company.too_long");
        RuleFor(x => x.Position).MaximumLength(256).WithErrorCode("crm.contact.position.too_long");
        RuleFor(x => x.Notes).MaximumLength(8192).WithErrorCode("crm.contact.notes.too_long");

        When(x => x.Status is not null, () =>
            RuleFor(x => x.Status)
                .Must(ContactStatuses.IsValid).WithErrorCode("crm.contact.status.invalid"));

        When(x => x.Tags is not null, () =>
        {
            RuleFor(x => x.Tags!).Must(t => t.Length <= 32).WithErrorCode("crm.contact.tags.too_many");
            RuleForEach(x => x.Tags!).MaximumLength(48).WithErrorCode("crm.contact.tag.too_long");
        });
    }
}
