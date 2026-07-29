namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 内容替换记录类型 — 对齐 TS ContentReplacementRecord.kind
/// </summary>
public enum ContentReplacementRecordKind
{
    /// <summary>
    /// 工具结果替换 — 对齐 TS kind: 'tool-result'
    /// </summary>
    ToolResult,
}

public sealed class ContentReplacementRecord
{
    /// <summary>
    /// 记录类型 — 对齐 TS ContentReplacementRecord.kind
    /// </summary>
    public required ContentReplacementRecordKind Kind { get; init; }

    public required string ToolUseId { get; init; }
    public required string Replacement { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(ContentReplacementRecord))]
[JsonSerializable(typeof(List<ContentReplacementRecord>))]
public sealed partial class ContentReplacementRecordListJsonContext : JsonSerializerContext;
