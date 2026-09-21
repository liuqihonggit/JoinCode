namespace JccAuditCli;

/// <summary>
/// 单条诊断记录
/// </summary>
public sealed record AuditDiagnostic {
    /// <summary>获取规则标识。</summary>
    public string RuleId { get; init; } = string.Empty;
    /// <summary>获取严重级别。</summary>
    public string Severity { get; init; } = string.Empty;
    /// <summary>获取诊断消息。</summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>获取文件路径。</summary>
    public string FilePath { get; init; } = string.Empty;
    /// <summary>获取行号。</summary>
    public int Line { get; init; }
    /// <summary>获取列号。</summary>
    public int Column { get; init; }
    /// <summary>获取类别。</summary>
    public string Category { get; init; } = string.Empty;
}

/// <summary>
/// 项目审计结果
/// </summary>
public sealed record ProjectAuditResult {
    /// <summary>获取项目名称。</summary>
    public string ProjectName { get; init; } = string.Empty;
    /// <summary>获取项目路径。</summary>
    public string ProjectPath { get; init; } = string.Empty;
    /// <summary>获取诊断总数。</summary>
    public int TotalDiagnostics { get; init; }
    /// <summary>获取警告数。</summary>
    public int WarningCount { get; init; }
    /// <summary>获取错误数。</summary>
    public int ErrorCount { get; init; }
    /// <summary>获取信息数。</summary>
    public int InfoCount { get; init; }
    /// <summary>获取诊断列表。</summary>
    public List<AuditDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// 审计报告
/// </summary>
public sealed record AuditReport {
    /// <summary>获取目标路径。</summary>
    public string TargetPath { get; init; } = string.Empty;
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    /// <summary>获取项目总数。</summary>
    public int TotalProjects { get; init; }
    /// <summary>获取诊断总数。</summary>
    public int TotalDiagnostics { get; init; }
    /// <summary>获取项目审计结果列表。</summary>
    public List<ProjectAuditResult> Projects { get; init; } = [];
}

/// <summary>
/// JSON 序列化上下文（AOT 兼容）
/// </summary>
[JsonSerializable(typeof(AuditReport))]
[JsonSerializable(typeof(ProjectAuditResult))]
[JsonSerializable(typeof(AuditDiagnostic))]
[JsonSerializable(typeof(List<ProjectAuditResult>))]
[JsonSerializable(typeof(List<AuditDiagnostic>))]
[JsonSerializable(typeof(ConstructorParamInfo))]
[JsonSerializable(typeof(List<ConstructorParamInfo>))]
[JsonSerializable(typeof(ConstructorParamReport))]
[JsonSerializable(typeof(FileInfoEntry))]
[JsonSerializable(typeof(List<FileInfoEntry>))]
[JsonSerializable(typeof(FileLineReport))]
[JsonSerializable(typeof(DisposableConsistencyInfo))]
[JsonSerializable(typeof(List<DisposableConsistencyInfo>))]
[JsonSerializable(typeof(LayerViolationInfo))]
[JsonSerializable(typeof(List<LayerViolationInfo>))]
[JsonSerializable(typeof(LayerAuditReport))]
[JsonSerializable(typeof(BomStripEntry))]
[JsonSerializable(typeof(List<BomStripEntry>))]
[JsonSerializable(typeof(BomStripReport))]
public sealed partial class AuditReportContext : JsonSerializerContext;

/// <summary>
/// 构造函数参数计数审计报告
/// </summary>
public sealed record ConstructorParamReport {
    /// <summary>获取目标路径。</summary>
    public string TargetPath { get; init; } = string.Empty;
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    /// <summary>获取参数数量阈值。</summary>
    public int Threshold { get; init; }
    /// <summary>获取胖构造函数总数。</summary>
    public int TotalFatCtors { get; init; }
    /// <summary>获取构造函数列表。</summary>
    public List<ConstructorParamInfo> Constructors { get; init; } = [];
}

/// <summary>
/// 单个文件的行数信息
/// </summary>
public sealed record FileInfoEntry {
    /// <summary>获取文件相对路径。</summary>
    public string FilePath { get; init; } = string.Empty;
    /// <summary>获取文件完整路径。</summary>
    public string FullPath { get; init; } = string.Empty;
    /// <summary>获取文件行数。</summary>
    public int LineCount { get; init; }
}

/// <summary>
/// 文件行数统计报告（Top N 大文件）
/// </summary>
public sealed record FileLineReport {
    /// <summary>获取根路径。</summary>
    public string RootPath { get; init; } = string.Empty;
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    /// <summary>获取 Top N 数量。</summary>
    public int TopN { get; init; }
    /// <summary>获取行数阈值。</summary>
    public int Threshold { get; init; }
    /// <summary>获取 CS 文件总数。</summary>
    public int TotalCsFiles { get; init; }
    /// <summary>获取跳过的文件数。</summary>
    public int SkippedFiles { get; init; }
    /// <summary>获取超过阈值的文件数。</summary>
    public int FilesAboveThreshold { get; init; }
    /// <summary>获取文件信息列表。</summary>
    public List<FileInfoEntry> Files { get; init; } = [];
}

/// <summary>
/// 单个 BOM 移除记录
/// </summary>
public sealed record BomStripEntry {
    /// <summary>获取文件相对路径。</summary>
    public string FilePath { get; init; } = string.Empty;
    /// <summary>获取文件完整路径。</summary>
    public string FullPath { get; init; } = string.Empty;
}

/// <summary>
/// UTF-8 BOM 移除报告
/// </summary>
public sealed record BomStripReport {
    /// <summary>获取根路径。</summary>
    public string RootPath { get; init; } = string.Empty;
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    /// <summary>获取 CS 文件总数。</summary>
    public int TotalCsFiles { get; init; }
    /// <summary>获取跳过的文件数。</summary>
    public int SkippedFiles { get; init; }
    /// <summary>获取已扫描的文件数。</summary>
    public int ScannedFiles { get; init; }
    /// <summary>获取含 BOM 的文件数。</summary>
    public int WithBomCount { get; init; }
    /// <summary>获取已移除 BOM 的文件数。</summary>
    public int StrippedCount { get; init; }
    /// <summary>获取是否试运行。</summary>
    public bool DryRun { get; init; }
    /// <summary>获取是否跳过测试。</summary>
    public bool SkipTests { get; init; }
    /// <summary>获取 BOM 移除记录列表。</summary>
    public List<BomStripEntry> Files { get; init; } = [];
}