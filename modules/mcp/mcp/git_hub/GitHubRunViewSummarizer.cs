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
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.RunDetailResponse);
            if (resp is null) return json;
            var sb = new StringBuilder(512);

            var status = resp.Status;
            var conclusion = resp.Conclusion;
            var symbol = GitHubRunFormatHelper.GetStatusSymbol(status, conclusion);
            var title = resp.DisplayTitle ?? resp.Name ?? "(unknown)";
            var runNumber = resp.RunNumber;
            sb.Append(symbol).Append(' ').Append(title);
            if (runNumber > 0) sb.Append(" · ").Append(runNumber);
            sb.AppendLine();

            var parts = new List<string>(4);
            var evt = resp.Event;
            var branch = resp.HeadBranch;
            var sha = resp.HeadSha;
            if (!string.IsNullOrEmpty(evt)) parts.Add($"Event: {evt}");
            if (!string.IsNullOrEmpty(branch)) parts.Add($"Branch: {branch}");
            if (!string.IsNullOrEmpty(sha)) parts.Add($"SHA: {sha[..Math.Min(7, sha.Length)]}");
            if (parts.Count > 0) sb.AppendLine(string.Join("  ", parts));

            parts.Clear();
            if (!string.IsNullOrEmpty(status)) parts.Add($"Status: {status}");
            if (!string.IsNullOrEmpty(conclusion)) parts.Add($"Conclusion: {conclusion}");
            if (parts.Count > 0) sb.AppendLine(string.Join("  ", parts));

            var id = resp.Id.ToString();
            var elapsed = GitHubRunFormatHelper.FormatElapsed(resp.CreatedAt, resp.UpdatedAt);
            parts.Clear();
            if (!string.IsNullOrEmpty(id)) parts.Add($"ID: {id}");
            if (!string.IsNullOrEmpty(elapsed)) parts.Add($"Elapsed: {elapsed}");
            if (parts.Count > 0) sb.AppendLine(string.Join("  ", parts));

            var url = resp.HtmlUrl;
            if (!string.IsNullOrEmpty(url)) sb.AppendLine($"URL: {url}");

            return sb.ToString().TrimEnd();
        } catch {
            return json;
        }
    }
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
}
