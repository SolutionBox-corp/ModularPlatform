using FluentValidation;
using ModularPlatform.Files.Contracts;

namespace ModularPlatform.Files.Features.Links.LinkFile;

internal sealed class LinkFileValidator : AbstractValidator<LinkFileToOwnerCommand>
{
    public LinkFileValidator()
    {
        RuleFor(x => x.FileObjectId)
            .NotEmpty().WithErrorCode("file.id.required");

        RuleFor(x => x.OwnerType)
            .NotEmpty().WithErrorCode("file.link.owner_type.required")
            .MaximumLength(128).WithErrorCode("file.link.owner_type.too_long")
            .Matches("^[a-z0-9._-]+$").WithErrorCode("file.link.owner_type.invalid");

        RuleFor(x => x.OwnerId)
            .NotEmpty().WithErrorCode("file.link.owner_id.required");
    }
}
