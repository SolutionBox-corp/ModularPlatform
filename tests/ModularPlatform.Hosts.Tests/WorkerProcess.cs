using System.Diagnostics;
using ModularPlatform.Worker;

namespace ModularPlatform.Hosts.Tests;

internal static class WorkerProcess
{
    public static Process Start(string connectionString, params string[] extraArgs)
    {
        var assemblyPath = typeof(WorkerHostBuilder).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var arg in Args(connectionString).Concat(extraArgs))
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ModularPlatform.Worker.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public static async Task KillAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync();
        process.Dispose();
    }

    private static string[] Args(string connectionString) =>
    [
        "--environment=Testing",
        "--ConnectionStrings:Write", connectionString,
        "--ConnectionStrings:Read", connectionString,
        "--RunMigrationsAtStartup=false",
        "--Messaging:SoloMode=true",
        "--Modules:Identity:Enabled=true",
        "--Modules:Billing:Enabled=true",
        "--Modules:Notifications:Enabled=true",
        "--Modules:Gdpr:Enabled=true",
        "--Modules:Operations:Enabled=true",
        "--Modules:Files:Enabled=true",
        "--Modules:Marketing:Enabled=true",
        "--Modules:Tenancy:Enabled=true",
        "--Persistence:Rls:RuntimePassword=test_app_rls_pwd",
        "--Identity:Auth:AdminEmails:0=admin@platform.test",
        "--Jwt:Issuer=test",
        "--Jwt:Audience=test",
        "--Jwt:SigningKey=integration-test-signing-key-at-least-32b",
        "--Gdpr:Encryption:BlindIndexKey=integration-test-blind-index-key-32ch",
        "--Secrets:MasterKeys:1=aW50ZWdyYXRpb24tdGVzdC1zZWNyZXRzLWtleS0wMzI=",
        "--RateLimiting:GlobalPermitsPerMinute=100000",
        "--RateLimiting:AuthPermitsPerMinute=100000",
        "--Storage:Provider=local",
        "--Storage:Local:RootPath=" + Path.Combine(Path.GetTempPath(), $"mp-worker-storage-{Guid.CreateVersion7():N}"),
        "--Marketing:UseFakeGateways=true",
        "--Billing:Stripe:UseFakeGateway=true",
        "--Billing:Stripe:SuccessUrl=https://app.test/billing/success",
        "--Billing:Stripe:CancelUrl=https://app.test/billing/cancel",
        "--Billing:Subscriptions:Plans:0:PlanKey=pro",
        "--Billing:Subscriptions:Plans:0:StripePriceId=price_test_pro",
        "--Billing:Subscriptions:Plans:0:CreditsPerPeriod=100",
        "--Platform:Payments:Provider=fake",
        "--Platform:Payments:Currency=EUR",
        "--Platform:Payments:Plans:pro:AmountMinorUnits=4900",
        "--Platform:Payments:Plans:pro:Currency=EUR",
        "--Platform:Payments:Plans:pro:Description=Pro plan",
    ];
}
