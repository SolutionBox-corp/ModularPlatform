using FluentValidation;
using ModularPlatform.Marketing.Entities;

namespace ModularPlatform.Marketing.Features.Pulls.TriggerPull;

internal sealed class TriggerPullValidator : AbstractValidator<TriggerPullCommand>
{
    public TriggerPullValidator()
    {
        RuleFor(x => x.Source)
            .Must(s => Enum.TryParse<PullSource>(s, ignoreCase: true, out _))
            .WithErrorCode("marketing.source_unknown")
            .WithMessage("Unknown marketing source.");

        RuleFor(x => x.StartDate)
            .LessThanOrEqualTo(x => x.EndDate)
            .WithErrorCode("marketing.date_range_invalid")
            .WithMessage("StartDate must be on or before EndDate.");
    }
}
