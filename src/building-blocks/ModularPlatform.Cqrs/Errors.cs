namespace ModularPlatform.Cqrs;

/// <summary>
/// Base for every business/domain error. Carries a stable, language-independent
/// <see cref="ErrorCode"/> (e.g. "credit.insufficient_balance") and the HTTP status
/// it maps to. The Web layer turns these into RFC 9457 Problem Details; the message
/// is localized from a resx whose key == ErrorCode.
/// </summary>
public abstract class ModularPlatformException(string message) : Exception(message)
{
    public abstract string ErrorCode { get; }
    public abstract int StatusCode { get; }
}

public sealed record ValidationError(string Field, string ErrorCode, string Message);

public sealed class ValidationException(IReadOnlyList<ValidationError> errors)
    : ModularPlatformException("One or more validation errors occurred.")
{
    public override string ErrorCode => "validation.failed";
    public override int StatusCode => StatusCodes.BadRequest;
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}

public sealed class NotFoundException(string errorCode, string message)
    : ModularPlatformException(message)
{
    public override string ErrorCode { get; } = errorCode;
    public override int StatusCode => StatusCodes.NotFound;
}

public sealed class ConflictException(string errorCode, string message)
    : ModularPlatformException(message)
{
    public override string ErrorCode { get; } = errorCode;
    public override int StatusCode => StatusCodes.Conflict;
}

public sealed class ForbiddenException(string errorCode, string message)
    : ModularPlatformException(message)
{
    public override string ErrorCode { get; } = errorCode;
    public override int StatusCode => StatusCodes.Forbidden;
}

public sealed class UnauthorizedException(string errorCode, string message)
    : ModularPlatformException(message)
{
    public override string ErrorCode { get; } = errorCode;
    public override int StatusCode => StatusCodes.Unauthorized;
}

/// <summary>
/// The tenant is not entitled to the module behind this endpoint. Maps to <b>404</b> (route-not-found shape), NOT
/// 403 — a disabled module must not leak its existence. Raised by the <c>ModuleEntitlementGuard</c> / <c>.RequireModule</c>.
/// </summary>
public sealed class NotEntitledException(string errorCode, string message)
    : ModularPlatformException(message)
{
    public override string ErrorCode { get; } = errorCode;
    public override int StatusCode => StatusCodes.NotFound;
}

/// <summary>Generic 422-style business-rule violation (e.g. coupon expired).</summary>
public sealed class BusinessRuleException(string errorCode, string message)
    : ModularPlatformException(message)
{
    public override string ErrorCode { get; } = errorCode;
    public override int StatusCode => StatusCodes.UnprocessableEntity;
}

/// <summary>Status codes kept here so Cqrs has no ASP.NET dependency.</summary>
internal static class StatusCodes
{
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int UnprocessableEntity = 422;
}
