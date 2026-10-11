
namespace JoinCode.Abstractions.Decision;

/// <summary>
/// 类型化决策结果容器 — 包含所有问题的决策答案 + 模型元数据
/// 对应 Jev 响应的 answers + model + usage 字段
/// </summary>
public sealed class TypedDecisionResult {
    /// <summary>决策答案字典,key 对应请求 questions 的 key,value 为该问题的决策</summary>
    public IReadOnlyDictionary<string, ITypedDecision> Answers { get; init; } = FrozenDictionary<string, ITypedDecision>.Empty;

    /// <summary>实际响应的模型标识符(如 "jev-latest" 别名解析后的具体版本)</summary>
    public string? ModelId { get; init; }

    /// <summary>token 用量统计</summary>
    public TypedDecisionUsage? Usage { get; init; }
}
