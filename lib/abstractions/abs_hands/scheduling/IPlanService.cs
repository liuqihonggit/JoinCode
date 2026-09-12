
namespace JoinCode.Abstractions.Interfaces;

public interface IPlanService {
    Task<string> ExecutePlanAsync(string userPrompt, CancellationToken cancellationToken = default);
    Task<PlanExecutionResult> ExecutePlanWithResultAsync(string userPrompt, CancellationToken cancellationToken = default);
}
