
namespace JoinCode.Abstractions.Commands;

/// <summary>
/// 创建任务命令
/// </summary>
public sealed record TaskCreateCommand(
    [Required(ErrorMessage = "title 不能为空")]
    [StringLength(200, ErrorMessage = "标题过长")]
    string Title,
    [StringLength(2000, ErrorMessage = "描述过长")]
    string? Description,
    [StringLength(100, ErrorMessage = "负责人名称过长")]
    string? Assignee,
    DateTime? DueDate,
    [StringLength(20, ErrorMessage = "优先级过长")]
    string Priority,
    List<string>? Tags);

/// <summary>
/// 列出任务命令
/// </summary>
public sealed record TaskListCommand(
    [StringLength(20, ErrorMessage = "状态过长")]
    string? Status,
    [StringLength(100, ErrorMessage = "负责人名称过长")]
    string? Assignee,
    [StringLength(20, ErrorMessage = "优先级过长")]
    string? Priority,
    [Range(1, 1000, ErrorMessage = "limit 必须在 1-1000 之间")]
    int? Limit,
    [Range(0, int.MaxValue, ErrorMessage = "offset 不能为负数")]
    int? Offset);

/// <summary>
/// 更新任务命令
/// </summary>
public sealed record TaskUpdateCommand(
    [Required(ErrorMessage = "task_id 不能为空")]
    [StringLength(50, ErrorMessage = "任务ID过长")]
    string TaskId,
    [StringLength(200, ErrorMessage = "标题过长")]
    string? Title,
    [StringLength(2000, ErrorMessage = "描述过长")]
    string? Description,
    [StringLength(20, ErrorMessage = "状态过长")]
    string? Status,
    [StringLength(100, ErrorMessage = "负责人名称过长")]
    string? Assignee,
    DateTime? DueDate,
    [StringLength(20, ErrorMessage = "优先级过长")]
    string? Priority,
    List<string>? Tags);

/// <summary>
/// 停止任务命令
/// </summary>
public sealed record TaskStopCommand(
    [Required(ErrorMessage = "task_id 不能为空")]
    [StringLength(50, ErrorMessage = "任务ID过长")]
    string TaskId,
    [StringLength(500, ErrorMessage = "原因描述过长")]
    string? Reason);

/// <summary>
/// 获取任务命令
/// </summary>
public sealed record TaskGetCommand(
    [Required(ErrorMessage = "task_id 不能为空")]
    [StringLength(50, ErrorMessage = "任务ID过长")]
    string TaskId);

/// <summary>
/// 设置任务依赖命令
/// </summary>
public sealed record TaskSetDependencyCommand(
    [Required(ErrorMessage = "task_id 不能为空")]
    [StringLength(50, ErrorMessage = "任务ID过长")]
    string TaskId,
    [Required(ErrorMessage = "depends_on_task_id 不能为空")]
    [StringLength(50, ErrorMessage = "依赖任务ID过长")]
    string DependsOnTaskId,
    [StringLength(20, ErrorMessage = "依赖类型过长")]
    string? DependencyType);

/// <summary>
/// 移除任务依赖命令
/// </summary>
public sealed record TaskRemoveDependencyCommand(
    [Required(ErrorMessage = "task_id 不能为空")]
    [StringLength(50, ErrorMessage = "任务ID过长")]
    string TaskId,
    [Required(ErrorMessage = "depends_on_task_id 不能为空")]
    [StringLength(50, ErrorMessage = "依赖任务ID过长")]
    string DependsOnTaskId);

/// <summary>
/// 获取任务依赖命令
/// </summary>
public sealed record TaskGetDependenciesCommand(
    [Required(ErrorMessage = "task_id 不能为空")]
    [StringLength(50, ErrorMessage = "任务ID过长")]
    string TaskId);

/// <summary>
/// 检查任务是否可以执行命令
/// </summary>
public sealed record TaskCanExecuteCommand(
    [Required(ErrorMessage = "task_id 不能为空")]
    [StringLength(50, ErrorMessage = "任务ID过长")]
    string TaskId);
