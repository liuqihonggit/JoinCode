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
    /// 需要清理的敏感环境变量 — 对齐 TS GHA_SUBPROCESS_SCRUB
    /// 在 CI/GitHub Actions 环境中防止 prompt-injection 通过 shell 扩展窃取密钥
    /// </summary>
    private static readonly FrozenSet<string> SensitiveEnvVars = new[]
    {
        "ANTHROPIC_API_KEY",
        "JCC_OAUTH_TOKEN",
        "ANTHROPIC_AUTH_TOKEN",
        "ANTHROPIC_FOUNDRY_API_KEY",
        "ANTHROPIC_CUSTOM_HEADERS",
        "OTEL_EXPORTER_OTLP_HEADERS",
        "OTEL_EXPORTER_OTLP_LOGS_HEADERS",
        "OTEL_EXPORTER_OTLP_METRICS_HEADERS",
        "OTEL_EXPORTER_OTLP_TRACES_HEADERS",
        "AWS_SECRET_ACCESS_KEY",
        "AWS_SESSION_TOKEN",
        "AWS_BEARER_TOKEN_BEDROCK",
        "GOOGLE_APPLICATION_CREDENTIALS",
        "AZURE_CLIENT_SECRET",
        "AZURE_CLIENT_CERTIFICATE_PATH",
        "ACTIONS_ID_TOKEN_REQUEST_TOKEN",
        "ACTIONS_ID_TOKEN_REQUEST_URL",
        "ACTIONS_RUNTIME_TOKEN",
        "ACTIONS_RUNTIME_URL",
        "ALL_INPUTS",
        "OVERRIDE_GITHUB_TOKEN",
        "DEFAULT_WORKFLOW_TOKEN",
        "SSH_SIGNING_KEY",
        "GITHUB_TOKEN",
        "OPENAI_API_KEY",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 是否启用环境变量清理
    /// </summary>
    public static bool IsScrubbingEnabled
        => Environment.GetEnvironmentVariable(ScrubEnvVar) is "1" or "true";

    /// <summary>
    /// 从 ProcessStartInfo.EnvironmentVariables 中移除敏感变量 — 对齐 TS subprocessEnv
    /// TS 源码从 process.env 副本中删除敏感项，CS 直接在 psi.EnvironmentVariables 上操作
    /// 因为 psi.EnvironmentVariables 默认继承当前进程环境，只需删除不需要的
    /// </summary>
    public static void ScrubProcessEnvironment(ProcessStartInfo psi)
    {
        if (!IsScrubbingEnabled) return;

        var keysToRemove = new List<string>();
        foreach (string? key in psi.EnvironmentVariables.Keys)
        {
            if (key is null) continue;

            if (IsSensitiveKey(key))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            psi.EnvironmentVariables.Remove(key);
        }
    }

    /// <summary>
    /// 从环境变量字典中移除敏感变量 — 供 IProcessService 路径使用
    /// 对齐 TS subprocessEnv: 从 process.env 副本中删除敏感项后返回
    /// </summary>
    public static Dictionary<string, string> ScrubDictionaryEnv(Dictionary<string, string> env)
    {
        if (!IsScrubbingEnabled) return env;

        var keysToRemove = new List<string>();
        foreach (var key in env.Keys)
        {
            if (IsSensitiveKey(key))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            env.Remove(key);
        }

        return env;
    }

    private static bool IsSensitiveKey(string key)
    {
        foreach (var sensitive in SensitiveEnvVars)
        {
            if (key.Equals(sensitive, StringComparison.OrdinalIgnoreCase)
                || key.Equals($"INPUT_{sensitive}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
