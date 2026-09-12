namespace Core.Context;

public static class FilePathExtractor
{
    public static List<string> ExtractFilePaths(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(message, @"[A-Za-z]:[\\/][^\s""'<>|]+"))
        {
            if (seen.Add(match.Value)) paths.Add(match.Value);
        }

        foreach (Match match in Regex.Matches(message,
            @"(?:~?[/\\]|\.{1,2}[/\\]|[a-zA-Z][\w]*[/\\])[\w./\\\-]+\.[a-zA-Z]{1,6}"))
        {
            if (seen.Add(match.Value)) paths.Add(match.Value);
        }

        foreach (Match match in Regex.Matches(message,
            @"(?<![\w./\\])[\w][\w.\-]*\.(?:cs|csproj|sln|json|xml|yaml|yml|md|py|js|ts|tsx|jsx|java|go|rs|rb|php|swift|kt|c|cpp|h|hpp)(?![\w])",
            RegexOptions.IgnoreCase))
        {
            if (seen.Add(match.Value)) paths.Add(match.Value);
        }

        return paths;
    }
}
