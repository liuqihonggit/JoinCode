namespace Infrastructure.HotSpot;

/// <summary>
/// 热文件检测器实现 — 启发式规则判断文件是否为热文件
/// 通用支持 C#/Java/Python/JS/Go 等，纯路径模式匹配不读文件系统
/// 规则：目录约定 + 命名约定 + 配置文件 + 可配扩展
/// </summary>
[Register(typeof(IHotFileDetector), ServiceLifetime.Singleton)]
public sealed class HotFileDetector : IHotFileDetector
{
    private readonly FrozenSet<string> _hotDirectoryKeywords;
    private readonly FrozenSet<string> _configExtensions;
    private readonly FrozenSet<string> _extraHotFiles;
    private readonly string[] _extraPatterns;

    private static readonly FrozenSet<string> DefaultHotDirectoryKeywords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "abstractions", "contracts", "foundation", "interfaces", "api", "shared", "common", "public");

    private static readonly FrozenSet<string> DefaultConfigExtensions = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, ".json", ".yaml", ".yml", ".toml");

    private static readonly FrozenSet<string> ExcludedDirectories = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "bin", "obj", ".vs", ".vscode", ".idea", ".git", ".svn",
        "node_modules", "__pycache__", ".gradle", "build", "dist", "target",
        "artifacts", ".codegraph", ".jcc");

    public HotFileDetector() : this(null, null) { }

    public HotFileDetector(
        IReadOnlyCollection<string>? extraHotFiles = null,
        IReadOnlyCollection<string>? extraPatterns = null)
    {
        _hotDirectoryKeywords = DefaultHotDirectoryKeywords;
        _configExtensions = DefaultConfigExtensions;
        _extraHotFiles = extraHotFiles is null
            ? FrozenSet<string>.Empty
            : extraHotFiles.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _extraPatterns = extraPatterns?.ToArray() ?? [];
    }

    public bool IsHotFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var normalized = filePath.Replace('\\', '/');

        if (IsExcludedPath(normalized))
            return false;

        var fileName = GetFileName(normalized);
        var extension = GetExtension(fileName);

        if (_extraHotFiles.Contains(normalized) || _extraHotFiles.Contains(fileName))
            return true;

        if (IsHotByDirectory(normalized))
            return true;

        if (IsHotByName(fileName, extension))
            return true;

        if (IsHotByConfig(extension, fileName))
            return true;

        if (IsHotByExtraPattern(normalized, fileName))
            return true;

        return false;
    }

    public IReadOnlySet<string> DetectHotFiles(IEnumerable<string> filePaths)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in filePaths)
        {
            if (IsHotFile(path))
                result.Add(path);
        }
        return result;
    }

    private static string GetFileName(string normalizedPath)
    {
        var idx = normalizedPath.LastIndexOf('/');
        return idx < 0 ? normalizedPath : normalizedPath[(idx + 1)..];
    }

    private static string GetExtension(string fileName)
    {
        var idx = fileName.LastIndexOf('.');
        return idx < 0 ? string.Empty : fileName[idx..];
    }

    private bool IsHotByDirectory(string normalizedPath)
    {
        var parts = normalizedPath.Split('/');
        foreach (var part in parts)
        {
            if (_hotDirectoryKeywords.Contains(part))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检查路径是否在排除目录中（bin/obj/.vs/node_modules 等编译/缓存目录）
    /// </summary>
    private static bool IsExcludedPath(string normalizedPath)
    {
        var parts = normalizedPath.Split('/');
        foreach (var part in parts)
        {
            if (ExcludedDirectories.Contains(part))
                return true;
        }
        return false;
    }

    private static bool IsHotByName(string fileName, string extension)
    {
        if (fileName.StartsWith("I", StringComparison.Ordinal) &&
            extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) &&
            fileName.Length > 1 &&
            char.IsUpper(fileName[1]))
            return true;

        if (fileName.Contains("Enum", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("Constant", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("Base", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("Abstract", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fileName.Equals("__init__.py", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("index.ts", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("index.js", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("package-info.java", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("mod.go", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private bool IsHotByConfig(string extension, string fileName)
    {
        if (_configExtensions.Contains(extension))
            return true;

        if (fileName.StartsWith("settings.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("config.", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private bool IsHotByExtraPattern(string normalizedPath, string fileName)
    {
        foreach (var pattern in _extraPatterns)
        {
            if (normalizedPath.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
