
namespace IO;

/// <summary>
/// 轻量级 .gitignore 匹配器，对齐 ripgrep 的 .gitignore 处理行为
/// 支持：否定模式(!)、目录模式(trailing /)、双星号通配符(**)、字符范围([a-z])
/// </summary>
internal sealed class GitignoreMatcher
{
    private readonly GitignoreRule[] _rules;

    private GitignoreMatcher(GitignoreRule[] rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// 从 .gitignore 文件内容创建匹配器
    /// </summary>
    public static GitignoreMatcher Parse(string content)
    {
        var rules = new List<GitignoreRule>();
        var lines = content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine;

            // 跳过空行和注释
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            // 去除行尾空格（未转义的空格）
            line = TrimTrailingSpaces(line);

            // 检测否定模式
            var negated = false;
            if (line.StartsWith('!'))
            {
                negated = true;
                line = line[1..];
            }

            // 去除转义的 # 和 !
            line = line.Replace("\\#", "#").Replace("\\!", "!");

            // 检测目录模式（以 / 结尾）
            var directoryOnly = line.EndsWith('/');
            if (directoryOnly)
            {
                line = line[..^1];
            }

            // 处理前导 / — 锚定到 gitignore 文件所在目录
            var anchored = false;
            if (line.StartsWith('/'))
            {
                anchored = true;
                line = line[1..];
            }
            else if (line.Contains('/'))
            {
                // 包含中间 / 的模式也锚定（如 foo/bar）
                anchored = true;
            }

            // 如果模式中间没有 / 且不以 / 开头，则匹配任意层级
            // 即 foo 同时匹配 foo 和 a/b/foo

            rules.Add(new GitignoreRule(line, negated, directoryOnly, anchored));
        }

        return new GitignoreMatcher(rules.ToArray());
    }

