using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ModularPlatform.Web;

/// <summary>
/// Fails the host at startup (via <c>ValidateOnStart</c>) if JWT is misconfigured outside Development — a weak
/// or missing signing key is auth-fails-open (anyone can mint tokens). Development is exempt so local runs work
/// without a configured key.
/// </summary>
public sealed class JwtOptionsValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    public const int MinimumKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SigningKey) ||
            Encoding.UTF8.GetByteCount(options.SigningKey) < MinimumKeyBytes)
        {
            errors.Add($"Jwt:SigningKey must be configured and at least {MinimumKeyBytes} bytes outside Development.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            errors.Add("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            errors.Add("Jwt:Audience is required.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
