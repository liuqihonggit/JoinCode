namespace NativeTui.Core.Repl;

/// <summary>
/// 危险命令规则（S8）— 词序列 + 剩余参数谓词：HeadTokens 匹配命令前缀后，
/// Match 谓词作用于剩余词集（组合 flag/排除 flag/终结符检查），Warning 为中文警告文案。
/// 对齐基准 destructiveCommandWarning.ts 的 DestructivePattern 语义（正则 → 词法化实现，AOT 零正则开销）。
/// </summary>
/// <param name="HeadTokens">命令头部词序列（OrdinalIgnoreCase 匹配）。</param>
/// <param name="Match">剩余词集谓词（true = 命中危险模式）。</param>
/// <param name="Warning">中文警告文案（"可能…"句式）。</param>
internal sealed record DangerousRule(string[] HeadTokens, Func<string[], bool> Match, string Warning);

/// <summary>
/// 危险命令检测目录（S8）— 唯一数据源（对齐 AGENTS.md 硬编码变委托原则）：
/// 覆盖基准 destructiveCommandWarning.ts 全部 15 类模式（git 数据丢失/安全绕过、rm 递归删除、
/// 数据库 DROP/TRUNCATE/DELETE、kubectl/terraform 基础设施销毁）+ sudo 提权（任务书指定）。
/// 纯词法化匹配（分词 + 前缀 + 谓词），无正则 — 词扫描任意起点命中复合命令（cd x && rm -rf …）。
/// </summary>
public static class DangerousCommandCatalog
{
    /// <summary>规则表（顺序即优先级 — rm 递归+强制组合在前，最具体警告优先命中）。</summary>
    private static readonly DangerousRule[] Rules =
    [
        // Git — 数据丢失 / 难以逆转
        new(["git", "reset"], static rest => rest.Contains("--hard"), "可能丢弃未提交的更改"),
        new(["git", "push"], static rest => rest.Contains("--force") || rest.Contains("--force-with-lease") || rest.Contains("-f"), "可能覆盖远程历史"),
        new(["git", "clean"], static rest => HasShortFlag(rest, 'f') && !HasShortFlag(rest, 'n') && !rest.Contains("--dry-run"), "可能永久删除未跟踪文件"),
        new(["git", "checkout"], static rest => rest is ["."], "可能丢弃所有工作区更改"),
        new(["git", "restore"], static rest => rest is ["."], "可能丢弃所有工作区更改"),
        new(["git", "stash"], static rest => IsFirstWord(rest, "drop", "clear"), "可能永久移除储藏的更改"),
        new(["git", "branch"], static rest => rest.Contains("-d") || (rest.Contains("--delete") && rest.Contains("--force")), "可能强制删除分支"),

        // Git — 安全绕过
        new(["git", "commit"], static rest => rest.Contains("--no-verify"), "可能跳过安全钩子"),
        new(["git", "push"], static rest => rest.Contains("--no-verify"), "可能跳过安全钩子"),
        new(["git", "merge"], static rest => rest.Contains("--no-verify"), "可能跳过安全钩子"),
        new(["git", "commit"], static rest => rest.Contains("--amend"), "可能重写最后一次提交"),

        // 文件删除 — 组合 flag（-rf/-fr）先于单 flag
        new(["rm"], static rest => HasShortFlag(rest, 'r') && HasShortFlag(rest, 'f'), "可能递归强制删除文件"),
        new(["rm"], static rest => HasShortFlag(rest, 'r'), "可能递归删除文件"),
        new(["rm"], static rest => HasShortFlag(rest, 'f'), "可能强制删除文件"),

        // 数据库
        new(["drop"], static rest => IsFirstWord(rest, "table", "database", "schema"), "可能删除或清空数据库对象"),
        new(["truncate"], static rest => IsFirstWord(rest, "table", "database", "schema"), "可能删除或清空数据库对象"),
        new(["delete"], static rest => IsFirstWord(rest, "from") && EndsStatement(rest), "可能删除表中所有行"),

        // 基础设施
        new(["kubectl", "delete"], static _ => true, "可能删除 Kubernetes 资源"),
        new(["terraform", "destroy"], static _ => true, "可能销毁 Terraform 基础设施"),

        // 提权（任务书指定 — sudo 任意后续命令均警示）
        new(["sudo"], static _ => true, "以管理员权限执行，请谨慎确认"),
    ];

    /// <summary>
    /// 检测命令文本的危险模式 — 词扫描（任意起点对齐复合命令分号/管道语义），
    /// 规则表顺序即优先级（最具体的组合模式在前），首个命中规则的警告返回；无命中返回 null。
    /// 纯函数无状态（线程安全）。
    /// </summary>
    /// <param name="commandText">命令文本（空白分词；大小写不敏感）。</param>
    /// <returns>中文警告文案，无危险模式为 null。</returns>
    public static string? MatchWarning(string commandText) =>
        string.IsNullOrWhiteSpace(commandText) ? null : MatchTokens(Tokenize(commandText));

    /// <summary>规则表顺序扫描 — 首个头部命中且谓词通过的规则警告。</summary>
    private static string? MatchTokens(string[] tokens) =>
        Rules.Select(rule => (Rule: rule, Start: FindHead(tokens, rule.HeadTokens)))
            .Where(hit => hit.Start >= 0)
            .Select(hit => hit.Rule.Match([
                .. tokens[(hit.Start + hit.Rule.HeadTokens.Length)..].Select(static token => token.ToLowerInvariant()),
            ]) ? hit.Rule.Warning : null)
            .FirstOrDefault(warning => warning is not null);

    /// <summary>分词 — 空白分隔 + 首/尾引号剥离（"请求执行 'git" 分词后 "'git" 剥离为 "git"，
    /// 对齐基准 \b 词边界语义；分号等终结符保留 — DELETE FROM 语句终结检查依赖）。</summary>
    private static string[] Tokenize(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('\'', '"'))
            .Where(token => token.Length > 0)
            .ToArray();

    /// <summary>词序列中查找规则头部前缀（任意起点；OrdinalIgnoreCase）。</summary>
    private static int FindHead(string[] tokens, string[] head)
    {
        for (var start = 0; start + head.Length <= tokens.Length; start++)
        {
            if (Enumerable.Range(0, head.Length).All(i => string.Equals(tokens[start + i], head[i], StringComparison.OrdinalIgnoreCase)))
            {
                return start;
            }
        }

        return -1;
    }

    /// <summary>短组合 flag 检查 — 任一 "-x…" 单横线 flag 含指定字母（大小写均收，覆盖 -f/-fd/-rf/-RF 组合）。</summary>
    private static bool HasShortFlag(string[] tokens, char flag) =>
        tokens.Any(token => token.Length > 1 && token[0] == '-' && token[1] != '-'
            && (token.Contains(flag) || token.Contains(char.ToUpperInvariant(flag))));

    /// <summary>剩余词集首词匹配（OrdinalIgnoreCase — SQL 关键词/stash 子命令大小写不可控）。</summary>
    private static bool IsFirstWord(string[] rest, params string[] words) =>
        words.Any(word => rest.Length > 0 && string.Equals(rest[0], word, StringComparison.OrdinalIgnoreCase));

    /// <summary>DELETE FROM 表名后直接终结 — 表名粘连 ; 或紧跟结尾（对齐基准正则的"无 WHERE 全删"语义；
    /// 词法分词剥离引号后双引号终结场景罕见，分号终结为主流覆盖）。</summary>
    private static bool EndsStatement(string[] rest) =>
        rest.Length == 2 || (rest.Length > 1 && rest[1].EndsWith(';'));
}
