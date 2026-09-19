namespace JoinCode;

/// <summary>
/// 工具直调子命令参数 — mcp_call 和 slash_call 共享
/// [CliOption] 由 CliOptionGenerator 自动生成 ToolCallArgParser + ToolCallArgCliOptionConstants
/// </summary>
public enum ToolCallArg {
    /// <summary>从 JSON 文件读取工具参数</summary>
    [CliOption(JccCliArgEnumConstants.ArgsFile, "", "从 JSON 文件读取工具参数", AcceptsValue = true, Category = "参数源", Example = "jcc mcp_call read_file --args-file args.json")]
    ArgsFile,

    /// <summary>从 stdin 读取工具参数（JSON）</summary>
    [CliOption(JccCliArgEnumConstants.ArgsStdin, "", "从 stdin 读取工具参数（JSON）", Category = "参数源", Example = "jcc mcp_call read_file --args-stdin < args.json")]
    ArgsStdin,
}