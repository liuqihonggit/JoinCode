namespace JoinCode.Abstractions.Cmd;

/// <summary>
/// LLM 侧工具定义 — 斜杠命令和 MCP 工具的统一工具描述，供 LLM prompt 构建
/// </summary>
public sealed record CmdToolDef
{
    /// <summary>工具名称</summary>
    public required string Name { get; init; }

    /// <summary>工具描述</summary>
    public string Description { get; init; } = "";

    /// <summary>输入 schema — MCP 用原 schema，斜杠自动生成</summary>
    public ToolSchema? InputSchema { get; init; }

    /// <summary>命令来源</summary>
    public required CmdSource Source { get; init; }

    /// <summary>ToolKind — 决定注入策略</summary>
    public ToolKind Kind { get; init; } = ToolKind.System;

    // === 工厂方法 ===

    /// <summary>从 MCP 工具创建 — 直接用原 schema</summary>
    public static CmdToolDef FromMcp(IToolHandler handler) => new()
    {
        Name = handler.Name,
        Description = handler.Description,
        InputSchema = handler.InputSchema,
        Source = CmdSource.Mcp,
        Kind = handler.Kind,
    };

    /// <summary>从斜杠命令创建 — 自动生成简单 schema</summary>
    public static CmdToolDef FromSlash(IChatCommand cmd, ToolKind kind = ToolKind.Slash) => new()
    {
        Name = cmd.Name,
        Description = cmd.Description,
        InputSchema = CreateSlashSchema(cmd),
        Source = CmdSource.Slash,
        Kind = kind,
    };

    /// <summary>
    /// 为斜杠命令自动生成 schema — {"arguments": string}
    /// </summary>
    private static ToolSchema CreateSlashSchema(IChatCommand cmd)
    {
        var schema = new ToolSchema();
        schema.Properties["arguments"] = new ToolSchemaProperty
        {
            Type = "string",
            Description = string.IsNullOrEmpty(cmd.ArgumentHint)
                ? "命令参数"
                : cmd.ArgumentHint,
        };
        return schema;
    }
}
