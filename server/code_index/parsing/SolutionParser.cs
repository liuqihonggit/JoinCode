namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// 解决方案项目条目 — 描述 .sln/.slnx 中单个项目的名称、相对路径与 GUID
/// </summary>
internal sealed class SolutionProjectEntry
{
    /// <summary>项目名称</summary>
    public required string Name { get; init; }
    /// <summary>项目相对路径（已规范化）</summary>
    public required string RelativePath { get; init; }
    /// <summary>项目 GUID</summary>
    public required string ProjectGuid { get; init; }
}

/// <summary>
/// 解决方案解析结果 — 包含解决方案中所有项目条目
/// </summary>
internal sealed class SolutionParseResult
{
    /// <summary>项目条目列表</summary>
    public required List<SolutionProjectEntry> Projects { get; init; }
}

/// <summary>
/// 解决方案解析器 — 解析 .sln 与 .slnx 文件提取项目条目
/// </summary>
internal static class SolutionParser
{
    /// <summary>
    /// 解析 .sln 文件 — 逐行扫描 Project(...) 条目
    /// </summary>
    /// <param name="filePath">.sln 文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <returns>解决方案解析结果</returns>
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

    /// <summary>
    /// 解析 .slnx 文件 — 基于 XML 提取 Project 元素的 Path/Id 属性
    /// </summary>
    /// <param name="filePath">.slnx 文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <returns>解决方案解析结果</returns>
    internal static SolutionParseResult ParseSlnx(string filePath, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(fs);

        var doc = XDocument.Parse(fs.ReadAllText(filePath));
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
