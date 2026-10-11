
namespace JoinCode.Abstractions.Decision;

/// <summary>
/// 类型化决策 token 用量 — 对应 Jev 响应的 usage 字段
/// </summary>
public sealed class TypedDecisionUsage {
    /// <summary>输入 token 数(state + questions 序列化后的 token 总量)</summary>
    public int InputTokens { get; init; }
}
