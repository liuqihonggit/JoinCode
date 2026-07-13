namespace JoinCode.CodeIndex.Ast;

internal sealed class SolutionProjectEntry
{
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public required string ProjectGuid { get; init; }
}

internal sealed class SolutionParseResult
{
    public required List<SolutionProjectEntry> Projects { get; init; }
}

internal static class SolutionParser
{
    internal static SolutionParseResult ParseSln(string filePath, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(fs);

        var content = fs.ReadAllText(filePath);
        var solutionDir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var projects = new List<SolutionProjectEntry>();

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Project("))
            {
                continue;
            }

            var entry = ParseProjectLine(trimmed, solutionDir);
            if (entry is not null)
            {
                projects.Add(entry);
            }
        }

        return new SolutionParseResult { Projects = projects };
    }

    internal static SolutionParseResult ParseSlnx(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var doc = System.Xml.Linq.XDocument.Load(filePath);
        var solutionDir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var projects = new List<SolutionProjectEntry>();

        foreach (var elem in doc.Descendants("Project"))
        {
            var pathAttr = elem.Attribute("Path")?.Value;
            if (string.IsNullOrEmpty(pathAttr))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(pathAttr);
            var resolvedPath = Path.IsPathRooted(pathAttr)
                ? pathAttr
                : Path.GetFullPath(Path.Combine(solutionDir, pathAttr));

            projects.Add(new SolutionProjectEntry
            {
                Name = name,
                RelativePath = NormalizePath(resolvedPath),
                ProjectGuid = elem.Attribute("Id")?.Value ?? string.Empty
            });
        }

        return new SolutionParseResult { Projects = projects };
    }

    private static SolutionProjectEntry? ParseProjectLine(string line, string solutionDir)
    {
        var eqIndex = line.IndexOf('=');
        if (eqIndex < 0)
        {
            return null;
        }

        var rest = line.AsSpan()[(eqIndex + 1)..].Trim();
        if (rest.Length < 2 || rest[0] != '"')
        {
            return null;
        }

        var parts = SplitQuotedParts(rest);
        if (parts.Count < 3)
        {
            return null;
        }

        var name = parts[0];
        var relativePath = parts[1];
        var guid = parts[2].Trim('{', '}');

        if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resolvedPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(solutionDir, relativePath));

        return new SolutionProjectEntry
        {
            Name = name,
            RelativePath = NormalizePath(resolvedPath),
            ProjectGuid = guid
        };
    }

    private static List<string> SplitQuotedParts(ReadOnlySpan<char> input)
    {
        var result = new List<string>();
        var i = 0;

        while (i < input.Length)
        {
            while (i < input.Length && input[i] != '"')
            {
                i++;
            }

            if (i >= input.Length)
            {
                break;
            }

            i++;
            var start = i;
            while (i < input.Length && input[i] != '"')
            {
                i++;
            }

            if (i > start)
            {
                result.Add(input[start..i].ToString());
            }

            i++;
        }

        return result;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }
}
