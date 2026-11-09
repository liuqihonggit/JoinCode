namespace Core.Agents.Coordinator;

/// <summary>
/// Swarm JSON 序列化上下文 — Swarm 权限请求/响应/更新 + 计划审批消息的 AOT 源码生成
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SwarmPermissionRequestData))]
[JsonSerializable(typeof(SwarmPermissionResponseData))]
[JsonSerializable(typeof(SwarmPermissionUpdateData))]
[JsonSerializable(typeof(List<SwarmPermissionUpdateData>))]
[JsonSerializable(typeof(PlanApprovalRequestMessage))]
[JsonSerializable(typeof(PlanApprovalResponseMessage))]
[JsonSerializable(typeof(string))]
public sealed partial class SwarmJsonContext : JsonSerializerContext;
