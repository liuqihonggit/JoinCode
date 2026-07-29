
namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PowerShell Git 内部路径安全检查。
/// 防止通过 Git 武器化实现沙箱逃逸的两个向量：
/// 1. Bare-repo 攻击: 如果 cwd 包含 HEAD + objects/ + refs/ 但没有有效 .git/HEAD，
///    Git 将 cwd 视为 bare 仓库并从 cwd 执行 hooks。
/// 2. Git-internal 写入 + git: 复合命令创建 HEAD/objects/refs/hooks/ 然后运行 git —
///    git 子命令执行新创建的恶意 hooks。
/// 对齐 TS: src/tools/PowerShellTool/gitSafety.ts
/// </summary>
public static partial class PsGitSafety
{
    /// <summary>
    /// Git 内部路径前缀（bare-repo 攻击向量）
    /// </summary>
    private static readonly string[] GitInternalPrefixes = ["head", "objects", "refs", "hooks"];

    /// <summary>
    /// 判断参数（原始 PS 参数文本）是否解析为 cwd 中的 git-internal 路径。
    /// 覆盖 bare-repo 路径（hooks/, refs/）和标准仓库路径（.git/hooks/, .git/config）。
    /// </summary>
    public static bool IsGitInternalPathPS(string arg, string cwd)
    {
        if (string.IsNullOrEmpty(arg)) return false;

        var n = ResolveCwdReentry(NormalizeGitPathArg(arg), cwd);
        if (MatchesGitInternalPrefix(n)) return true;

        // 安全检查: 前导 ../ 或绝对路径，解析回 cwd 内部
        if (n.StartsWith("../") || n.StartsWith("/") || (n.Length >= 2 && n[1] == ':'))
        {
            var rel = ResolveEscapingPathToCwdRelative(n, cwd);
            if (rel != null && MatchesGitInternalPrefix(rel)) return true;
        }

        return false;
    }

    /// <summary>
    /// 判断参数是否解析为 .git/ 内部的路径（标准仓库元数据目录）。
    /// 与 IsGitInternalPathPS 不同，不匹配 bare-repo 风格的根级 hooks/, refs/ 等
    /// — 这些是常见的项目目录名。
    /// </summary>
    public static bool IsDotGitPathPS(string arg, string cwd)
    {
        if (string.IsNullOrEmpty(arg)) return false;

        var n = ResolveCwdReentry(NormalizeGitPathArg(arg), cwd);
        if (MatchesDotGitPrefix(n)) return true;

        if (n.StartsWith("../") || n.StartsWith("/") || (n.Length >= 2 && n[1] == ':'))
        {
            var rel = ResolveEscapingPathToCwdRelative(n, cwd);
            if (rel != null && MatchesDotGitPrefix(rel)) return true;
        }

        return false;
    }

    /// <summary>
    /// 规范化 PS 参数文本为 Git 路径匹配的标准形式。
    /// 顺序重要: 结构性剥离优先（冒号绑定参数、引号、反引号转义、provider前缀、
    /// 驱动器相对前缀），然后 NTFS 逐组件尾随剥离（空格始终; 点仅在非 ./.. 时），
    /// 然后 posix.normalize（解析 .., ., //），最后小写。
    /// </summary>
    internal static string NormalizeGitPathArg(string arg)
    {
        var s = arg;

        // 规范化参数前缀: 短划线字符和正斜杠（PS 5.1）
        // /Path:hooks/pre-commit → 提取冒号绑定值
        if (s.Length > 0 && (IsDashChar(s[0]) || s[0] == '/'))
        {
            var c = s.IndexOf(':', 1);
            if (c > 0) s = s[(c + 1)..];
        }

        // 去除引号
        s = StripSurroundingQuotes(s);

        // 去除反引号转义
        s = s.Replace("`", "");

        // PS provider 限定路径: FileSystem::hooks/pre-commit → hooks/pre-commit
        var fsIdx = s.IndexOf("FileSystem::", StringComparison.OrdinalIgnoreCase);
        if (fsIdx >= 0)
        {
            s = s[(fsIdx + "FileSystem::".Length)..];
        }

        // 驱动器相对 C:foo（冒号后无分隔符）是 cwd 相对路径
        // C:\foo（有分隔符）是绝对路径，不应匹配
        if (s.Length >= 2 && char.IsLetter(s[0]) && s[1] == ':' &&
            s.Length > 2 && s[2] != '/' && s[2] != '\\')
        {
            s = s[2..];
        }

        s = s.Replace('\\', '/');

        // Win32 CreateFileW 逐组件: 迭代剥离尾随空格，然后尾随点
        var components = s.Split('/');
        for (var i = 0; i < components.Length; i++)
        {
            var c = components[i];
            if (string.IsNullOrEmpty(c)) continue;

            string prev;
            do
            {
                prev = c;
                c = c.TrimEnd(' ');
                if (c == "." || c == "..")
                {
                    c = prev.TrimEnd(' '); // 保留 . 和 .. 不剥离点
                    break;
                }
                c = c.TrimEnd('.');
            } while (c != prev);

            components[i] = string.IsNullOrEmpty(c) ? "." : c;
        }

        s = string.Join('/', components);

        // posix.normalize 等价: 解析 .., ., //
        s = PosixNormalize(s);
        if (s.StartsWith("./")) s = s[2..];

        return s.ToLowerInvariant();
    }

