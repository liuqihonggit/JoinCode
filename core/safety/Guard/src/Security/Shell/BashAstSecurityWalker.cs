using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Bash AST 安全步行器 — 对齐 TS ast.ts parseForSecurity
/// 基于 TreeSitter.DotNet 解析结果，实现 FAIL-CLOSED 安全检查
///
/// 核心设计：任何无法静态分析的结构 → too-complex → 需要用户手动审批
/// 这不是沙箱，它只回答一个问题："我们能否为每个简单命令生成可信的 argv[]？"
/// </summary>
public interface IBashAstSecurityWalker
{
    /// <summary>
    /// 解析命令并提取安全信息 — 对齐 TS parseForSecurity
    /// </summary>
    BashAstSecurityResult ParseForSecurity(string command);

    /// <summary>
    /// 语义安全检查 — 对齐 TS ast.ts checkSemantics
    /// 对提取的命令列表进行安全规则检查
    /// </summary>
    BashSemanticCheckResult CheckSemantics(BashSimpleCommandInfo[] commands);
}
