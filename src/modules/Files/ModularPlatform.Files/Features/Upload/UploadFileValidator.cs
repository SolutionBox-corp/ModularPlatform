using FluentValidation;

namespace ModularPlatform.Files.Features.Upload;

/// <summary>
/// SECURITY gate for uploads (runs in the validation behavior BEFORE the handler): non-empty file, a content-type
/// on the allowlist (deny by default) and a size at or under the cap.
/// </summary>
internal sealed class UploadFileValidator : AbstractValidator<UploadFileCommand>
{
    public UploadFileValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithErrorCode("file.name.required")
            .MaximumLength(512).WithErrorCode("file.name.too_long");

        RuleFor(x => x.Size)
            .GreaterThan(0).WithErrorCode("file.empty")
            .LessThanOrEqualTo(FileUploadPolicy.MaxSizeBytes).WithErrorCode("file.too_large");

        RuleFor(x => x.ContentType)
            .Must(ct => FileUploadPolicy.AllowedContentTypes.Contains(ct))
            .WithErrorCode("file.content_type.not_allowed");
    }
}
