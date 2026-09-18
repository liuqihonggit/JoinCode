namespace McpToolDispatch;

/// <summary>
/// GitHub Run 缓存路径工具 — 共享给 GitHubToolHandlers 和 GitHubRunLogCache
/// </summary>
internal static class GitHubRunCachePaths
{
    /// <summary>
    /// 文件级缓存目录名 — {projectDir}/.jcc/gh_cache/,跨进程共享
    /// </summary>
    public static readonly string CacheDirName = Path.Combine(AppDataConstants.AppDataFolder, "gh_cache");

    /// <summary>
    /// 获取缓存目录路径 — {workingDir}/.jcc/gh_cache/ 或 {cwd}/.jcc/gh_cache/
    /// <para>项目级缓存,跨进程共享,24h 过期</para>
    /// </summary>
    public static string GetCacheDir(IFileSystem fs, string? workingDir)
    {
        var baseDir = string.IsNullOrWhiteSpace(workingDir) ? fs.GetCurrentDirectory() : workingDir;
        return fs.CombinePath(baseDir, CacheDirName);
    }

    /// <summary>
    /// 获取缓存文件路径 — {cacheDir}/{sanitizedRunId}_{sanitizedJobId}.{extension}
    /// </summary>
    public static string GetCacheFilePath(string cacheDir, string runId, string? jobId, string extension)
    {
        var safeRunId = SanitizeFileName(runId);
        var safeJobId = SanitizeFileName(string.IsNullOrWhiteSpace(jobId) ? "all" : jobId);
        return Path.Combine(cacheDir, $"{safeRunId}_{safeJobId}.{extension}");
    }

    /// <summary>
    /// 文件名安全化 — 移除路径分隔符和特殊字符,只保留字母数字下划线减号
    /// </summary>
    public static string SanitizeFileName(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sb.Append(c);
            else if (c == ' ')
                sb.Append('_');
        }
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }
}
