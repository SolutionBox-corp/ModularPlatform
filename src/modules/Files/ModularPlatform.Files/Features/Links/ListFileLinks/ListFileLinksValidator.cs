using FluentValidation;

namespace ModularPlatform.Files.Features.Links.ListFileLinks;

internal sealed class ListFileLinksValidator : AbstractValidator<ListFileLinksQuery>
{
    public ListFileLinksValidator()
    {
        RuleFor(x => x.OwnerType)
            .NotEmpty().WithErrorCode("file.link.owner_type.required")
            .MaximumLength(128).WithErrorCode("file.link.owner_type.too_long")
            .Matches("^[a-z0-9._-]+$").WithErrorCode("file.link.owner_type.invalid");

        RuleFor(x => x.OwnerId)
            .NotEmpty().WithErrorCode("file.link.owner_id.required");
    }
}
