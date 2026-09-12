namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 斜杠命令参数 schema 条目 — 每个斜杠命令的参数声明，供 slash_schema 查询和 AI 调用参考。
/// 由源码生成器从 [ChatCommandArg] 特性自动提取。未声明 [ChatCommandArg] 的命令 Schema 为 null，降级用 ArgumentHint。
/// </summary>
public sealed record SlashCommandSchemaEntry
{
    /// <summary>命令名（如 "compact"，不含 /）</summary>
    public required string CommandName { get; init; }

    /// <summary>参数 schema（ToolSchema 格式，和 mcp_schema 统一）；null 表示未声明 [ChatCommandArg]</summary>
    public ToolSchema? Schema { get; init; }

    /// <summary>降级提示 — 未声明 [ChatCommandArg] 时用 ArgumentHint 填充</summary>
    public string? ArgumentHint { get; init; }
}

/// <summary>
/// 斜杠命令参数 schema 目录接口 — 由源码生成器生成的 GeneratedSlashCommandSchemaCatalog 实现。
/// slash_schema &lt;cmd&gt; 从此接口查询参数 schema，AI 调用 slash_call 时参考此 schema 构造参数。
/// </summary>
public interface ISlashCommandSchemaCatalog
{
    /// <summary>全部斜杠命令参数 schema 条目</summary>
    IReadOnlyList<SlashCommandSchemaEntry> AllSchemas { get; }

    /// <summary>按命令名查询参数 schema；未找到返回 null</summary>
    ToolSchema? GetSchema(string commandName);
}
