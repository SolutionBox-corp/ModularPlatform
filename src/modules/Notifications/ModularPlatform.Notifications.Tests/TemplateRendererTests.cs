using ModularPlatform.Notifications.Rendering;
using Shouldly;

namespace ModularPlatform.Notifications.Tests;

public sealed class TemplateRendererTests
{
    [Fact]
    public void Render_replaces_all_matching_placeholders()
    {
        var result = TemplateRenderer.Render(
            "Hello {displayName}, deal {dealName} is ready for {displayName}.",
            new Dictionary<string, string>
            {
                ["displayName"] = "Ada",
                ["dealName"] = "Apollo",
            });

        result.ShouldBe("Hello Ada, deal Apollo is ready for Ada.");
    }

    [Fact]
    public void Render_leaves_unmatched_placeholders_intact()
    {
        var result = TemplateRenderer.Render(
            "Hello {displayName}, missing {unknown}.",
            new Dictionary<string, string> { ["displayName"] = "Ada" });

        result.ShouldBe("Hello Ada, missing {unknown}.");
    }

    [Fact]
    public void Render_returns_empty_template_unchanged()
    {
        TemplateRenderer.Render("", new Dictionary<string, string> { ["displayName"] = "Ada" })
            .ShouldBe("");
    }

    [Fact]
    public void Render_returns_template_unchanged_when_data_is_empty()
    {
        TemplateRenderer.Render("Hello {displayName}.", new Dictionary<string, string>())
            .ShouldBe("Hello {displayName}.");
    }

    [Fact]
    public void Render_does_not_loop_when_value_contains_placeholder_text()
    {
        var result = TemplateRenderer.Render(
            "Hello {displayName}.",
            new Dictionary<string, string>
            {
                ["displayName"] = "{dealName}",
                ["dealName"] = "Apollo",
            });

        result.ShouldBe("Hello Apollo.");
    }
}
