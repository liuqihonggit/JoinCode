namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 修复动作类型
/// </summary>
public enum HotFixActionType
{
    /// <summary>无需修复</summary>
    None,

    /// <summary>修改源码 + 重编译 + 重启</summary>
    SourceCodePatch,

    /// <summary>修改配置文件 + 热更新</summary>
    ConfigChange,

    /// <summary>发送 /compact 指令压缩上下文</summary>
    CompactContext,

    /// <summary>重启病人进程（不修改代码）</summary>
    RestartProcess
}

/// <summary>
/// 修复动作 — 描述一次修复操作
/// </summary>
public sealed record HotFixAction
{
    /// <summary>修复类型</summary>
    public required HotFixActionType ActionType { get; init; }

    /// <summary>修复描述</summary>
    public required string Description { get; init; }

    /// <summary>目标文件路径（SourceCodePatch / ConfigChange 时使用）</summary>
    public string? TargetFilePath { get; init; }

    /// <summary>原始内容（用于回滚）</summary>
    public string? OriginalContent { get; init; }

    /// <summary>修改后内容</summary>
    public string? PatchedContent { get; init; }

    /// <summary>发送给病人的指令（CompactContext 时使用）</summary>
    public string? CommandToSend { get; init; }
}

/// <summary>
/// 修复结果
/// </summary>
public sealed record HotFixResult
{
    /// <summary>是否成功</summary>
    public required bool Success { get; init; }

    /// <summary>病人 ID — 标识修复的目标病人</summary>
    public string PatientId { get; init; } = string.Empty;

    /// <summary>修复动作</summary>
    public required HotFixAction Action { get; init; }

    /// <summary>结果描述</summary>
    public string? Description { get; init; }

    /// <summary>是否已回滚</summary>
    public bool WasRolledBack { get; init; }

    /// <summary>编译输出（SourceCodePatch 时使用）</summary>
    public string? BuildOutput { get; init; }

    /// <summary>执行耗时</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// 医生报告 — 一次完整的诊断-修复循环的结果
/// </summary>
public sealed record DoctorReport
{
    /// <summary>报告 ID</summary>
    public string ReportId { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>病人信息字典（PatientId → PatientInfo）</summary>
    public IReadOnlyDictionary<string, PatientInfo> Patients { get; init; } = new Dictionary<string, PatientInfo>();

    /// <summary>病人信息（单病人兼容，取第一个）</summary>
    public PatientInfo? Patient => Patients.Values.FirstOrDefault();

    /// <summary>诊断报告列表</summary>
    public IReadOnlyList<DiagnosticReport> Diagnostics { get; init; } = [];

    /// <summary>修复结果列表</summary>
    public IReadOnlyList<HotFixResult> FixResults { get; init; } = [];

    /// <summary>测试结果列表</summary>
    public IReadOnlyList<DoctorTestCaseResult> TestResults { get; init; } = [];

    /// <summary>开始时间</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>结束时间</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>总体状态</summary>
    public DoctorReportStatus Status { get; init; }
}

/// <summary>
/// 医生报告状态
/// </summary>
public enum DoctorReportStatus
{
    Running,
    Completed,
    Failed,
    PartiallyFixed
}

/// <summary>
/// 测试用例结果
/// </summary>
public sealed record DoctorTestCaseResult
{
    /// <summary>测试用例 ID</summary>
    public required string TestCaseId { get; init; }

    /// <summary>测试名称</summary>
    public required string TestName { get; init; }

    /// <summary>测试结果</summary>
    public required DoctorTestStatus Status { get; init; }

    /// <summary>执行耗时</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>错误信息（失败时）</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 测试用例状态
/// </summary>
public enum DoctorTestStatus
{
    Pass,
    Fail,
    Hung,
    Error,
    Skipped
}
