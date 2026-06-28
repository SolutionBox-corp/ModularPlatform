using FluentValidation;
using FluentValidation.Results;
using ModularPlatform.Cqrs;
using ModularPlatform.Cqrs.Behaviors;
using Shouldly;
using PlatformValidationException = ModularPlatform.Cqrs.ValidationException;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task No_validators_calls_next()
    {
        var behavior = new ValidationBehavior<TestRequest, string>([]);
        var nextCalled = false;

        var result = await behavior.Handle(new TestRequest("", ""), () =>
        {
            nextCalled = true;
            return Task.FromResult("ok");
        }, CancellationToken.None);

        result.ShouldBe("ok");
        nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task Multiple_validators_are_aggregated_and_blank_error_code_falls_back()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
            [new NameValidator(), new EmailValidator()]);

        PlatformValidationException? ex = null;
        try
        {
            await behavior.Handle(
                new TestRequest("", ""), () => Task.FromResult("should-not-run"), CancellationToken.None);
        }
        catch (PlatformValidationException caught)
        {
            ex = caught;
        }

        ex.ShouldNotBeNull();
        ex.Errors.Select(e => e.Field).ShouldBe(["Name", "Email"]);
        ex.Errors.Select(e => e.ErrorCode).ShouldBe(["validation.invalid", "user.email.required"]);
        ex.Errors.Select(e => e.Message).ShouldBe(["Name is required.", "Email is required."]);
    }

    private sealed record TestRequest(string Name, string Email);

    private sealed class NameValidator : AbstractValidator<TestRequest>
    {
        public NameValidator()
        {
            RuleFor(x => x).Custom((request, context) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    context.AddFailure(new ValidationFailure("Name", "Name is required.")
                    {
                        ErrorCode = "",
                    });
                }
            });
        }
    }

    private sealed class EmailValidator : AbstractValidator<TestRequest>
    {
        public EmailValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .WithErrorCode("user.email.required")
                .WithMessage("Email is required.");
        }
    }
}
