using FluentValidation;
using ModularPlatform.Crm.Entities;

namespace ModularPlatform.Crm.Features.Contacts.AddInteraction;

internal sealed class AddInteractionValidator : AbstractValidator<AddInteractionCommand>
{
    public AddInteractionValidator()
    {
        RuleFor(x => x.Type)
            .Must(InteractionTypes.IsValid).WithErrorCode("crm.interaction.type.invalid");

        RuleFor(x => x.Body).MaximumLength(8192).WithErrorCode("crm.interaction.body.too_long");
    }
}
