namespace Core.Goal;


/// <summary>
/// Graph 定义节点 DTO — goal_graph_define 工具的节点参数
/// </summary>
public sealed class GraphDefineNode
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("systemPrompt")] public string? SystemPrompt { get; set; }
    [JsonPropertyName("instruction")] public string? Instruction { get; set; }
    [JsonPropertyName("freshContext")] public bool FreshContext { get; set; }
}

/// <summary>
/// Graph 定义边 DTO — goal_graph_define 工具的边参数
/// </summary>
public sealed class GraphDefineEdge
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("fromId")] public string? FromId { get; set; }
    [JsonPropertyName("toId")] public string? ToId { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
}

[JsonSerializable(typeof(GraphDefineNode[]))]
[JsonSerializable(typeof(GraphDefineEdge[]))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class GraphDefineJsonContext : JsonSerializerContext;
