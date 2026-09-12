
namespace JoinCode.Abstractions.Commands;

/// <summary>
/// 进入计划模式命令
/// </summary>
public sealed record EnterPlanModeCommand(
    [StringLength(500, ErrorMessage = "计划描述过长")]
    string? Description);

/// <summary>
/// 退出计划模式命令
/// </summary>
public sealed record ExitPlanModeCommand(
    bool? ExecuteRemainingSteps);

/// <summary>
/// 添加计划步骤命令
/// </summary>
public sealed record AddPlanStepCommand(
    [Required(ErrorMessage = "description 不能为空")]
    [StringLength(500, ErrorMessage = "步骤描述过长")]
    string Description,
    [StringLength(100, ErrorMessage = "工具名称过长")]
    string? ToolName,
    Dictionary<string, JsonElement>? Parameters);

/// <summary>
/// 批准计划步骤命令
/// </summary>
public sealed record ApprovePlanStepCommand(
    [Required(ErrorMessage = "step_index 不能为空")]
    [Range(0, int.MaxValue, ErrorMessage = "步骤索引必须是非负数")]
    int StepIndex);

/// <summary>
/// 拒绝计划步骤命令
/// </summary>
public sealed record RejectPlanStepCommand(
    [Required(ErrorMessage = "step_index 不能为空")]
    [Range(0, int.MaxValue, ErrorMessage = "步骤索引必须是非负数")]
    int StepIndex,
    [StringLength(500, ErrorMessage = "拒绝原因过长")]
    string? Reason);

/// <summary>
/// 执行计划步骤命令
/// </summary>
public sealed record ExecutePlanStepsCommand;

/// <summary>
/// 修改计划步骤命令
/// </summary>
public sealed record ModifyPlanStepCommand(
    [Required(ErrorMessage = "step_index 不能为空")]
    [Range(0, int.MaxValue, ErrorMessage = "步骤索引必须是非负数")]
    int StepIndex,
    [StringLength(500, ErrorMessage = "步骤描述过长")]
    string? NewDescription,
    [StringLength(100, ErrorMessage = "工具名称过长")]
    string? NewToolName,
    Dictionary<string, JsonElement>? NewParameters);

/// <summary>
/// 删除计划步骤命令
/// </summary>
public sealed record RemovePlanStepCommand(
    [Required(ErrorMessage = "step_index 不能为空")]
    [Range(0, int.MaxValue, ErrorMessage = "步骤索引必须是非负数")]
    int StepIndex);

/// <summary>
/// 获取计划状态命令
/// </summary>
public sealed record GetPlanStatusCommand;

/// <summary>
/// 获取计划历史命令
/// </summary>
public sealed record GetPlanHistoryCommand(
    [Range(1, 100, ErrorMessage = "限制数量必须在1-100之间")]
    int? Limit);