    /// <summary>
    /// 从 .gitignore 文件路径创建匹配器
    /// </summary>
    public static GitignoreMatcher? FromFile(string gitignorePath, IFileSystem fs)
    {
        try
        {
            if (!fs.FileExists(gitignorePath))
                return null;

            var content = fs.ReadAllText(gitignorePath);
            return Parse(content);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 判断给定相对路径是否被忽略
    /// </summary>
    /// <param name="relativePath">相对于 .gitignore 文件所在目录的相对路径</param>
    /// <param name="isDirectory">是否为目录</param>
    /// <returns>true 表示被忽略</returns>
    public bool IsIgnored(string relativePath, bool isDirectory = false)
    {
        // 规范化路径分隔符
        var normalizedPath = relativePath.Replace('\\', '/');

        // 按规则顺序应用（后出现的规则优先级更高）
        var ignored = false;
        foreach (var rule in _rules)
        {
            // 目录模式只匹配目录
            if (rule.DirectoryOnly && !isDirectory)
                continue;

            if (Matches(rule, normalizedPath, isDirectory))
            {
                ignored = !rule.Negated;
            }
        }

        return ignored;
    }

    /// <summary>
    /// 判断给定路径是否匹配规则
    /// </summary>
    private static bool Matches(GitignoreRule rule, string path, bool isDirectory)
    {
        var pattern = rule.Pattern;

        if (rule.Anchored)
        {
            // 锚定模式：从根目录开始匹配
            return GlobMatch(pattern, path);
        }
        else
        {
            // 非锚定模式：匹配任意层级
            // 先尝试直接匹配
            if (GlobMatch(pattern, path))
                return true;

            // 再尝试匹配路径的任意后缀
            // 如模式 "foo" 匹配 "a/b/foo" 和 "a/foo"
            var lastSlash = path.LastIndexOf('/');
            while (lastSlash >= 0)
            {
                var suffix = path[(lastSlash + 1)..];
                if (GlobMatch(pattern, suffix))
                    return true;
                lastSlash = path.LastIndexOf('/', lastSlash - 1);
            }

            return false;
        }
    }

    /// <summary>
    /// 简单的 glob 模式匹配，支持 * ? ** 和字符范围
    /// </summary>
    private static bool GlobMatch(string pattern, string path)
    {
        // 处理 ** 模式
        var doubleStarIndex = pattern.IndexOf("**", StringComparison.Ordinal);
        if (doubleStarIndex >= 0)
        {
            return MatchDoubleStar(pattern, path, doubleStarIndex);
        }

        // 逐字符匹配
        return MatchSimple(pattern, 0, path, 0);
    }

    /// <summary>
    /// 处理 ** 通配符的匹配
    /// </summary>
    private static bool MatchDoubleStar(string pattern, string path, int doubleStarIndex)
    {
        // ** 匹配零个或多个目录
        var prefix = pattern[..doubleStarIndex];
        var suffix = pattern[(doubleStarIndex + 2)..];

        // 前缀必须匹配路径开头
        if (prefix.Length > 0)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            path = path[prefix.Length..];
        }

        // suffix 可以为空（** 匹配一切）
        if (suffix.Length == 0)
            return true;

        // **/suffix — suffix 匹配路径的任意后缀
        // 去除 suffix 开头的 /
        if (suffix.StartsWith('/'))
            suffix = suffix[1..];

        // 尝试从每个 / 位置匹配 suffix
        var pos = 0;
        while (true)
        {
            if (MatchSimple(suffix, 0, path, pos))
                return true;
            var nextSlash = path.IndexOf('/', pos);
            if (nextSlash < 0) break;
            pos = nextSlash + 1;
        }

        return MatchSimple(suffix, 0, path, pos);
    }

    /// <summary>
    /// 简单 glob 匹配（不含 **），支持 * ? [a-z]
    /// </summary>
    private static bool MatchSimple(string pattern, int pi, string path, int si)
    {
        while (true)
        {
            if (pi >= pattern.Length)
                return si >= path.Length;

            var pc = pattern[pi];

            if (pc == '*')
            {
                // * 匹配非 / 的任意字符
                // 跳过连续的 *
                while (pi < pattern.Length && pattern[pi] == '*')
                    pi++;

                if (pi >= pattern.Length)
                    return path.IndexOf('/', si) < 0 || si >= path.Length;

                // 尝试匹配剩余模式
                for (var i = si; i <= path.Length; i++)
                {
                    if (i < path.Length && path[i] == '/')
                        break;
                    if (MatchSimple(pattern, pi, path, i))
                        return true;
                }

                return false;
            }

            if (si >= path.Length)
                return false;

            if (pc == '?')
            {
                // ? 匹配非 / 的任意单个字符
                if (path[si] == '/')
                    return false;
                pi++;
                si++;
                continue;
            }

            if (pc == '[')
            {
                // 字符范围 [a-z]
                var closeBracket = pattern.IndexOf(']', pi + 1);
                if (closeBracket < 0)
                    goto LiteralMatch;

                var charClass = pattern.AsSpan(pi + 1, closeBracket - pi - 1);
                if (!MatchCharClass(charClass, path[si]))
                    return false;

                pi = closeBracket + 1;
                si++;
                continue;
            }

        LiteralMatch:
            if (char.ToLowerInvariant(pc) != char.ToLowerInvariant(path[si]))
                return false;

            pi++;
            si++;
        }
    }

    /// <summary>
    /// 字符范围匹配
    /// </summary>
    private static bool MatchCharClass(ReadOnlySpan<char> charClass, char c)
    {
        var negate = false;
        var offset = 0;

        if (charClass.Length > 0 && charClass[0] == '^')
        {
            negate = true;
            offset = 1;
        }

        var matched = false;
        for (var i = offset; i < charClass.Length; i++)
        {
            if (i + 2 < charClass.Length && charClass[i + 1] == '-')
            {
                // 范围 [a-z]
                var low = char.ToLowerInvariant(charClass[i]);
                var high = char.ToLowerInvariant(charClass[i + 2]);
                var cc = char.ToLowerInvariant(c);
                if (cc >= low && cc <= high)
                    matched = true;
                i += 2;
            }
            else
            {
                if (char.ToLowerInvariant(charClass[i]) == char.ToLowerInvariant(c))
                    matched = true;
            }
        }

        return negate ? !matched : matched;
    }

    /// <summary>
    /// 去除行尾未转义的空格
    /// </summary>
    private static string TrimTrailingSpaces(string line)
    {
        var span = line.AsSpan();
        var end = span.Length;
        while (end > 0 && span[end - 1] == ' ')
        {
            // 检查空格是否被转义
            if (end > 1 && span[end - 2] == '\\')
                break;
            end--;
        }
        return span[..end].ToString();
    }

    private sealed record GitignoreRule(string Pattern, bool Negated, bool DirectoryOnly, bool Anchored);
}
