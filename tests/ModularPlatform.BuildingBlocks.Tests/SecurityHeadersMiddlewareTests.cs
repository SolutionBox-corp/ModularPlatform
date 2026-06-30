using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using ModularPlatform.Web;
using ModularPlatform.Web.Errors;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task Headers_are_written_before_downstream_starts_the_response()
    {
        var context = new DefaultHttpContext();
        var responseFeature = new MutableStartedResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.Response.Body = responseFeature.Body;
        var middleware = new SecurityHeadersMiddleware(
            async ctx =>
            {
                responseFeature.Started = true;
                await ctx.Response.WriteAsync("streaming");
            },
            new TestEnvironmentAccessor(isDevelopment: true));

        await middleware.Invoke(context);

        context.Response.HasStarted.ShouldBeTrue();
        context.Response.Headers["X-Content-Type-Options"].ToArray().ShouldBe(["nosniff"]);
        context.Response.Headers["X-Frame-Options"].ToArray().ShouldBe(["DENY"]);
        context.Response.Headers["Referrer-Policy"].ToArray().ShouldBe(["no-referrer"]);
        context.Response.Headers["Content-Security-Policy"].ToArray().ShouldBe(["default-src 'none'; frame-ancestors 'none'"]);
    }

    [Fact]
    public async Task Hsts_is_added_only_outside_development()
    {
        var development = await InvokeWithEnvironmentAsync(isDevelopment: true);
        var production = await InvokeWithEnvironmentAsync(isDevelopment: false);

        development.Response.Headers.ContainsKey("Strict-Transport-Security").ShouldBeFalse();
        production.Response.Headers["Strict-Transport-Security"].ToArray()
            .ShouldBe(["max-age=31536000; includeSubDomains"]);
    }

    private static async Task<DefaultHttpContext> InvokeWithEnvironmentAsync(bool isDevelopment)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, new TestEnvironmentAccessor(isDevelopment));

        await middleware.Invoke(context);

        return context;
    }

    private sealed class TestEnvironmentAccessor(bool isDevelopment) : IHostEnvironmentAccessor
    {
        public bool IsDevelopment { get; } = isDevelopment;
    }

    private sealed class MutableStartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool Started { get; set; }
        public bool HasStarted => Started;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }
}
