using FluentValidation;

namespace ModularPlatform.Cqrs.Behaviors;

/// <summary>
/// Runs every FluentValidation validator registered for the request type, aggregates failures,
/// and throws a single <see cref="ValidationException"/> (mapped to RFC 9457 400 with a
/// per-field <c>errors[]</c>). Runs for both commands and queries.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var validatorList = validators as IValidator<TRequest>[] ?? validators.ToArray();
        if (validatorList.Length == 0)
        {
            return await next();
        }

        var results = await Task.WhenAll(validatorList.Select(v =>
            v.ValidateAsync(new ValidationContext<TRequest>(request), ct)));

        var errors = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .Select(f => new ValidationError(
                Field: f.PropertyName,
                ErrorCode: string.IsNullOrWhiteSpace(f.ErrorCode) ? "validation.invalid" : f.ErrorCode,
                Message: f.ErrorMessage))
            .ToArray();

        if (errors.Length > 0)
        {
            throw new ValidationException(errors);
        }

        return await next();
    }
}
