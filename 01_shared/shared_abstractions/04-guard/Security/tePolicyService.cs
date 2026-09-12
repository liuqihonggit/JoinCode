
namespace JoinCode.Abstractions.Interfaces;

public interface IRemotePolicyService : IDisposable
{
    Task<PolicyEvaluationResult> EvaluateAsync(string action, Dictionary<string, string>? context = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PolicyRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PolicyEvaluationResult>> EvaluateAllAsync(string action, Dictionary<string, string>? context = null, CancellationToken cancellationToken = default);
}
