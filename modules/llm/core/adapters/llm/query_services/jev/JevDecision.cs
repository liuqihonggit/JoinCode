
namespace Api.LLM.QueryServices.Jev;

/// <summary>
/// Jev 决策实现 — ITypedDecision 的 Jev 具体实现
/// 承载 Jev API 返回的单个决策结果(Noul/Choice/Score + Confidence)
/// </summary>
public sealed class JevDecision : ITypedDecision {
    /// <inheritdoc />
    public string QuestionName { get; init; } = string.Empty;

    /// <inheritdoc />
    public TypedDecisionKind Kind { get; init; }

    /// <inheritdoc />
    public double Confidence { get; init; }

    /// <inheritdoc />
    public JsonElement RawValue { get; init; }
}
