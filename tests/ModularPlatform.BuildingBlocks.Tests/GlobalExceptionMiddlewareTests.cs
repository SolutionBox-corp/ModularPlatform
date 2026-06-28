using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web.Errors;
using ModularPlatform.Web.Localization;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task Unhandled_exception_returns_safe_problem_details()
    {
        var context = NewHttpContext();
        var middleware = NewMiddleware(
            _ => throw new InvalidOperationException("database hostname must not leak"),
            isDevelopment: false);

        await middleware.Invoke(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.ShouldStartWith("application/problem+json");

        var json = await ReadResponseJson(context);
        json.RootElement.GetProperty("title").GetString().ShouldBe("error.unexpected");
        json.RootElement.GetProperty("type").GetString().ShouldBe("https://errors.modularplatform.dev/error.unexpected");
        json.RootElement.GetProperty("detail").GetString().ShouldBe("Something went wrong.");
        json.RootElement.GetProperty("errorCode").GetString().ShouldBe("error.unexpected");
        json.RootElement.TryGetProperty("exception", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Development_500_includes_exception_extension()
    {
        var context = NewHttpContext();
        var middleware = NewMiddleware(
            _ => throw new InvalidOperationException("development detail"),
            isDevelopment: true);

        await middleware.Invoke(context);

        var json = await ReadResponseJson(context);
        json.RootElement.GetProperty("exception").GetString().ShouldNotBeNull().ShouldContain("development detail");
    }

    [Fact]
    public async Task Validation_exception_returns_problem_details_with_errors_extension_shape()
    {
        var context = NewHttpContext();
        var middleware = NewMiddleware(_ => throw new ValidationException(
        [
            new ValidationError("email", "user.email_invalid", "Email is invalid."),
        ]));

        await middleware.Invoke(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        context.Response.ContentType.ShouldStartWith("application/problem+json");

        var json = await ReadResponseJson(context);
        json.RootElement.GetProperty("title").GetString().ShouldBe("validation.failed");
        json.RootElement.GetProperty("type").GetString().ShouldBe("https://errors.modularplatform.dev/validation.failed");
        json.RootElement.GetProperty("detail").GetString().ShouldBe("Validation failed.");
        json.RootElement.GetProperty("errorCode").GetString().ShouldBe("validation.failed");
        json.RootElement.GetProperty("traceId").GetString().ShouldBe(context.TraceIdentifier);

        var errors = json.RootElement.GetProperty("errors");
        errors.ValueKind.ShouldBe(JsonValueKind.Array);
        errors.GetArrayLength().ShouldBe(1);
        errors[0].GetProperty("field").GetString().ShouldBe("email");
        errors[0].GetProperty("errorCode").GetString().ShouldBe("user.email_invalid");
        errors[0].GetProperty("message").GetString().ShouldBe("Email is invalid.");
    }

    [Fact]
    public async Task Started_response_rethrows_original_exception_without_overwriting_stream()
    {
        var context = NewStartedHttpContext();
        var original = new InvalidOperationException("stream failed after headers");
        var middleware = NewMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("already streamed"));
            throw original;
        });

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => middleware.Invoke(context));

        thrown.ShouldBeSameAs(original);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        (await ReadResponseText(context)).ShouldBe("already streamed");
    }

    private static GlobalExceptionMiddleware NewMiddleware(
        RequestDelegate next,
        bool isDevelopment = false)
    {
        return new GlobalExceptionMiddleware(
            next,
            new TestLocalizer(),
            NullLogger<GlobalExceptionMiddleware>.Instance,
            new TestEnvironmentAccessor(isDevelopment));
    }

    private static DefaultHttpContext NewHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static DefaultHttpContext NewStartedHttpContext()
    {
        var context = new DefaultHttpContext();
        var feature = new StartedResponseFeature();
        context.Features.Set<IHttpResponseFeature>(feature);
        context.Response.Body = feature.Body;
        return context;
    }

    private static async Task<JsonDocument> ReadResponseJson(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private static async Task<string> ReadResponseText(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private sealed class TestEnvironmentAccessor(bool isDevelopment) : IHostEnvironmentAccessor
    {
        public bool IsDevelopment { get; } = isDevelopment;
    }

    private sealed class TestLocalizer : IStringLocalizer<SharedResource>
    {
        private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
        {
            ["error.unexpected"] = "Something went wrong.",
            ["validation.failed"] = "Validation failed.",
        };

        public LocalizedString this[string name]
        {
            get
            {
                var found = Values.TryGetValue(name, out var value);
                return new LocalizedString(name, found ? value! : name, resourceNotFound: !found);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var localized = this[name];
                return localized.ResourceNotFound
                    ? localized
                    : new LocalizedString(name, string.Format(CultureInfo.InvariantCulture, localized.Value, arguments));
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return Values.Select(kvp => new LocalizedString(kvp.Key, kvp.Value));
        }
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }
}
