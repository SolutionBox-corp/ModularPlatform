using FluentValidation;

namespace ModularPlatform.Files.Features.Rename;

/// <summary>Validates the new file name before the handler runs (same rules as the upload validator).</summary>
internal sealed class RenameFileValidator : AbstractValidator<RenameFileCommand>
{
    public RenameFileValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithErrorCode("file.name.required")
            .MaximumLength(512).WithErrorCode("file.name.too_long");
    }
}
