namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 协议 JSON 上下文 — AOT 源生成（ADR 0141：DTO 双向转换全部经此上下文，禁止手写拼接）。
/// camelCase 字段命名对齐基准 JSON 风格。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(BridgeRequest))]
[JsonSerializable(typeof(BridgeEvent))]
[JsonSerializable(typeof(DiffHunk))]
[JsonSerializable(typeof(MarkdownDocument))]
public sealed partial class BridgeJsonContext : JsonSerializerContext;
