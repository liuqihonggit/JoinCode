
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 计划模式管理器接口
/// </summary>
public interface IPlanModeManager
{
    /// <summary>
    /// 当前是否处于计划模式
    /// </summary>
    bool IsInPlanMode { get; }

    /// <summary>
    /// 当前计划ID
    /// </summary>
    string? CurrentPlanId { get; }

    /// <summary>
    /// 对齐 TS hasExitedPlanModeInSession: 本次会话是否退出过plan模式
    /// </summary>
    bool HasExitedPlanMode { get; }

    /// <summary>
    /// 对齐 TS needsPlanModeExitAttachment: 退出plan后是否需要发送一次性通知
    /// </summary>
    bool NeedsPlanModeExitAttachment { get; }

    /// <summary>
    /// 对齐 TS setNeedsPlanModeExitAttachment(false): 清除退出通知标志
    /// </summary>
    void ClearPlanModeExitAttachment();

    /// <summary>
    /// 对齐 TS setHasExitedPlanMode(false): 清除已退出plan标志
    /// </summary>
    void ClearHasExitedPlanMode();

    /// <summary>
    /// 进入计划模式
    /// </summary>
    Task<PlanOperationResult> EnterPlanModeAsync(
        string? description = null,
        List<PlanStepInput>? initialSteps = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 退出计划模式
    /// </summary>
    Task<PlanOperationResult> ExitPlanModeAsync(
        bool executeRemainingSteps = false,
        AllowedPrompt[]? allowedPrompts = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前计划状态
    /// </summary>
    Task<PlanState?> GetPlanStatusAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加计划步骤
    /// </summary>
    Task<PlanOperationResult> AddStepAsync(
        string description,
        string? toolName = null,
        Dictionary<string, JsonElement>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批准执行步骤
    /// </summary>
    Task<PlanOperationResult> ApproveStepAsync(
        int stepIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 拒绝执行步骤
    /// </summary>
    Task<PlanOperationResult> RejectStepAsync(
        int stepIndex,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行已批准的步骤
    /// </summary>
    Task<PlanOperationResult> ExecuteApprovedStepsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改步骤
    /// </summary>
    Task<PlanOperationResult> ModifyStepAsync(
        int stepIndex,
        string? newDescription = null,
        string? newToolName = null,
        Dictionary<string, JsonElement>? newParameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除步骤
    /// </summary>
    Task<PlanOperationResult> RemoveStepAsync(
        int stepIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重新排序步骤
    /// </summary>
    Task<PlanOperationResult> ReorderStepsAsync(
        List<int> newOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有计划历史
    /// </summary>
    Task<List<PlanState>> GetPlanHistoryAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理审批响应 — 对齐 TS handlePlanApprovalResponse
    /// Leader 审批后，teammate 的 mailbox poller 调用此方法恢复权限模式
    /// </summary>
    Task HandlePlanApprovalResponseAsync(
        PlanApprovalResponseMessage response,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 对齐 TS cleanupOldPlanFiles(): 清理超过指定天数的旧 plan 文件
    /// 默认清理 30 天前的文件
    /// </summary>
    int CleanupOldPlanFiles(int maxAgeDays = 30);

    /// <summary>
    /// 对齐 TS clearPlanSlug(): 清除当前 session 的 slug 缓存
    /// 下次进入 plan mode 时会生成新 slug（即新文件）
    /// </summary>
    void ClearPlanSlug();
}
