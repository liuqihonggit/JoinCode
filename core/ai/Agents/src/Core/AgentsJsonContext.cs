
namespace Core.Agents;

[JsonSourceGenerationOptions(WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(SwarmPermissionRequestData))]
[JsonSerializable(typeof(SwarmPermissionResponseData))]
[JsonSerializable(typeof(SwarmPermissionUpdateData))]
[JsonSerializable(typeof(List<SwarmPermissionUpdateData>))]
[JsonSerializable(typeof(PlanApprovalRequestMessage))]
[JsonSerializable(typeof(PlanApprovalResponseMessage))]
[JsonSerializable(typeof(BootstrapJudgmentJson))]
[JsonSerializable(typeof(AgentMemorySnapshotMetaJson))]
[JsonSerializable(typeof(AgentMemorySyncedMetaJson))]
public partial class AgentsJsonContext : JsonSerializerContext;

/// <summary>
/// Bootstrap LLM 判断结果 JSON 格式
/// </summary>
public sealed class BootstrapJudgmentJson
{
    [System.Text.Json.Serialization.JsonPropertyName("needsFix")]
    public bool NeedsFix { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("targetFile")]
    public string? TargetFile { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("priority")]
    public string Priority { get; set; } = "low";

    [System.Text.Json.Serialization.JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }
}

/// <summary>
/// 快照元数据 JSON 格式
/// </summary>
public sealed class AgentMemorySnapshotMetaJson
{
    [System.Text.Json.Serialization.JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// 同步标记元数据 JSON 格式
/// </summary>
public sealed class AgentMemorySyncedMetaJson
{
    [System.Text.Json.Serialization.JsonPropertyName("syncedFrom")]
    public string SyncedFrom { get; set; } = string.Empty;
}
