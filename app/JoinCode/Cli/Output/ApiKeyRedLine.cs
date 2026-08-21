namespace JoinCode.Cli.Output;

/// <summary>
/// 密钥红线检查器 — 对齐架构指南安全设计
/// 核心原则: 禁止在命令行参数中传递 API Key（会被 ps/top/进程列表暴露）
/// 正确做法: 使用供应商专属环境变量（如 OPENAI_API_KEY=xxx）或 auth.json
/// </summary>
public static class ApiKeyRedLine
{
    /// <summary>
    /// 检查命令行参数是否包含 API Key 模式
    /// 匹配: --api-key, --key, --token, --secret 等参数名
    /// 以及包含 sk-、sk_live、ghp_、gho_ 等常见 Key 前缀的值
    /// </summary>
    /// <returns>如果检测到密钥泄露风险返回错误描述，否则 null</returns>
    public static string? CheckArgsForSecrets(string[] args)
    {
        if (args is null || args.Length == 0)
            return null;

        // 危险参数名模式
        var dangerousArgPatterns = new[]
        {
            "--api-key", "--apikey", "--key", "--token", "--secret",
            "--access-key", "--secret-key", "--private-key",
        };

        // 常见 Key 前缀
        var keyPrefixes = new[]
        {
            "sk-", "sk_live_", "sk_test_",
            "ghp_", "gho_", "github_pat_",
            "AKIA", // AWS Access Key ID
            "eyJ", // JWT token
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLowerInvariant();

            // 检查参数名
            foreach (var pattern in dangerousArgPatterns)
            {
                if (arg == pattern || arg.StartsWith(pattern + "="))
                {
                    return $"检测到命令行参数 '{args[i]}' 可能包含 API Key。"
                         + "禁止在命令行中传递密钥（会被 ps/top/进程列表暴露）。"
                         + "请使用供应商专属环境变量（如 OPENAI_API_KEY=xxx），或在 ~/.jcc/auth.json 中配置。";
                }
            }

            // 检查参数值
            if (i + 1 < args.Length && args[i].StartsWith('-') && !args[i + 1].StartsWith('-'))
            {
                var value = args[i + 1];
                foreach (var prefix in keyPrefixes)
                {
                    if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"检测到命令行参数值 '{value[..Math.Min(8, value.Length)]}...' 可能是 API Key。"
                             + "禁止在命令行中传递密钥。"
                             + "请使用供应商专属环境变量（如 OPENAI_API_KEY=xxx），或在 ~/.jcc/auth.json 中配置。";
                    }
                }
            }
        }

        return null;
    }
}
