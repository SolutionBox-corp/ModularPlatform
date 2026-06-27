using FluentValidation;

namespace ModularPlatform.Crm.Features.Meetings.CompleteMeeting;

internal sealed class CompleteMeetingValidator : AbstractValidator<CompleteMeetingCommand>
{
    public CompleteMeetingValidator()
    {
        RuleFor(x => x.Outcome).MaximumLength(8192).WithErrorCode("crm.meeting.outcome.too_long");
    }
}
