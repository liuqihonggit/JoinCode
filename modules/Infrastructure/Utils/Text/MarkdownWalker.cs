namespace Core.Utils;

/// <summary>
/// Markdown遍历选项
/// </summary>
public sealed class MarkdownWalkerOptions
{
    /// <summary>
    /// 是否在遇到SKILL.md目录时停止递归
    /// 默认为true
    /// </summary>
    public bool StopAtSkillDir { get; init; } = true;

    /// <summary>
    /// 要排除的目录名称列表
    /// </summary>
    public IReadOnlyList<string> ExcludeDirs { get; init; } = new[]
    {
        ".git", ".svn", ".hg",
        "node_modules", "bin", "obj",
        ".vs", ".vscode", ".idea",
        "dist", "build", "out"
    };

    /// <summary>
    /// 技能文件名（不含扩展名）
    /// 默认为 "SKILL"
    /// </summary>
    public string SkillFileName { get; init; } = "SKILL";
}

/// <summary>
/// Markdown文件信息
/// </summary>
public sealed class MarkdownFileInfo
{
    /// <summary>
    /// 文件完整路径
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// 相对路径（相对于根目录）
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// 文件名
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// 命名空间（基于目录结构）
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// 是否为技能文件（SKILL.md）
    /// </summary>
    public required bool IsSkillFile { get; init; }

    /// <summary>
    /// 所在目录路径
    /// </summary>
    public required string DirectoryPath { get; init; }
}

/// <summary>
/// Markdown文件遍历器
/// 递归遍历目录，识别SKILL.md文件，支持命名空间追踪
/// </summary>
public sealed class MarkdownWalker
{
    private readonly MarkdownWalkerOptions _options;
    private readonly HashSet<string> _excludeDirSet;
    private readonly IFileSystem _fs;

    /// <summary>
    /// 创建Markdown遍历器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="options">遍历选项</param>
    public MarkdownWalker(IFileSystem fs, MarkdownWalkerOptions? options = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _options = options ?? new MarkdownWalkerOptions();
        _excludeDirSet = new HashSet<string>(_options.ExcludeDirs, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 遍历指定目录下的所有Markdown文件
    /// </summary>
    /// <param name="rootPath">根目录路径</param>
    /// <returns>Markdown文件信息列表</returns>
    public IReadOnlyList<MarkdownFileInfo> Walk(string rootPath)
    {
        if (!_fs.DirectoryExists(rootPath))
            throw new DirectoryNotFoundException($"目录不存在: {rootPath}");

        var results = new List<MarkdownFileInfo>();
        var rootFullPath = Path.GetFullPath(rootPath);

        WalkDirectory(rootFullPath, rootFullPath, string.Empty, results);

        return results;
    }

    /// <summary>
    /// 异步遍历指定目录下的所有Markdown文件
    /// </summary>
    /// <param name="rootPath">根目录路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Markdown文件信息列表</returns>
    public Task<IReadOnlyList<MarkdownFileInfo>> WalkAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Walk(rootPath), cancellationToken);
    }

    private void WalkDirectory(string rootDirPath, string currentDirPath, string currentNamespace, List<MarkdownFileInfo> results)
    {
        var currentDirName = _fs.GetDirectoryName(currentDirPath);
        var relativePath = DirectoryHelper.GetRelativePath(rootDirPath, currentDirPath);
        var namespacePart = BuildNamespace(currentNamespace, currentDirName);

        // 检查当前目录是否包含SKILL.md
        var skillFileName = $"{_options.SkillFileName}.md";
        var skillFilePath = Path.Combine(currentDirPath, skillFileName);
        var hasSkillFile = _fs.FileExists(skillFilePath);

        // 处理Markdown文件
        foreach (var mdFile in _fs.GetFiles(currentDirPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(mdFile);
            var isSkillFile = fileName.Equals(skillFileName, StringComparison.OrdinalIgnoreCase);

            results.Add(new MarkdownFileInfo
            {
                FullPath = mdFile,
                RelativePath = DirectoryHelper.GetRelativePath(rootDirPath, mdFile),
                FileName = fileName,
                Namespace = namespacePart,
                IsSkillFile = isSkillFile,
                DirectoryPath = currentDirPath
            });
        }

        // 如果当前目录有SKILL.md且设置了StopAtSkillDir，则不再递归子目录
        if (_options.StopAtSkillDir && hasSkillFile)
            return;

        // 递归处理子目录
        foreach (var subDirPath in _fs.GetDirectories(currentDirPath, "*", SearchOption.TopDirectoryOnly))
        {
            // 跳过排除的目录
            var subDirName = Path.GetFileName(subDirPath);
            if (_excludeDirSet.Contains(subDirName))
                continue;

            WalkDirectory(rootDirPath, subDirPath, namespacePart, results);
        }
    }

    private static string BuildNamespace(string parentNamespace, string dirName)
    {
        // 如果是根目录，使用目录名作为命名空间
        if (string.IsNullOrEmpty(parentNamespace))
            return SanitizeNamespace(dirName);

        return $"{parentNamespace}.{SanitizeNamespace(dirName)}";
    }

    private static string SanitizeNamespace(string name)
    {
        // 移除或替换不适合命名空间的字符
        var sanitized = new System.Text.StringBuilder();

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sanitized.Append(c);
            }
            else if (c == '-' || c == ' ')
            {
                sanitized.Append('_');
            }
        }

        // 确保不以数字开头
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            sanitized.Insert(0, '_');

        return sanitized.Length > 0 ? sanitized.ToString() : "_";
    }
}
