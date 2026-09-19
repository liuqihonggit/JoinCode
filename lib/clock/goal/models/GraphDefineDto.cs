namespace Core.Goal;


/// <summary>
/// Graph 定义节点 DTO — goal_graph_define 工具的节点参数
/// </summary>
public sealed class GraphDefineNode {
    /// <summary>节点 ID</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }
    /// <summary>节点类型</summary>
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    /// <summary>节点名称</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>系统提示词</summary>
    [JsonPropertyName("systemPrompt")] public string? SystemPrompt { get; set; }
    /// <summary>节点指令</summary>
    [JsonPropertyName("instruction")] public string? Instruction { get; set; }
    /// <summary>是否使用全新上下文</summary>
    [JsonPropertyName("freshContext")] public bool FreshContext { get; set; }
}

/// <summary>
/// Graph 定义边 DTO — goal_graph_define 工具的边参数
/// </summary>
public sealed class GraphDefineEdge {
    /// <summary>边 ID</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }
    /// <summary>起始节点 ID</summary>
    [JsonPropertyName("fromId")] public string? FromId { get; set; }
    /// <summary>目标节点 ID</summary>
    [JsonPropertyName("toId")] public string? ToId { get; set; }
    /// <summary>边标签</summary>
    [JsonPropertyName("label")] public string? Label { get; set; }
}

[JsonSerializable(typeof(GraphDefineNode[]))]
[JsonSerializable(typeof(GraphDefineEdge[]))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class GraphDefineJsonContext : JsonSerializerContext;