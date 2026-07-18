namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 诊断严重度
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// 诊断规则 ID — 每条规则对应一种问题检测模式
/// </summary>
public enum DiagnosticRuleId
{
    /// <summary>循环检测：同一会话 LoopDetected ≥ 3次</summary>
    [EnumValue("D001")] LoopDetected,

    /// <summary>工具权限拒绝：同一工具 PermissionDenied ≥ 2次</summary>
    [EnumValue("D002")] ToolPermissionDenied,

    /// <summary>上下文溢出：token 使用量 > 80% 窗口</summary>
    [EnumValue("D003")] ContextOverflow,

    /// <summary>进程卡死：子进程 --await 返回 1234</summary>
    [EnumValue("D004")] ProcessHung,

    /// <summary>API 错误：API 调用连续失败 ≥ 3次</summary>
    [EnumValue("D005")] ApiError
}

/// <summary>
/// 诊断事件 — 从病人 stdout 解析出的结构化遥测事件
/// </summary>
public sealed record DiagnosticEvent
{
    /// <summary>事件类型标识（如 "loop_detected", "permission_denied", "api_error"）</summary>
    public required string EventType { get; init; }

    /// <summary>病人 ID — 标识事件来源的病人进程</summary>
    public string PatientId { get; init; } = string.Empty;

    /// <summary>事件时间戳</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>事件原始数据（NDJSON 行）</summary>
    public string? RawData { get; init; }

    /// <summary>会话 ID</summary>
    public string? SessionId { get; init; }

    /// <summary>附加属性</summary>
    public Dictionary<string, string> Properties { get; init; } = new();
}

/// <summary>
/// 诊断报告 — 一条诊断规则的检测结果
/// </summary>
public sealed record DiagnosticReport
{
    /// <summary>规则 ID</summary>
    public required DiagnosticRuleId RuleId { get; init; }

    /// <summary>病人 ID — 标识触发诊断的病人进程</summary>
    public string PatientId { get; init; } = string.Empty;

    /// <summary>严重度</summary>
    public required DiagnosticSeverity Severity { get; init; }

    /// <summary>问题描述</summary>
    public required string Description { get; init; }

    /// <summary>触发该报告的事件列表</summary>
    public IReadOnlyList<DiagnosticEvent> TriggeringEvents { get; init; } = [];

    /// <summary>建议修复动作类型</summary>
    public HotFixActionType SuggestedFixType { get; init; }

    /// <summary>建议修复描述</summary>
    public string? SuggestedFixDescription { get; init; }

    /// <summary>检测时间</summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 病人进程状态
/// </summary>
public enum PatientState
{
    /// <summary>未启动</summary>
    NotStarted,

    /// <summary>运行中</summary>
    Running,

    /// <summary>正常退出</summary>
    Completed,

    /// <summary>异常退出</summary>
    Failed,

    /// <summary>卡死（超时退出，退出码 1234）</summary>
    Hung,

    /// <summary>被医生终止</summary>
    Killed
}

/// <summary>
/// 病人进程信息
/// </summary>
public sealed record PatientInfo
{
    /// <summary>病人 ID — 唯一标识一个病人进程</summary>
    public string PatientId { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>进程 ID</summary>
    public int ProcessId { get; init; }

    /// <summary>进程状态</summary>
    public PatientState State { get; init; }

    /// <summary>退出码（进程退出后可用）</summary>
    public int? ExitCode { get; init; }

    /// <summary>启动时间</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>退出时间</summary>
    public DateTimeOffset? ExitedAt { get; init; }

    /// <summary>命令行参数</summary>
    public string? Arguments { get; init; }
}
