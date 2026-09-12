namespace JoinCode.Abstractions.Models;

/// <summary>
/// 命名描述基类 — 提取 ToolDefinition、McpToolDefinition 等共同的 Name + Description 模式
/// </summary>
public abstract class NamedItem
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
