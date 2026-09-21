
namespace JoinCode.Abstractions.Interfaces;

public interface IRemotePolicyService : IAsyncDisposable {
    /// <summary>异步评估指定操作的策略。</summary>
    Task<PolicyEvaluationResult> EvaluateAsync(string action, Dictionary<string, string>? context = null, CancellationToken cancellationToken = default);
    /// <summary>异步获取活跃策略规则列表。</summary>
    Task<IReadOnlyList<PolicyRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);
    /// <summary>异步刷新策略缓存。</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
    /// <summary>异步评估指定操作的所有策略。</summary>
    Task<IReadOnlyList<PolicyEvaluationResult>> EvaluateAllAsync(string action, Dictionary<string, string>? context = null, CancellationToken cancellationToken = default);
}