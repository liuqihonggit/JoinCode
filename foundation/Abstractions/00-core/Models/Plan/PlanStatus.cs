namespace JoinCode.Abstractions.Models.Plan;

/// <summary>
/// 计划状态
/// </summary>
public enum PlanStatus
{
    /// <summary>
    /// 草稿 - 计划正在编辑中
    /// </summary>
    [EnumValue("draft")] Draft,

    /// <summary>
    /// 等待审批 - 计划等待用户审批
    /// </summary>
    [EnumValue("awaiting_approval")] AwaitingApproval,

    /// <summary>
    /// 执行中 - 计划正在执行
    /// </summary>
    [EnumValue("executing")] Executing,

    /// <summary>
    /// 已完成 - 计划成功完成
    /// </summary>
    [EnumValue("completed")] Completed,

    /// <summary>
    /// 已取消 - 计划被取消
    /// </summary>
    [EnumValue("cancelled")] Cancelled,

    /// <summary>
    /// 已失败 - 计划执行失败
    /// </summary>
    [EnumValue("failed")] Failed
}

/// <summary>
/// 计划步骤状态
/// </summary>
public enum PlanStepStatus
{
    /// <summary>
    /// 待审批
    /// </summary>
    [EnumValue("pending")] Pending,

    /// <summary>
    /// 已批准
    /// </summary>
    [EnumValue("approved")] Approved,

    /// <summary>
    /// 已拒绝
    /// </summary>
    [EnumValue("rejected")] Rejected,

    /// <summary>
    /// 执行中
    /// </summary>
    [EnumValue("executing")] Executing,

    /// <summary>
    /// 已完成
    /// </summary>
    [EnumValue("completed")] Completed,

    /// <summary>
    /// 失败
    /// </summary>
    [EnumValue("failed")] Failed,

    /// <summary>
    /// 已跳过
    /// </summary>
    [EnumValue("skipped")] Skipped
}
