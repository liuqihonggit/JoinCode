
namespace JoinCode.Abstractions.Commands;

public sealed record ExecuteWorkflowCommand(
    [Required(ErrorMessage = "task 不能为空")]
    [StringLength(1000, ErrorMessage = "任务描述过长")]
    string Task);

public sealed record CreatePlanCommand(
    [Required(ErrorMessage = "prompt 不能为空")]
    [StringLength(2000, ErrorMessage = "提示词过长")]
    string Prompt);

public sealed record GenerateCodeCommand(
    [Required(ErrorMessage = "requirement 不能为空")]
    [StringLength(2000, ErrorMessage = "需求描述过长")]
    string Requirement);

public sealed record AnalyzeCodeCommand(
    [Required(ErrorMessage = "code 不能为空")]
    [StringLength(50000, ErrorMessage = "代码过长")]
    string Code,
    [StringLength(50, ErrorMessage = "分析类型过长")]
    string AnalysisType = "general");

public sealed record ChatCommand(
    [Required(ErrorMessage = "message 不能为空")]
    [StringLength(10000, ErrorMessage = "消息过长")]
    string Message);
