namespace McpToolDispatch;

/// <summary>
/// GitHub Run 详情 Summarizer — 把 run 详情 JSON 转人类可读文本(gh 风格简洁输出)
/// <para>三档输出: 0=gh风格人类可读(默认) 1=精简JSON 2=完整JSON</para>
/// </summary>
internal static class GitHubRunViewSummarizer {
    /// <summary>
    /// 把 run 详情 JSON 转人类可读文本 — gh 风格简洁输出
    /// </summary>
    public static string SummarizeRunView(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return json;
            var root = doc.RootElement;
            var sb = new StringBuilder(512);

            var status = GetString(root, "status");
            var conclusion = GetString(root, "conclusion");
            var symbol = GitHubRunFormatHelper.GetStatusSymbol(status, conclusion);
            var title = GetString(root, "display_title") ?? GetString(root, "name") ?? "(unknown)";
            var runNumber = TryGetInt(root, "run_number");
            sb.Append(symbol).Append(' ').Append(title);
            if (runNumber is int n) sb.Append(" · ").Append(n);
            sb.AppendLine();

            var parts = new List<string>(4);
            var evt = GetString(root, "event");
            var branch = GetString(root, "head_branch");
            var sha = GetString(root, "head_sha");
            if (!string.IsNullOrEmpty(evt)) parts.Add($"Event: {evt}");
            if (!string.IsNullOrEmpty(branch)) parts.Add($"Branch: {branch}");
            if (!string.IsNullOrEmpty(sha)) parts.Add($"SHA: {sha[..Math.Min(7, sha.Length)]}");
            if (parts.Count > 0) sb.AppendLine(string.Join("  ", parts));

            parts.Clear();
            if (!string.IsNullOrEmpty(status)) parts.Add($"Status: {status}");
            if (!string.IsNullOrEmpty(conclusion)) parts.Add($"Conclusion: {conclusion}");
            if (parts.Count > 0) sb.AppendLine(string.Join("  ", parts));

            var id = GetId(root);
            var elapsed = GitHubRunFormatHelper.FormatElapsed(GetString(root, "created_at"), GetString(root, "updated_at"));
            parts.Clear();
            if (!string.IsNullOrEmpty(id)) parts.Add($"ID: {id}");
            if (!string.IsNullOrEmpty(elapsed)) parts.Add($"Elapsed: {elapsed}");
            if (parts.Count > 0) sb.AppendLine(string.Join("  ", parts));

            var url = GetString(root, "html_url");
            if (!string.IsNullOrEmpty(url)) sb.AppendLine($"URL: {url}");

            return sb.ToString().TrimEnd();
        } catch {
            return json;
        }
    }

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static int? TryGetInt(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v) ? v : null;

    private static string? GetId(JsonElement obj)
        => obj.TryGetProperty("id", out var prop) ? prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.GetRawText() : null;
}

/// <summary>
/// GitHub Run 格式化共享 helper — 状态符号 + 时间跨度
/// </summary>
internal static class GitHubRunFormatHelper {
    /// <summary>
    /// 状态符号映射 — ✓成功 ✗失败 ⊘取消 *进行中 …排队
    /// </summary>
    public static string GetStatusSymbol(string? status, string? conclusion) {
        if (string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase)) return "*";
        if (string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase)) return "…";
        if (string.Equals(conclusion, "success", StringComparison.OrdinalIgnoreCase)) return "✓";
        if (string.Equals(conclusion, "failure", StringComparison.OrdinalIgnoreCase)) return "✗";
        if (string.Equals(conclusion, "cancelled", StringComparison.OrdinalIgnoreCase)) return "⊘";
        if (string.Equals(conclusion, "timed_out", StringComparison.OrdinalIgnoreCase)) return "⏱";
        if (string.Equals(conclusion, "skipped", StringComparison.OrdinalIgnoreCase)) return "→";
        return "-";
    }

    /// <summary>
    /// 格式化时间跨度 — 从 ISO 8601 创建/更新时间计算 elapsed
    /// </summary>
    public static string? FormatElapsed(string? createdAt, string? updatedAt) {
        if (string.IsNullOrEmpty(createdAt) || string.IsNullOrEmpty(updatedAt)) return null;
        if (!DateTimeOffset.TryParse(createdAt, out var start)) return null;
        if (!DateTimeOffset.TryParse(updatedAt, out var end)) return null;
        var span = end - start;
        if (span.TotalSeconds < 0) return null;
        if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m{(int)span.Seconds}s";
        return $"{(int)span.TotalHours}h{(int)span.Minutes}m";
    }

    /// <summary>
    /// 获取 JSON 字符串属性值
    /// </summary>
    public static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    /// <summary>
    /// 获取 id 属性值(兼容数字和字符串两种类型)
    /// </summary>
    public static string? GetId(JsonElement obj)
        => obj.TryGetProperty("id", out var prop) ? prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.GetRawText() : null;
}
