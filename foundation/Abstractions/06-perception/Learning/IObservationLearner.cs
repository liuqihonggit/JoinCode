namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 观察会话 — 用户演示操作的完整记录（PRD L-01）
/// </summary>
public sealed record ObservedSession(
    string Name,
    IReadOnlyList<DesktopOperation> Operations,
    IReadOnlyList<string> Screenshots,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

/// <summary>
/// 抽象操作逻辑 — 从观察中学习到的参数化操作模式（PRD L-02）
/// </summary>
public sealed record AbstractOperationLogic(
    string Name,
    string Pattern,
    string Parameters,
    IReadOnlyList<string> Steps,
    double Confidence);

/// <summary>
/// 观察学习器 — 从用户演示中学习操作模式并优化（PRD L-02/L-04）
/// </summary>
public interface IObservationLearner
{
    /// <summary>操作抽象（L-02）— 将原始操作序列抽象为参数化逻辑</summary>
    Task<AbstractOperationLogic> AbstractAsync(ObservedSession session, CancellationToken cancellationToken = default);

    /// <summary>步骤优化（L-04）— 分析抽象逻辑并提出优化建议</summary>
    Task<string> OptimizeAsync(AbstractOperationLogic logic, CancellationToken cancellationToken = default);
}
