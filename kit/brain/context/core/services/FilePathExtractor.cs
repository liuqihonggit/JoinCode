namespace Core.Context;

/// <summary>
/// 文件路径提取器，从文本中识别 Windows 绝对路径、Unix 风格路径和常见代码文件名
/// </summary>
public static class FilePathExtractor {
    /// <summary>
    /// 从消息文本中提取文件路径，按出现顺序去重返回
    /// </summary>
    /// <param name="message">待解析的文本</param>
    /// <returns>识别到的文件路径列表</returns>
    public static List<string> ExtractFilePaths(string message) {
        ArgumentNullException.ThrowIfNull(message);
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(message, @"[A-Za-z]:[\\/][^\s""'<>|]+")) {
            if (seen.Add(match.Value)) paths.Add(match.Value);
        }

        foreach (Match match in Regex.Matches(message,
            @"(?:~?[/\\]|\.{1,2}[/\\]|[a-zA-Z][\w]*[/\\])[\w./\\\-]+\.[a-zA-Z]{1,6}")) {
            if (seen.Add(match.Value)) paths.Add(match.Value);
        }

        foreach (Match match in Regex.Matches(message,
            @"(?<![\w./\\])[\w][\w.\-]*\.(?:cs|csproj|sln|json|xml|yaml|yml|md|py|js|ts|tsx|jsx|java|go|rs|rb|php|swift|kt|c|cpp|h|hpp)(?![\w])",
            RegexOptions.IgnoreCase)) {
            if (seen.Add(match.Value)) paths.Add(match.Value);
        }

        return paths;
    }
}