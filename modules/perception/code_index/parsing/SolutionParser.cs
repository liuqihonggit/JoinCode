namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// 解决方案项目条目 — 描述 .sln/.slnx 中单个项目的名称、相对路径与 GUID
/// </summary>
internal sealed class SolutionProjectEntry {
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
internal sealed class SolutionParseResult {
    /// <summary>项目条目列表</summary>
    public required List<SolutionProjectEntry> Projects { get; init; }
}

/// <summary>
/// 解决方案解析器 — 解析 .sln 与 .slnx 文件提取项目条目
/// </summary>
internal static class SolutionParser {
    /// <summary>
    /// 解析 .sln 文件 — 逐行扫描 Project(...) 条目
    /// </summary>
    /// <param name="filePath">.sln 文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <returns>解决方案解析结果</returns>
    internal static async Task<SolutionParseResult> ParseSlnAsync(string filePath, IFileSystem fs) {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(fs);

        var content = await fs.ReadAllText(filePath).ConfigureAwait(false);
        var solutionDir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var projects = new List<SolutionProjectEntry>();

        foreach (var line in content.Split('\n')) {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Project(")) {
                continue;
            }

            var entry = ParseProjectLine(trimmed, solutionDir);
            if (entry is not null) {
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
    internal static async Task<SolutionParseResult> ParseSlnxAsync(string filePath, IFileSystem fs) {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(fs);

        var doc = XDocument.Parse(await fs.ReadAllText(filePath).ConfigureAwait(false));
        var solutionDir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var projects = new List<SolutionProjectEntry>();

        foreach (var elem in doc.Descendants("Project")) {
            var pathAttr = elem.Attribute("Path")?.Value;
            if (string.IsNullOrEmpty(pathAttr)) {
                continue;
            }

            // slnx 中路径可能用 \ 分隔（Windows 风格），统一为平台分隔符后再解析
            // Linux 上 Path.GetFileNameWithoutExtension 不识别 \ 分隔符
            var normalizedPath = pathAttr.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var name = Path.GetFileNameWithoutExtension(normalizedPath);
            var resolvedPath = Path.IsPathRooted(normalizedPath)
                ? normalizedPath
                : Path.GetFullPath(Path.Combine(solutionDir, normalizedPath));

            projects.Add(new SolutionProjectEntry {
                Name = name,
                RelativePath = NormalizePath(resolvedPath),
                ProjectGuid = elem.Attribute("Id")?.Value ?? string.Empty
            });
        }

        return new SolutionParseResult { Projects = projects };
    }

    internal static SolutionProjectEntry? ParseProjectLine(string line, string solutionDir) {
        var eqIndex = line.IndexOf('=');
        if (eqIndex < 0) {
            return null;
        }

        var rest = line.AsSpan()[(eqIndex + 1)..].Trim();
        if (rest.Length < 2 || rest[0] != '"') {
            return null;
        }

        var parts = SplitQuotedParts(rest);
        if (parts.Count < 3) {
            return null;
        }

        var name = parts[0];
        var relativePath = parts[1];
        var guid = parts[2].Trim('{', '}');

        if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var resolvedPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(solutionDir, relativePath));

        return new SolutionProjectEntry {
            Name = name,
            RelativePath = NormalizePath(resolvedPath),
            ProjectGuid = guid
        };
    }

    internal static List<string> SplitQuotedParts(ReadOnlySpan<char> input) {
        var result = new List<string>();
        var i = 0;
        var spin = new SpinWait();

        while (i < input.Length) {
            var prev = i;
            while (i < input.Length && input[i] != '"') {
                i++;
            }

            if (i >= input.Length) {
                break;
            }

            i++;
            var start = i;
            while (i < input.Length && input[i] != '"') {
                i++;
            }

            if (i > start) {
                result.Add(input[start..i].ToString());
            }

            i++;

            // 游标停滞防御(#102 CodeIndex 超时同类根因): 尾部 i++ 被变异(i--)后,
            // 相邻引号场景 i 在 c/c+1 震荡永不前进 → testhost 挂死。
            // 正常路径每轮至少前进 1(停滞检测永不触发); SpinWait.Count 为框架内部计数器,
            // Stryker 单点变异无法破坏, 上限=输入长度(正常扫描轮数上界)。
            if (i <= prev) {
                spin.SpinOnce();
                if (spin.Count > input.Length) throw new InvalidOperationException(
                    $"SplitQuotedParts 游标停滞于 {i}(轮首 {prev}), 判定游标前进被破坏(变异防御)");
            }
        }

        return result;
    }

    private static string NormalizePath(string path) {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }
}