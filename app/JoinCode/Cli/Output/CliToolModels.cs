namespace JoinCode.Cli.Output;

/// <summary>
/// 工具列表项 — jcc mcp list --json 输出契约
/// </summary>
public sealed record CliToolListItem(string Name, string Description, string? Category, string? GroupName, string Kind);

/// <summary>
/// 工具搜索结果项 — jcc mcp search --json 输出契约
/// </summary>
public sealed record CliToolSearchItem(string Name, string? Description, string? Category);
