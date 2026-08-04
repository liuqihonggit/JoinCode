
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 任务可拆性分析器 — 判断目标是否可分解为并行子任务
/// </summary>
public interface IDecomposabilityAnalyzer
{
    Task<DecompositionResult> AnalyzeAsync(
        string objective,
        IReadOnlyList<string> constraints,
        CancellationToken cancellationToken = default);
}
