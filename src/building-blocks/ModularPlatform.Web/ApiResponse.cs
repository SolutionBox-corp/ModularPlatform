namespace ModularPlatform.Web;

/// <summary>
/// Success envelope ONLY. Errors are never wrapped in this shape — they are always RFC 9457
/// Problem Details (see <see cref="ProblemDetailsMapper"/>). Keeps success responses uniform
/// while staying interoperable + OpenAPI-describable on the error path.
/// </summary>
public sealed record ApiResponse<T>(T Data, string? Message = null)
{
    public bool Success => true;

    public static ApiResponse<T> Ok(T data, string? message = null) => new(data, message);
}
