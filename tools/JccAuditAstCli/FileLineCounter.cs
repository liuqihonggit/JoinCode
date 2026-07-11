namespace JccAuditCli;

/// <summary>
/// 文件行数统计器：扫描指定目录下所有 .cs 文件，按行数降序排列
/// 用于识别需要拆分的大文件，逐步消除超长文件
/// </summary>
public static class FileLineCounter
{
    /// <summary>
    /// 默认 Top N 数量
    /// </summary>
    public const int DefaultTopN = 10;

    /// <summary>
    /// 默认行数阈值：超过此值才纳入统计
    /// </summary>
    public const int DefaultThreshold = 200;

    private static readonly string[] ExcludedDirectories = ["bin", "obj", ".xxx", ".git", ".vs", "artifacts", "node_modules"];

    /// <summary>
    /// 扫描指定目录下所有 .cs 文件，返回行数最高的 Top N
    /// </summary>
    /// <param name="rootPath">扫描根目录</param>
    /// <param name="topN">返回前 N 个（默认 10）</param>
    /// <param name="threshold">最低行数阈值（默认 200）</param>
    /// <param name="skipTests">是否跳过测试项目</param>
    /// <param name="ct">取消令牌</param>
    public static FileLineReport Scan(
        string rootPath, int topN = DefaultTopN, int threshold = DefaultThreshold,
        bool skipTests = false, CancellationToken ct = default)
    {
        var rootDir = Path.GetFullPath(rootPath);
        if (!Directory.Exists(rootDir))
        {
            throw new ArgumentException($"目录不存在: {rootDir}");
        }

        var files = new List<FileInfoEntry>();
        var totalFiles = 0;
        var skippedFiles = 0;

        foreach (var filePath in Directory.EnumerateFiles(rootDir, "*.cs", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            totalFiles++;

            if (ShouldSkipFile(filePath, skipTests))
            {
                skippedFiles++;
                continue;
            }

            var lineCount = CountLines(filePath);
            if (lineCount < threshold)
                continue;

            var relativePath = Path.GetRelativePath(rootDir, filePath);

            files.Add(new FileInfoEntry
            {
                FilePath = relativePath,
                FullPath = filePath,
                LineCount = lineCount,
            });
        }

        var topFiles = files
            .OrderByDescending(f => f.LineCount)
            .Take(topN)
            .ToList();

        return new FileLineReport
        {
            RootPath = rootDir,
            Timestamp = DateTime.UtcNow,
            TopN = topN,
            Threshold = threshold,
            TotalCsFiles = totalFiles,
            SkippedFiles = skippedFiles,
            FilesAboveThreshold = files.Count,
            Files = topFiles,
        };
    }

    /// <summary>
    /// 统计文件行数（跳过空行和纯空白行不计）
    /// </summary>
    private static int CountLines(string filePath)
    {
        var count = 0;
        using var reader = new StreamReader(filePath);
        while (reader.ReadLine() is not null)
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// 判断是否应跳过该文件
    /// </summary>
    private static bool ShouldSkipFile(string filePath, bool skipTests)
    {
        var segments = filePath.Split('\\', '/');

        foreach (var segment in segments)
        {
            if (ExcludedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        if (skipTests)
        {
            if (filePath.Contains("\\tests\\", StringComparison.Ordinal) ||
                filePath.Contains("/tests/", StringComparison.Ordinal) ||
                filePath.Contains("MockServer", StringComparison.Ordinal) ||
                filePath.Contains(".Tests.", StringComparison.Ordinal) ||
                filePath.Contains(".E2E.", StringComparison.Ordinal) ||
                filePath.Contains(".Benchmarks.", StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (filePath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".Generated.cs", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains(".g.cs", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
