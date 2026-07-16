namespace Tools.Shell;

/// <summary>
/// Shell 中间件管道上下文 — 在管道各阶段间传递状态
/// 与 ShellCommandContext（进程执行上下文）区分：本类是纯 DTO，仅携带参数和中间件间状态
/// </summary>
public sealed class ShellPipelineContext
{
    // === 输入 ===

    /// <summary>
    /// 要执行的命令
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// 是否为 PowerShell 命令
    /// </summary>
    public required bool IsPowerShell { get; init; }

    /// <summary>
    /// 命令描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    public int? Timeout { get; init; }

    /// <summary>
    /// 工作目录
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 是否在后台运行
    /// </summary>
    public bool? Background { get; init; }

    /// <summary>
    /// 是否允许超时自动后台化
    /// </summary>
    public bool? AutoBackground { get; init; }

    /// <summary>
    /// 是否禁用沙箱
    /// </summary>
    public bool? DangerouslyDisableSandbox { get; init; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// 进度回调
    /// </summary>
    public ToolProgressCallback? OnProgress { get; init; }

    // === ShellValidationMiddleware 填充 ===

    /// <summary>
    /// 验证错误信息 — 由 ShellValidationMiddleware 设置，非 null 表示验证失败
    /// </summary>
    public string? ValidationError { get; set; }

    // === ShellClassificationMiddleware 填充 ===

    /// <summary>
    /// 分类错误结果 — 由 ShellClassificationMiddleware 设置，非 null 表示命令被拦截
    /// </summary>
    public ToolResult? ClassificationError { get; set; }

    // === ShellSedInterceptMiddleware 填充 ===

    /// <summary>
    /// sed 拦截结果 — 由 ShellSedInterceptMiddleware 设置，非 null 表示命令被 sed 拦截
    /// </summary>
    public ToolResult? SedResult { get; set; }

    // === ShellBackgroundMiddleware 填充 ===

    /// <summary>
    /// 后台任务结果 — 由 ShellBackgroundMiddleware 设置，非 null 表示命令已作为后台任务创建
    /// </summary>
    public ToolResult? BackgroundResult { get; set; }

    // === ShellExecutionMiddleware 填充 ===

    /// <summary>
    /// 执行结果 — 由 ShellExecutionMiddleware 设置
    /// </summary>
    public ShellExecutionResult? ExecutionResult { get; set; }

    // === ShellOutputMiddleware 填充 ===

    /// <summary>
    /// 最终结果 — 由 ShellOutputMiddleware 设置
    /// </summary>
    public ToolResult? Result { get; set; }
}
