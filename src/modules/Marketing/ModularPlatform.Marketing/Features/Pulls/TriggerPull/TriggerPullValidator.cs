using FluentValidation;
using ModularPlatform.Marketing.Entities;

namespace ModularPlatform.Marketing.Features.Pulls.TriggerPull;

internal sealed class TriggerPullValidator : AbstractValidator<TriggerPullCommand>
{
    public TriggerPullValidator()
    {
        RuleFor(x => x.Source)
            .Cascade(CascadeMode.Stop)
            .Must(IsKnownSource)
            .WithErrorCode("marketing.source_unknown")
            .WithMessage("Unknown marketing source.")
            .Must(IsSupportedSource)
            .WithErrorCode("marketing.source_not_supported")
            .WithMessage("This marketing source is not supported yet.");

        RuleFor(x => x.StartDate)
            .LessThanOrEqualTo(x => x.EndDate)
            .WithErrorCode("marketing.date_range_invalid")
            .WithMessage("StartDate must be on or before EndDate.");
    }

    private static bool IsKnownSource(string source) =>
        Enum.TryParse<PullSource>(source, ignoreCase: true, out _);

    private static bool IsSupportedSource(string source) =>
        Enum.TryParse<PullSource>(source, ignoreCase: true, out var parsed)
        && parsed is PullSource.Ga4 or PullSource.Gsc;
}