    /// <summary>
    /// 如果规范化路径以 ../&lt;cwd-basename&gt;/ 开头，它通过父目录重新进入 cwd —
    /// 解析为 cwd 相对形式。
    /// </summary>
    private static string ResolveCwdReentry(string normalized, string cwd)
    {
        if (!normalized.StartsWith("../")) return normalized;

        var cwdBase = Path.GetFileName(cwd)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(cwdBase)) return normalized;

        var prefix = $"../{cwdBase}/";
        var s = normalized;
        while (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            s = s[prefix.Length..];
        }

        if (s == $"../{cwdBase}") return ".";

        return s;
    }

    /// <summary>
    /// 解析逃逸 cwd 的路径（前导 ../ 或绝对路径），检查是否落回 cwd 内部。
    /// 如果是，剥离 cwd 前缀返回 cwd 相对路径用于前缀匹配。
    /// 如果落在 cwd 外部，返回 null（真正外部路径 — 由 path-validation 处理）。
    /// </summary>
    private static string? ResolveEscapingPathToCwdRelative(string n, string cwd)
    {
        try
        {
            var abs = Path.GetFullPath(Path.Combine(cwd, n.Replace('/', '\\')));
            var cwdWithSep = cwd.EndsWith('\\') ? cwd : cwd + '\\';

            if (string.Equals(abs, cwd, StringComparison.OrdinalIgnoreCase)) return ".";
            if (!abs.StartsWith(cwdWithSep, StringComparison.OrdinalIgnoreCase)) return null;

            var relative = abs[cwdWithSep.Length..].Replace('\\', '/').ToLowerInvariant();
            return relative;
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesGitInternalPrefix(string n)
    {
        if (n == "head" || SecurityPatterns.MatchesVcsInternalPath(n)) return true;
        if (n.StartsWith(".git/") || GitShortNameRegex().IsMatch(n)) return true;

        foreach (var p in GitInternalPrefixes)
        {
            if (p == "head") continue;
            if (n == p || n.StartsWith(p + "/")) return true;
        }

        return false;
    }

    private static bool MatchesDotGitPrefix(string n)
    {
        // VcsInternal 分类包含 .git/.git/**/.svn/.hg 模式
        return SecurityPatterns.MatchesVcsInternalPath(n);
    }

    /// <summary>
    /// 判断路径段是否为版本控制内部路径 — 使用 SensitiveFilePattern.VcsInternal 分类
    /// </summary>
    public static bool IsVcsInternalPath(string n)
    {
        return SecurityPatterns.MatchesVcsInternalPath(n);
    }

    private static bool IsDashChar(char c) =>
        c == '\u2013' || // en-dash –
        c == '\u2014' || // em-dash —
        c == '\u2015' || // horizontal bar ―
        c == '-';

    private static string StripSurroundingQuotes(string s)
    {
        if (s.Length >= 2 &&
            ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
        {
            return s[1..^1];
        }
        return s;
    }

    /// <summary>
    /// 简化的 posix.normalize: 解析 .., ., 重复 //
    /// </summary>
    private static string PosixNormalize(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();

        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
            else
            {
                stack.Add(part);
            }
        }

        var result = string.Join('/', stack);
        if (path.StartsWith('/')) result = "/" + result;
        return string.IsNullOrEmpty(result) ? "." : result;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^git~\d+($|/)")]
    private static partial System.Text.RegularExpressions.Regex GitShortNameRegex();
}
