namespace Core.Configuration;

/// <summary>
/// 规则前置元数据解析器 — 从规则原始内容中解析 YAML 风格的前置元数据(alwaysApply/globs/description)
/// </summary>
public static class RuleFrontmatterParser {
    /// <summary>
    /// 解析规则原始内容,分离前置元数据与正文
    /// </summary>
    /// <param name="rawContent">规则原始内容,可能包含以 --- 分隔的前置元数据</param>
    /// <returns>元组: 正文内容、是否总是应用、glob 匹配模式、描述</returns>
    public static (string Content, bool AlwaysApply, string Globs, string Description) Parse(string rawContent) {
        if (!rawContent.StartsWith("---", StringComparison.Ordinal)) {
            return (rawContent, false, string.Empty, string.Empty);
        }

        var endIdx = rawContent.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIdx < 0) {
            return (rawContent, false, string.Empty, string.Empty);
        }

        var frontmatter = rawContent[3..endIdx].Trim();
        var body = rawContent[(endIdx + 3)..].TrimStart('\n', '\r');

        var alwaysApply = false;
        var globs = string.Empty;
        var description = string.Empty;

        foreach (var line in frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            var trimmed = line.Trim();
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) continue;

            var key = trimmed[..colonIdx].Trim();
            var value = trimmed[(colonIdx + 1)..].Trim();

            if (key.Equals("alwaysApply", StringComparison.OrdinalIgnoreCase)
                || key.Equals("always-apply", StringComparison.OrdinalIgnoreCase)) {
                alwaysApply = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            } else if (key.Equals("globs", StringComparison.OrdinalIgnoreCase)) {
                globs = value.Trim('"', '\'');
            } else if (key.Equals("description", StringComparison.OrdinalIgnoreCase)) {
                description = value.Trim('"', '\'');
            }
        }

        return (body, alwaysApply, globs, description);
    }
}