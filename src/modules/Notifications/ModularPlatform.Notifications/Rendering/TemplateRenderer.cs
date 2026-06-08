namespace ModularPlatform.Notifications.Rendering;

/// <summary>
/// Substitutes {placeholders} in a template string from a key/value data dictionary. Unmatched
/// placeholders are left intact. Pure function — no I/O, trivially unit-testable.
/// </summary>
internal static class TemplateRenderer
{
    public static string Render(string template, IReadOnlyDictionary<string, string> data)
    {
        if (string.IsNullOrEmpty(template) || data.Count == 0)
        {
            return template;
        }

        var result = template;
        foreach (var (key, value) in data)
        {
            result = result.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        return result;
    }
}
