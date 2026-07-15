namespace Services.Shell;

/// <summary>
/// 子进程环境变量清理 — 对齐 TS subprocessEnv
/// 在 CI/GitHub Actions 环境中清理敏感环境变量（API Key、Secret 等）
/// 由 JCC_SUBPROCESS_ENV_SCRUB 环境变量控制是否启用
/// </summary>
public static class SubprocessEnvCleaner
{
    /// <summary>
    /// 环境变量名 — 对齐 TS CLAUDE_CODE_SUBPROCESS_ENV_SCRUB
    /// </summary>
    public const string ScrubEnvVar = "JCC_SUBPROCESS_ENV_SCRUB";

    /// <summary>
    /// 需要清理的敏感环境变量前缀 — 对齐 TS scrubEnvVars
    /// </summary>
    private static readonly FrozenSet<string> SensitivePrefixes = new[]
    {
        "ANTHROPIC_API_KEY",
        "AWS_SECRET_ACCESS_KEY",
        "AWS_SESSION_TOKEN",
        "GITHUB_TOKEN",
        "OPENAI_API_KEY",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 是否启用环境变量清理
    /// </summary>
    public static bool IsScrubbingEnabled
        => Environment.GetEnvironmentVariable(ScrubEnvVar) is "1" or "true";

    /// <summary>
    /// 清理环境变量中的敏感信息 — 对齐 TS subprocessEnv
    /// 返回清理后的环境变量字典
    /// </summary>
    public static Dictionary<string, string> CleanEnvironment(IDictionary<string, string>? source = null)
    {
        var env = source is not null
            ? new Dictionary<string, string>(source, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        if (!IsScrubbingEnabled) return env;

        foreach (var prefix in SensitivePrefixes)
        {
            var keysToRemove = env.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in keysToRemove)
            {
                env.Remove(key);
            }
        }

        return env;
    }
}
