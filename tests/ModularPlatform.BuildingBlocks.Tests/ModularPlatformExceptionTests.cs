using ModularPlatform.Cqrs;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class ModularPlatformExceptionTests
{
    [Fact]
    public void Exception_subtypes_map_to_the_documented_http_status_codes()
    {
        var cases = new Dictionary<ModularPlatformException, int>
        {
            [new ValidationException([new ValidationError("email", "user.email.required", "Email is required.")])] = 400,
            [new UnauthorizedException("auth.required", "Authentication required.")] = 401,
            [new ForbiddenException("permission.denied", "Permission denied.")] = 403,
            [new NotFoundException("entity.not_found", "Entity not found.")] = 404,
            [new NotEntitledException("module.not_found", "Module not found.")] = 404,
            [new ConflictException("entity.conflict", "Entity conflict.")] = 409,
            [new BusinessRuleException("rule.failed", "Rule failed.")] = 422,
        };

        foreach (var (exception, expectedStatusCode) in cases)
        {
            exception.StatusCode.ShouldBe(expectedStatusCode, exception.GetType().Name);
        }
    }

    [Fact]
    public void Validation_exception_carries_validation_failed_code_and_field_errors()
    {
        var error = new ValidationError("email", "user.email.required", "Email is required.");
        var ex = new ValidationException([error]);

        ex.ErrorCode.ShouldBe("validation.failed");
        ex.Errors.ShouldBe([error]);
    }
}
