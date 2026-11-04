
namespace JoinCode.Abstractions.Interfaces;

public interface IPlanService {
    /// <summary>异步执行计划。</summary>
    Task<string> ExecutePlanAsync(string userPrompt, CancellationToken cancellationToken = default);
    /// <summary>异步执行计划并返回结果。</summary>
    Task<PlanExecutionResult> ExecutePlanWithResultAsync(string userPrompt, CancellationToken cancellationToken = default);
}