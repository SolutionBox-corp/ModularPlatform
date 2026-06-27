using FluentValidation;

namespace ModularPlatform.Crm.Features.Meetings.CreateMeeting;

internal sealed class CreateMeetingValidator : AbstractValidator<CreateMeetingCommand>
{
    public CreateMeetingValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithErrorCode("crm.meeting.title.required")
            .MaximumLength(256).WithErrorCode("crm.meeting.title.too_long");

        RuleFor(x => x.ScheduledAt)
            .Must(d => d != default).WithErrorCode("crm.meeting.scheduled_at.required");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(1, 1440).WithErrorCode("crm.meeting.duration.invalid");

        RuleFor(x => x.Location).MaximumLength(512).WithErrorCode("crm.meeting.location.too_long");
        RuleFor(x => x.Notes).MaximumLength(8192).WithErrorCode("crm.meeting.notes.too_long");
    }
}
