using ModularPlatform.Notifications.Contracts;
using Shouldly;

namespace ModularPlatform.Notifications.Tests;

/// <summary>
/// Placeholder slice test. Exercises the SendNotification wire request mapping shape so the test project
/// has a real assertion; full integration tests (Testcontainers-Postgres) are added by the test pass.
/// </summary>
public sealed class TemplateRendererTests
{
    [Fact]
    public void SendNotificationCommand_carries_channels_and_data()
    {
        var data = new Dictionary<string, string> { ["displayName"] = "Ada" };
        var command = new SendNotificationCommand(
            Guid.CreateVersion7(), "welcome", ["email", "inapp"], data);

        command.Channels.ShouldBe(["email", "inapp"]);
        command.TemplateKey.ShouldBe("welcome");
        command.Data["displayName"].ShouldBe("Ada");
    }
}
