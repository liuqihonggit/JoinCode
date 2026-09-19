namespace JoinCode;

/// <summary>
/// mcp_list 子命令参数 — [CliOption] 由 CliOptionGenerator 自动生成 McpListArgParser + McpListArgCliOptionConstants
/// </summary>
public enum McpListArg {
    /// <summary>按分类过滤工具列表</summary>
    [CliOption(JccCliArgEnumConstants.Category, "", "按分类过滤工具列表", AcceptsValue = true, Category = "过滤", Example = "jcc mcp_list --category Code")]
    Category,
}