namespace Core.Configuration;

public static class RuleFrontmatterParser
{
    public static (string Content, bool AlwaysApply, string Globs, string Description) Parse(string rawContent)
    {
        if (!rawContent.StartsWith("---", StringComparison.Ordinal))
        {
            return (rawContent, false, string.Empty, string.Empty);
        }

        var endIdx = rawContent.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIdx < 0)
        {
            return (rawContent, false, string.Empty, string.Empty);
        }

        var frontmatter = rawContent[3..endIdx].Trim();
        var body = rawContent[(endIdx + 3)..].TrimStart('\n', '\r');

        var alwaysApply = false;
        var globs = string.Empty;
        var description = string.Empty;

        foreach (var line in frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) continue;

            var key = trimmed[..colonIdx].Trim();
            var value = trimmed[(colonIdx + 1)..].Trim();

            if (key.Equals("alwaysApply", StringComparison.OrdinalIgnoreCase)
                || key.Equals("always-apply", StringComparison.OrdinalIgnoreCase))
            {
                alwaysApply = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            else if (key.Equals("globs", StringComparison.OrdinalIgnoreCase))
            {
                globs = value.Trim('"', '\'');
            }
            else if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                description = value.Trim('"', '\'');
            }
        }

        return (body, alwaysApply, globs, description);
    }
}
