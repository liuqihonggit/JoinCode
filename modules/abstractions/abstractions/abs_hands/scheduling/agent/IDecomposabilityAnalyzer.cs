
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 任务可拆性分析器 — 判断目标是否可分解为并行子任务
/// </summary>
public interface IDecomposabilityAnalyzer {
    /// <summary>异步分析目标是否可拆解为并行子任务。</summary>
    /// <param name="objective">目标描述。</param>
    /// <param name="constraints">约束条件列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<DecompositionResult> AnalyzeAsync(
        string objective,
        IReadOnlyList<string> constraints,
        CancellationToken cancellationToken = default);
}