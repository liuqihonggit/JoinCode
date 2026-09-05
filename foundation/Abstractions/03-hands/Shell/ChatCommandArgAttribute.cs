namespace JoinCode.ChatCommands;

/// <summary>
/// 斜杠命令参数声明特性 — 声明在命令类上，源码生成器扫描收集，生成 SlashCommandSchemaCatalog。
/// 和 mcp_schema 统一输出 ToolSchema JSON 格式，供 slash_schema 查询和 AI 调用参考。
/// 渐进式：未声明此特性的命令降级输出 ArgumentHint，不阻塞重构。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ChatCommandArgAttribute : Attribute
{
    /// <summary>
    /// 参数名 — 对应 ToolSchemaProperty 的 key
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 参数类型 — string/boolean/number/array/object，对应 ToolSchemaProperty.Type
    /// </summary>
    public string Type { get; init; } = "string";

    /// <summary>
    /// 参数描述 — 对应 ToolSchemaProperty.Description
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 是否必需 — 为 true 时加入 ToolSchema.Required 列表
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// 默认值 — 对应 ToolSchemaProperty.Default
    /// </summary>
    public string? Default { get; init; }

    /// <summary>
    /// 枚举约束 — 对应 ToolSchemaProperty.Enum，限制参数取值范围
    /// </summary>
    public string[]? Enum { get; init; }

    /// <summary>
    /// 数组元素类型 — Type 为 array 时，对应 ToolSchemaProperty.Items.Type
    /// </summary>
    public string? ItemsType { get; init; }

    /// <summary>
    /// 数组元素描述 — Type 为 array 时，对应 ToolSchemaProperty.Items.Description
    /// </summary>
    public string? ItemsDescription { get; init; }
}
