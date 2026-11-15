using System.Text;
using System.Text.RegularExpressions;

namespace JoinCode.CliNode.Search;

/// <summary>
/// glob 模式匹配（glob / search_files 的本地实现）。
/// <para>支持 **、*、?、[abc]、{a,b}；与 ripgrep 的 --glob 语义保持一致：
/// 不含 "/" 的模式只匹配文件名，含 "/" 的模式匹配相对路径。</para>
/// 不支持 extglob / 忽略文件语法 —— 那些交给 ripgrep，本类只服务于本地零依赖路径。
/// </summary>
internal sealed class GlobMatcher {
    private readonly Regex _regex;
    private readonly bool _basenameOnly;

    private GlobMatcher(Regex regex, bool basenameOnly) {
        _regex = regex;
        _basenameOnly = basenameOnly;
    }

    public static GlobMatcher Create(string pattern) {
        var normalized = pattern.Trim().Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        var basenameOnly = !normalized.Contains('/');
        var body = Translate(normalized);
        var options = RegexOptions.CultureInvariant;
        if (OperatingSystem.IsWindows()) options |= RegexOptions.IgnoreCase;
        return new GlobMatcher(new Regex("^" + body + "$", options), basenameOnly);
    }

    public bool IsMatch(string relativePath) {
        var candidate = relativePath.Replace('\\', '/');
        return _regex.IsMatch(_basenameOnly ? Path.GetFileName(candidate) : candidate);
    }

    private static string Translate(string pattern) {
        var output = new StringBuilder(pattern.Length * 2);
        var index = 0;
        while (index < pattern.Length) {
            var current = pattern[index];
            switch (current) {
                case '*':
                    if (index + 1 < pattern.Length && pattern[index + 1] == '*') {
                        // "**/" 允许零层目录，"**" 单独出现时跨任意层级。
                        if (index + 2 < pattern.Length && pattern[index + 2] == '/') {
                            output.Append("(?:.*/)?");
                            index += 3;
                        } else {
                            output.Append(".*");
                            index += 2;
                        }
                    } else {
                        output.Append("[^/]*");
                        index++;
                    }
                    break;
                case '?':
                    output.Append("[^/]");
                    index++;
                    break;
                case '[': {
                    var closing = pattern.IndexOf(']', index + 1);
                    if (closing < 0) {
                        output.Append(Regex.Escape("["));
                        index++;
                        break;
                    }
                    var content = pattern[(index + 1)..closing];
                    if (content.StartsWith('!')) content = "^" + content[1..];
                    output.Append('[').Append(content).Append(']');
                    index = closing + 1;
                    break;
                }
                case '{': {
                    var closing = pattern.IndexOf('}', index + 1);
                    if (closing < 0) {
                        output.Append(Regex.Escape("{"));
                        index++;
                        break;
                    }
                    var alternatives = pattern[(index + 1)..closing].Split(',');
                    output.Append("(?:");
                    for (var i = 0; i < alternatives.Length; i++) {
                        if (i > 0) output.Append('|');
                        output.Append(Translate(alternatives[i]));
                    }
                    output.Append(')');
                    index = closing + 1;
                    break;
                }
                default:
                    output.Append(Regex.Escape(current.ToString()));
                    index++;
                    break;
            }
        }
        return output.ToString();
    }
}
