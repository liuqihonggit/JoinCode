namespace JoinCode.Abstractions.Utils;

/// <summary>
/// CLI 入口级子命令 — 源码生成器自动生成 CliSubCommandConstants + CliSubCommandExtensions + SubCommandHelpText
/// 适用范围: jcc [mcp_call|mcp_list|mcp_schema|mcp_search|mcp_serve|slash_call|slash_list|slash_schema|doctor|schema|remote-control|rc|remote|rg|gh] [子参数]
///
/// 使用示例:
/// - FromValue("mcp_call")       → CliSubCommand.McpCall
/// - FromValue("slash_call")     → CliSubCommand.SlashCall
/// - FromValue("RC")             → CliSubCommand.RemoteControl (OrdinalIgnoreCase)
/// - FromValue("rg")             → CliSubCommand.Rg
/// - CliSubCommand.RemoteControl.ToValue() → "remote-control"
/// </summary>
public enum CliSubCommand
{
    /// <summary>MCP 工具管理（旧设计，阶段4清理时移除）</summary>
    [EnumValue("tool")]
    [SubCommandInfo("旧设计, 阶段4清理时移除", "已废弃", IsDeprecated = true)]
    Tool,

    /// <summary>智能体管理（旧设计，阶段4清理时移除）</summary>
    [EnumValue("agent")]
    [SubCommandInfo("旧设计, 阶段4清理时移除", "已废弃", IsDeprecated = true)]
    Agent,

    /// <summary>代码操作（旧设计，阶段4清理时移除）</summary>
    [EnumValue("code")]
    [SubCommandInfo("旧设计, 阶段4清理时移除", "已废弃", IsDeprecated = true)]
    Code,

    /// <summary>MCP 子命令（旧设计，由 mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve 取代）</summary>
    [EnumValue("mcp")]
    [SubCommandInfo("旧设计, 由 mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve 取代", "已废弃", IsDeprecated = true)]
    Mcp,

    /// <summary>Schema 自省 — 输出 CLI 参数定义 JSON，供 Agent 动态查询</summary>
    [EnumValue("schema")]
    [SubCommandInfo("输出 CLI 参数定义 JSON, 供 Agent 动态查询", "自省", Example = "jcc schema")]
    Schema,

    /// <summary>远程控制（主名称）</summary>
    [EnumValue("remote-control")]
    [SubCommandInfo("远程控制 — 启动 HTTP 服务接收外部指令", "远程控制", Example = "jcc remote-control --port 9903")]
    RemoteControl,

    /// <summary>远程控制别名</summary>
    [EnumValue("rc")]
    [SubCommandInfo("远程控制别名", "远程控制", IsAlias = true, AliasOf = "remote-control")]
    Rc,

    /// <summary>远程控制别名</summary>
    [EnumValue("remote")]
    [SubCommandInfo("远程控制别名", "远程控制", IsAlias = true, AliasOf = "remote-control")]
    Remote,

    /// <summary>MCP 工具直调 — jcc mcp_call &lt;tool&gt; &lt;argsJson&gt;</summary>
    [EnumValue("mcp_call")]
    [SubCommandInfo("MCP 工具直调 — 传工具名+JSON参数, 直返结果", "MCP 工具", Example = "jcc mcp_call gh_pr_view {\"pr_number\":\"123\"}")]
    McpCall,

    /// <summary>MCP 工具列表 — jcc mcp_list [--category &lt;cat&gt;]</summary>
    [EnumValue("mcp_list")]
    [SubCommandInfo("MCP 工具列表 — 按分类列出所有可用工具", "MCP 工具", Example = "jcc mcp_list --category github")]
    McpList,

    /// <summary>MCP 工具参数 schema — jcc mcp_schema &lt;tool&gt;</summary>
    [EnumValue("mcp_schema")]
    [SubCommandInfo("MCP 工具参数 schema — 查看工具的参数定义", "MCP 工具", Example = "jcc mcp_schema gh_pr_view")]
    McpSchema,

    /// <summary>MCP 工具搜索 — jcc mcp_search &lt;query&gt;</summary>
    [EnumValue("mcp_search")]
    [SubCommandInfo("MCP 工具搜索 — 关键词搜索或按分组下钻", "MCP 工具", Example = "jcc mcp_search \"pr check\"")]
    McpSearch,

    /// <summary>MCP 服务端 — jcc mcp_serve [--port 9903]</summary>
    [EnumValue("mcp_serve")]
    [SubCommandInfo("MCP 服务端 — 以 HTTP 服务暴露 jcc 工具", "MCP 工具", Example = "jcc mcp_serve --port 9903")]
    McpServe,

    /// <summary>斜杠命令直调 — jcc slash_call &lt;cmd&gt; &lt;argsJson&gt;</summary>
    [EnumValue("slash_call")]
    [SubCommandInfo("斜杠命令直调 — 传命令名+JSON参数", "斜杠命令", Example = "jcc slash_call /help {}")]
    SlashCall,

    /// <summary>斜杠命令列表 — jcc slash_list [--category &lt;cat&gt;]</summary>
    [EnumValue("slash_list")]
    [SubCommandInfo("斜杠命令列表 — 按分类列出所有斜杠命令", "斜杠命令", Example = "jcc slash_list")]
    SlashList,

    /// <summary>斜杠命令参数 schema — jcc slash_schema &lt;cmd&gt;</summary>
    [EnumValue("slash_schema")]
    [SubCommandInfo("斜杠命令参数 schema — 查看命令的参数定义", "斜杠命令", Example = "jcc slash_schema /compact")]
    SlashSchema,

    /// <summary>医生模式 — jcc doctor [--server] [--port &lt;n&gt;]</summary>
    [EnumValue("doctor")]
    [SubCommandInfo("医生模式 — 诊断和修复 jcc 运行问题", "诊断", Example = "jcc doctor")]
    Doctor,

    /// <summary>ripgrep 兼容搜索 — jcc rg &lt;pattern&gt; [path...] [--type cs] [-g "!**/tests/**"] [-i] [-n] [-A N] [-B N] [-C N] [--head-limit N] [-U] [-F] [--count] [--files-with-matches] [--content] [--timeout N] [--json]</summary>
    /// <para>ADR: 0070 — 内置 rg 实现，复用 ISearchService.GrepSearchAsync，宽容处理 PowerShell 转义、缺少路径禁止扫盘、超时硬终止。</para>
    [EnumValue("rg")]
    [SubCommandInfo("ripgrep 兼容搜索 — 内置 RgEngine(mmap+PLINQ+零GC)", "搜索", Example = "jcc rg \"pattern\" core/ --type cs")]
    Rg,

    /// <summary>GitHub 操作 — jcc gh &lt;group&gt; &lt;action&gt; [位置参数...] [--选项 值] [--json]（如 jcc gh pr checks 123）</summary>
    /// <para>ADR: 0089 — 禁止系统 gh CLI；ADR: 0090 — 扁平元动词形态，工具名按 gh_{group}_{action} 约定拼接，位置参数按 schema required 顺序绑定。</para>
    [EnumValue("gh")]
    [SubCommandInfo("GitHub 操作 — 扁平元动词(pr/issue/repo/release/run/branch/api)", "GitHub", Example = "jcc gh pr checks 123")]
    Gh,
}
