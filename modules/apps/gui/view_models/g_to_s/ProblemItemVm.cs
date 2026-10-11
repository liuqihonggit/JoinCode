namespace JoinCode.Gui.ViewModels;

/// <summary>问题严重级别 — 对齐编译器 error/warning/info</summary>
public enum ProblemSeverity {
    /// <summary>错误（编译失败）</summary>
    Error,
    /// <summary>警告（可编译但有风险）</summary>
    Warning,
    /// <summary>信息（提示性）</summary>
    Info
}

/// <summary>
/// 问题项视图模型 — 表示一条编译错误/警告/lint 信息，显示在底部面板问题 tab。
/// </summary>
public sealed class ProblemItemVm {
    /// <summary>严重级别（Error/Warning/Info）</summary>
    public required ProblemSeverity Severity { get; init; }

    /// <summary>问题消息文本</summary>
    public required string Message { get; init; }

    /// <summary>源文件路径（相对或绝对）</summary>
    public required string File { get; init; }

    /// <summary>行号（1-based）</summary>
    public required int Line { get; init; }

    /// <summary>列号（1-based）</summary>
    public required int Column { get; init; }

    /// <summary>显示文本（file(line,col): severity message）</summary>
    public string DisplayText => $"{File}({Line},{Column}): {Severity.ToString().ToLowerInvariant()} {Message}";

    /// <summary>严重级别文本（error/warning/info，用于颜色绑定）</summary>
    public string SeverityText => Severity switch {
        ProblemSeverity.Error => "error",
        ProblemSeverity.Warning => "warning",
        _ => "info"
    };
}
