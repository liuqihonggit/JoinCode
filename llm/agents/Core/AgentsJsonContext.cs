
namespace Core.Agents;

/// <summary>
/// Agents 子系统统一 JSON 序列化上下文 — AOT 源码生成，覆盖权限、计划审批、判断等类型
/// </summary>
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
    /// <summary>是否需要修复</summary>
    [System.Text.Json.Serialization.JsonPropertyName("needsFix")]
    public bool NeedsFix { get; set; }

    /// <summary>目标源码文件路径</summary>
    [System.Text.Json.Serialization.JsonPropertyName("targetFile")]
    public string? TargetFile { get; set; }

    /// <summary>修复优先级（high/medium/low）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("priority")]
    public string Priority { get; set; } = "low";

    /// <summary>LLM 推理过程</summary>
    [System.Text.Json.Serialization.JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }
}

/// <summary>
/// 快照元数据 JSON 格式
/// </summary>
public sealed class AgentMemorySnapshotMetaJson
{
    /// <summary>快照更新时间</summary>
    [System.Text.Json.Serialization.JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// 同步标记元数据 JSON 格式
/// </summary>
public sealed class AgentMemorySyncedMetaJson
{
    /// <summary>同步来源标识</summary>
    [System.Text.Json.Serialization.JsonPropertyName("syncedFrom")]
    public string SyncedFrom { get; set; } = string.Empty;
}
