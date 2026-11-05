namespace Tools.Handlers;

/// <summary>
/// 子模块条目 — .gitmodules 中一个 [submodule] 段的解析结果。
/// </summary>
public sealed record SubmoduleEntry(string Name, string Path, string Url, string? Branch);

/// <summary>
/// .gitmodules 解析器 — gitconfig 格式逐行解析,提取子模块 path/url/branch。
/// <para>复用 <c>GitHubToolHandlers.ParseGitHubRemoteUrl</c> 将 url 转为 owner/repo。</para>
/// </summary>
public static class SubmoduleManifest {
    /// <summary>
    /// 解析 .gitmodules 文本内容,返回子模块条目列表。
    /// </summary>
    /// <param name="content">.gitmodules 文件全文(gitconfig 格式)。</param>
    /// <returns>子模块条目列表;空内容返回空列表。</returns>
    public static IReadOnlyList<SubmoduleEntry> Parse(string content) {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var entries = new List<SubmoduleEntry>();
        string? name = null;
        string? path = null;
        string? url = null;
        string? branch = null;

        foreach (var rawLine in content.AsSpan().EnumerateLines()) {
            var line = rawLine.Trim();
            if (line.IsEmpty || line[0] is '#' or ';') continue;

            if (line.StartsWith("[submodule".AsSpan())) {
                if (name is not null) {
                    entries.Add(new SubmoduleEntry(name, path ?? name, url ?? string.Empty, branch));
                }
                name = ExtractQuotedValue(line);
                path = null;
                url = null;
                branch = null;
                continue;
            }

            var eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;
            var key = line[..eqIdx].Trim().ToString();
            var value = line[(eqIdx + 1)..].Trim().ToString();

            switch (key) {
                case "path": path = value; break;
                case "url": url = value; break;
                case "branch": branch = value; break;
            }
        }

        if (name is not null) {
            entries.Add(new SubmoduleEntry(name, path ?? name, url ?? string.Empty, branch));
        }

        return entries;
    }

    private static string ExtractQuotedValue(ReadOnlySpan<char> sectionLine) {
        var start = sectionLine.IndexOf('"');
        if (start < 0) return sectionLine.ToString();
        var end = sectionLine[(start + 1)..].IndexOf('"');
        return end < 0 ? sectionLine[(start + 1)..].ToString() : sectionLine.Slice(start + 1, end).ToString();
    }
}
