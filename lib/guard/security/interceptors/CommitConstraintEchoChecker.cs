
namespace Core.Security.Interceptors;

/// <summary>
/// Commit 约束回显检查器 — 检查 commit 消息是否回显了元指令/规则约束。
/// 共享检查逻辑，供 git_commit 工具中间件和 /commit 斜杠命令统一调用，避免多处重复实现。
/// </summary>
public static class CommitConstraintEchoChecker {
    private static readonly Regex[] ConstraintEchoPatterns = [
        new(@"\([^)]*(?:不含|不放|不删|不提|无.{0,4}版|非.{0,4}版|按.{0,6}要求|隐匿|省略若干)[^)]*\)", RegexOptions.None, TimeSpan.FromMilliseconds(500)),
        new(@"\[[^]]*(?:free|无|非|不含)[^]]*\]", RegexOptions.None, TimeSpan.FromMilliseconds(500)),
        new(@"按用户要求|面向.{0,6}汇报|给领导看", RegexOptions.None, TimeSpan.FromMilliseconds(500)),
    ];

    /// <summary>
    /// 检查 commit 消息是否匹配任一约束回显模式。
    /// </summary>
    /// <param name="message">commit 消息文本</param>
    /// <returns>true 表示疑似回显约束；false 表示正常</returns>
    public static bool ContainsConstraintEcho(string? message) {
        if (string.IsNullOrEmpty(message))
            return false;
        foreach (var pattern in ConstraintEchoPatterns) {
            if (pattern.IsMatch(message))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 构建拒绝原因 — 提示 AI 重新写 commit 消息，不要回显约束。
    /// </summary>
    /// <returns>拒绝原因文本，含错误示例和正确示例</returns>
    public static string BuildDenyReason() {
        return """
        commit 消息疑似回显了元指令/规则约束，已拦截。commit 消息只描述变更本身，不要回显约束。

        错误示例（禁止）：
        - "feat: xxx (不含 Y)"
        - "feat: xxx (无分支名版)"
        - "feat: xxx [branch-free]"
        - "feat: xxx（已按要求隐匿某约束）"
        - "feat: 按用户要求调整 xxx"

        正确示例：
        - "feat: xxx"
        - "fix: 修复 Y 边界"

        请重新写 commit 消息，只描述做了什么变更，不提任何约束/规则/指令。
        """;
    }
}
