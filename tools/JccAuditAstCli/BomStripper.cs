namespace JccAuditCli;

/// <summary>
/// UTF-8 BOM 移除器：扫描指定目录下所有 .cs 文件，移除 UTF-8 BOM 头（0xEF 0xBB 0xBF）
/// 用于统一源码编码格式，避免 BOM 导致的 CS0234/编码异常等问题
/// </summary>
public static class BomStripper
{
    /// <summary>
    /// UTF-8 BOM 标记字节序列：0xEF 0xBB 0xBF
    /// </summary>
    public static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// 排除目录名（路径任一段匹配则跳过）：bin/obj/.xxx/.git/.vs/artifacts/node_modules
    /// </summary>
    public static readonly string[] ExcludedDirectories = new[] { "bin", "obj", ".xxx", ".git", ".vs", "artifacts", "node_modules" };

    /// <summary>
    /// 排除文件后缀/模式：.Designer.cs / .Generated.cs / .g.cs
    /// </summary>
    public static readonly string[] ExcludedFilePatterns = new[] { ".Designer.cs", ".Generated.cs", ".g.cs" };

    /// <summary>
    /// --skip-tests 时排除的路径标记
    /// </summary>
    public static readonly string[] ExcludedTestMarkers = new[] { "\\tests\\", "/tests/", "MockServer", ".Tests.", ".E2E.", ".Benchmarks." };

    /// <summary>
    /// 扫描指定目录下所有 .cs 文件，移除 UTF-8 BOM
    /// </summary>
    /// <param name="rootPath">扫描根目录</param>
    /// <param name="dryRun">仅预览，不实际写入文件</param>
    /// <param name="skipTests">是否跳过测试项目</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>BOM 移除报告</returns>
    public static BomStripReport Strip(
        string rootPath, bool dryRun = false, bool skipTests = false,
        CancellationToken ct = default)
    {
        var rootDir = Path.GetFullPath(rootPath);
        if (!Directory.Exists(rootDir))
        {
            throw new ArgumentException($"[GEN065] 目录不存在: {rootDir}");
        }

        var detectedFiles = new List<BomStripEntry>();
        var totalFiles = 0;
        var skippedFiles = 0;
        var scannedFiles = 0;
        var withBomCount = 0;
        var strippedCount = 0;

        foreach (var filePath in Directory.EnumerateFiles(rootDir, "*.cs", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            totalFiles++;

            if (ShouldSkipFile(filePath, skipTests))
            {
                skippedFiles++;
                continue;
            }

            scannedFiles++;

            if (!HasUtf8Bom(filePath))
                continue;

            withBomCount++;
            var relativePath = Path.GetRelativePath(rootDir, filePath);

            detectedFiles.Add(new BomStripEntry
            {
                FilePath = relativePath,
                FullPath = filePath,
            });

            if (!dryRun)
            {
                RemoveBomFromFile(filePath);
                strippedCount++;
            }
        }

        return new BomStripReport
        {
            RootPath = rootDir,
            Timestamp = DateTime.UtcNow,
            TotalCsFiles = totalFiles,
            SkippedFiles = skippedFiles,
            ScannedFiles = scannedFiles,
            WithBomCount = withBomCount,
            StrippedCount = strippedCount,
            DryRun = dryRun,
            SkipTests = skipTests,
            Files = detectedFiles,
        };
    }

    /// <summary>
    /// 检测文件是否以 UTF-8 BOM 开头
    /// 用字节级读取，避免 StreamReader 自动吞掉 BOM
    /// </summary>
    public static bool HasUtf8Bom(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length < Utf8Bom.Length)
            return false;

        Span<byte> header = stackalloc byte[Utf8Bom.Length];
        var read = stream.Read(header);
        if (read < Utf8Bom.Length)
            return false;

        return header[0] == Utf8Bom[0] && header[1] == Utf8Bom[1] && header[2] == Utf8Bom[2];
    }

    /// <summary>
    /// 从文件中移除 UTF-8 BOM：读取全部字节，跳过前3字节，覆盖写回
    /// 用 FileShare.ReadWrite 避免跨进程读-写冲突
    /// </summary>
    private static void RemoveBomFromFile(string filePath)
    {
        byte[] allBytes;
        using (var readStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            allBytes = new byte[readStream.Length];
            var totalRead = 0;
            while (totalRead < allBytes.Length)
            {
                var n = readStream.Read(allBytes, totalRead, allBytes.Length - totalRead);
                if (n <= 0) break;
                totalRead += n;
            }
        }

        if (allBytes.Length < Utf8Bom.Length)
            return;

        if (allBytes[0] != Utf8Bom[0] || allBytes[1] != Utf8Bom[1] || allBytes[2] != Utf8Bom[2])
            return;

        using var writeStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writeStream.Write(allBytes, Utf8Bom.Length, allBytes.Length - Utf8Bom.Length);
        writeStream.Flush();
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
