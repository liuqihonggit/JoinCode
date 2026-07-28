namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 模型协调器接口 — 双模型分离会话（Planner/Executor）
/// 对齐 Reasonix Coordinator: Planner 在独立 Session 中用只读工具做研究，
/// Executor 在另一个独立 Session 中执行计划。两个 Session 永不混合。
/// </summary>
public interface IModelCoordinator
{
    /// <summary>
    /// 规划 — Planner 在独立 Session 中用只读工具做研究，产出计划
    /// </summary>
    Task<ModelPlanResult> PlanAsync(string objective, CancellationToken ct = default);

    /// <summary>
    /// 执行 — Executor 在独立 Session 中执行计划
    /// </summary>
    Task<ModelExecutionResult> ExecuteAsync(string objective, string plan, CancellationToken ct = default);

    /// <summary>
    /// 规划并执行 — 完整的 Plan → Execute 流程
    /// </summary>
    Task<ModelCoordinationResult> PlanAndExecuteAsync(string objective, CancellationToken ct = default);

    /// <summary>
    /// 重置 Planner 会话 — 切换到新的 Executor 会话时调用
    /// </summary>
    void ResetPlannerSession();
}

/// <summary>
/// 规划结果
/// </summary>
public sealed class ModelPlanResult
{
    /// <summary>是否成功产出计划</summary>
    public bool Succeeded { get; init; }

    /// <summary>计划内容</summary>
    public string Plan { get; init; } = string.Empty;

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>是否为空计划（无需执行）</summary>
    public bool IsNoOp { get; init; }

    /// <summary>Planner 使用的 token 用量</summary>
    public TokenUsage? Usage { get; init; }

    public static ModelPlanResult Success(string plan, bool isNoOp = false, TokenUsage? usage = null) => new()
    {
        Succeeded = true,
        Plan = plan,
        IsNoOp = isNoOp,
        Usage = usage
    };

    public static ModelPlanResult Fail(string errorMessage) => new()
    {
        Succeeded = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// 执行结果
/// </summary>
public sealed class ModelExecutionResult
{
    /// <summary>是否成功执行</summary>
    public bool Succeeded { get; init; }

    /// <summary>执行输出</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Executor 使用的 token 用量</summary>
    public TokenUsage? Usage { get; init; }

    public static ModelExecutionResult Success(string output, TokenUsage? usage = null) => new()
    {
        Succeeded = true,
        Output = output,
        Usage = usage
    };

    public static ModelExecutionResult Fail(string errorMessage) => new()
    {
        Succeeded = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// 协调结果 — 完整的 Plan + Execute 流程结果
/// </summary>
public sealed class ModelCoordinationResult
{
    /// <summary>规划结果</summary>
    public ModelPlanResult Plan { get; init; } = new();

    /// <summary>执行结果（空计划时为 null）</summary>
    public ModelExecutionResult? Execution { get; init; }

    /// <summary>整体是否成功</summary>
    public bool Succeeded => Plan.Succeeded && (Plan.IsNoOp || Execution?.Succeeded == true);

    public static ModelCoordinationResult FromPlanOnly(ModelPlanResult plan) => new()
    {
        Plan = plan,
        Execution = null
    };

    public static ModelCoordinationResult FromPlanAndExecution(ModelPlanResult plan, ModelExecutionResult execution) => new()
    {
        Plan = plan,
        Execution = execution
    };
}
