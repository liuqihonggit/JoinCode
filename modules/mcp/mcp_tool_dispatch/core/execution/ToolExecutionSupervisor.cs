namespace McpToolRegistry;

/// <summary>
/// 执行后异步监督器 — 权限判定发生在执行前，本类补上“已经执行完但结果可疑”的事后检查：
/// 扫描工具输出里的密钥特征，命中时不阻断（结果已经返回），而是登记一条待复核票据交给
/// 前台收件箱；后台任务不会因为事后发现风险而挂起。
/// <para>只保存规则标签（如 GitHub PAT），不保存或回传命中的密钥原文。</para>
/// </summary>
internal static class ToolExecutionSupervisor {
    /// <summary>单次扫描的最大字符数 — 大文件读取/命令输出只扫前 64K，避免事后监督拖慢工具完成。</summary>
    private const int MaxScanChars = 64 * 1024;

    /// <summary>
    /// 检查一次已完成工具调用的输出。
    /// </summary>
    /// <param name="result">工具结果（错误结果也检查：报错里可能带出密钥）。</param>
    /// <returns>命中时返回可展示的复核原因；未命中返回 null。</returns>
    internal static string? Inspect(ToolResult result) {
        if (result.Content.Count == 0) {
            return null;
        }

        var builder = new StringBuilder(Math.Min(MaxScanChars, 4096));
        foreach (var content in result.Content) {
            if (content.Type != ToolContentType.Text || string.IsNullOrEmpty(content.Text)) {
                continue;
            }
            var remaining = MaxScanChars - builder.Length;
            if (remaining <= 0) {
                break;
            }
            builder.Append(content.Text.AsSpan(0, Math.Min(remaining, content.Text.Length)));
            builder.Append('\n');
        }
        if (builder.Length == 0) {
            return null;
        }

        var matches = SecurityPatterns.ScanForSecretMatches(builder.ToString());
        if (matches.Count == 0) {
            return null;
        }

        var labels = string.Join("、", matches.Select(match => match.Label).Distinct(StringComparer.Ordinal));
        return $"执行后监控：工具输出疑似包含凭据（{labels}）。结果已返回，请复核是否应保留在会话/日志中，并考虑轮换密钥。";
    }
}
