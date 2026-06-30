using FluentValidation;
using ModularPlatform.Crm.Entities;

namespace ModularPlatform.Crm.Features.Contacts.CreateContact;

internal sealed class CreateContactValidator : AbstractValidator<CreateContactCommand>
{
    public CreateContactValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithErrorCode("crm.contact.first_name.required")
            .MaximumLength(128).WithErrorCode("crm.contact.first_name.too_long");

        RuleFor(x => x.LastName)
            .NotEmpty().WithErrorCode("crm.contact.last_name.required")
            .MaximumLength(128).WithErrorCode("crm.contact.last_name.too_long");

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
            RuleFor(x => x.Email)
                .EmailAddress().WithErrorCode("crm.contact.email.invalid")
                .MaximumLength(256).WithErrorCode("crm.contact.email.too_long"));

        RuleFor(x => x.Phone).MaximumLength(64).WithErrorCode("crm.contact.phone.too_long");
        RuleFor(x => x.Position).MaximumLength(256).WithErrorCode("crm.contact.position.too_long");
        RuleFor(x => x.Notes).MaximumLength(8192).WithErrorCode("crm.contact.notes.too_long");

        RuleFor(x => x.Status)
            .Must(ContactStatuses.IsValid).WithErrorCode("crm.contact.status.invalid");

        RuleFor(x => x.Tags).Must(t => t.Length <= 32).WithErrorCode("crm.contact.tags.too_many");
        RuleForEach(x => x.Tags).MaximumLength(48).WithErrorCode("crm.contact.tag.too_long");
    }
}
