namespace JccAuditCli;

/// <summary>
/// 单条诊断记录
/// </summary>
public sealed record AuditDiagnostic
{
    public string RuleId { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public string Category { get; init; } = string.Empty;
}

/// <summary>
/// 项目审计结果
/// </summary>
public sealed record ProjectAuditResult
{
    public string ProjectName { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public int TotalDiagnostics { get; init; }
    public int WarningCount { get; init; }
    public int ErrorCount { get; init; }
    public int InfoCount { get; init; }
    public List<AuditDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// 审计报告
/// </summary>
public sealed record AuditReport
{
    public string TargetPath { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int TotalProjects { get; init; }
    public int TotalDiagnostics { get; init; }
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
public sealed partial class AuditReportContext : JsonSerializerContext;

/// <summary>
/// 构造函数参数计数审计报告
/// </summary>
public sealed record ConstructorParamReport
{
    public string TargetPath { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int Threshold { get; init; }
    public int TotalFatCtors { get; init; }
    public List<ConstructorParamInfo> Constructors { get; init; } = [];
}

/// <summary>
/// 单个文件的行数信息
/// </summary>
public sealed record FileInfoEntry
{
    public string FilePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public int LineCount { get; init; }
}

/// <summary>
/// 文件行数统计报告（Top N 大文件）
/// </summary>
public sealed record FileLineReport
{
    public string RootPath { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int TopN { get; init; }
    public int Threshold { get; init; }
    public int TotalCsFiles { get; init; }
    public int SkippedFiles { get; init; }
    public int FilesAboveThreshold { get; init; }
    public List<FileInfoEntry> Files { get; init; } = [];
}
