namespace Core.Agents.Coordinator;

/// <summary>
/// Lifecycle JSON 序列化上下文 — Worktree 路径等生命周期相关类型的 AOT 源码生成
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WorktreePathJson))]
[JsonSerializable(typeof(string))]
public sealed partial class LifecycleJsonContext : JsonSerializerContext;
