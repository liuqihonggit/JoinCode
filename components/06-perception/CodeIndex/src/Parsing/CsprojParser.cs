namespace CodeIndex.Ast;

internal sealed class CsprojParseResult
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public string? TargetFramework { get; init; }
    public string? OutputType { get; init; }
    public required List<string> ProjectReferences { get; init; }
    public required List<(string Name, string? Version)> PackageReferences { get; init; }
}

internal sealed class CsprojParser
{
    internal static CsprojParseResult Parse(string filePath, IFileSystem fs, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(fs);

        var doc = XDocument.Load(filePath);
        var projectDir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(filePath);

        var msbuildProps = LoadMsBuildProperties(projectDir, fs, workspaceRoot);

        var targetFramework = ExtractProperty(doc, "TargetFramework");
        var outputType = ExtractProperty(doc, "OutputType");

        var projectRefs = ExtractProjectReferences(doc, projectDir, msbuildProps);
        var packageRefs = ExtractPackageReferences(doc);

        return new CsprojParseResult
        {
            Name = name,
            FilePath = filePath,
            TargetFramework = targetFramework,
            OutputType = outputType,
            ProjectReferences = projectRefs,
            PackageReferences = packageRefs
        };
    }

    private static string? ExtractProperty(XDocument doc, string propertyName)
    {
        return doc.Descendants(propertyName)
            .Select(e => e.Value.Trim())
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
    }

    private static List<string> ExtractProjectReferences(XDocument doc, string projectDir, Dictionary<string, string> msbuildProps)
    {
        var result = new List<string>();

        foreach (var elem in doc.Descendants("ProjectReference"))
        {
            var include = elem.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
            {
                continue;
            }

            var resolved = ResolvePath(include, projectDir, msbuildProps);
            if (resolved is not null)
            {
                result.Add(resolved);
            }
        }

        return result;
    }

    private static List<(string Name, string? Version)> ExtractPackageReferences(XDocument doc)
    {
        var result = new List<(string Name, string? Version)>();

        foreach (var elem in doc.Descendants("PackageReference"))
        {
            var include = elem.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
            {
                continue;
            }

            var version = elem.Attribute("Version")?.Value
                ?? elem.Element("Version")?.Value;

            if (!string.IsNullOrEmpty(version) && version.Contains('$'))
            {
                version = null;
            }

            result.Add((include, version));
        }

        return result;
    }

    private static string? ResolvePath(string include, string projectDir, Dictionary<string, string> msbuildProps)
    {
        var resolved = include;

        if (include.Contains('$'))
        {
            resolved = ReplaceMsBuildVariables(include, msbuildProps);
            if (resolved.Contains('$'))
            {
                return null;
            }
        }

        if (!Path.IsPathRooted(resolved))
        {
            resolved = Path.GetFullPath(Path.Combine(projectDir, resolved));
        }

        return NormalizePath(resolved);
    }

    private static string ReplaceMsBuildVariables(string input, Dictionary<string, string> props)
    {
        var result = input;
        var maxIterations = 10;

        for (var i = 0; i < maxIterations; i++)
        {
            var changed = false;
            foreach (var kvp in props)
            {
                var placeholder = $"$({kvp.Key})";
                if (result.Contains(placeholder))
                {
                    result = result.Replace(placeholder, kvp.Value);
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return result;
    }

    private static Dictionary<string, string> LoadMsBuildProperties(string projectDir, IFileSystem fs, string? workspaceRoot)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var searchRoot = workspaceRoot ?? projectDir;
        var propsFiles = FindDirectoryBuildProps(searchRoot, projectDir, fs);

        foreach (var propsFile in propsFiles)
        {
            var propsDir = Path.GetDirectoryName(propsFile) ?? string.Empty;
            props["MSBuildThisFileDirectory"] = propsDir.EndsWith(Path.DirectorySeparatorChar)
                ? propsDir
                : propsDir + Path.DirectorySeparatorChar;

            try
            {
                var doc = XDocument.Load(propsFile);
                foreach (var pg in doc.Descendants("PropertyGroup"))
                {
                    foreach (var elem in pg.Elements())
                    {
                        var value = elem.Value.Trim();
                        if (!string.IsNullOrEmpty(value) && !value.Contains('<') && !value.Contains('>'))
                        {
                            props[elem.Name.LocalName] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"CsprojParser: Failed to parse Directory.Build.props file: {ex.Message}");
            }
        }

        return props;
    }

    private static List<string> FindDirectoryBuildProps(string searchRoot, string projectDir, IFileSystem fs)
    {
        var result = new List<string>();
        var rootFullPath = Path.GetFullPath(searchRoot);
        var currentDirPath = Path.GetFullPath(projectDir);

        while (currentDirPath is not null)
        {
            var propsPath = Path.Combine(currentDirPath, "Directory.Build.props");
            if (fs.FileExists(propsPath))
            {
                result.Add(propsPath);
            }

            if (string.Equals(currentDirPath, rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentDirPath = fs.GetParentPath(currentDirPath);
        }

        result.Reverse();
        return result;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }
}
