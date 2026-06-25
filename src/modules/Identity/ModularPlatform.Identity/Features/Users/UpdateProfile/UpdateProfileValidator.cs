using FluentValidation;

namespace ModularPlatform.Identity.Features.Users.UpdateProfile;

/// <summary>
/// Validates a profile edit: an optional display name within a sane length, and a locale from the supported set.
/// Locale is an allow-list (not free text) so a client can't store an unsupported/oversized value the UI can't render.
/// </summary>
internal sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    /// <summary>Locales the platform ships translations for (mirrors the frontend <c>messages/*.json</c>).</summary>
    private static readonly string[] SupportedLocales = ["en", "cs"];

    public UpdateProfileValidator()
    {
        RuleFor(x => x.DisplayName)
            .MaximumLength(128)
            .WithErrorCode("user.display_name.too_long")
            .When(x => x.DisplayName is not null);

        RuleFor(x => x.Locale)
            .NotEmpty()
            .WithErrorCode("user.locale.required")
            .Must(locale => SupportedLocales.Contains(locale))
            .WithErrorCode("user.locale.unsupported");
    }
}
