namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具执行失败的通用结构化诊断信息。
/// 所有工具在失败路径可返回此对象，GUI 可根据 Reason/Details/Suggestions 分区域渲染。
/// FormattedMessage 保持向后兼容 ErrorMessage 字符串。
/// </summary>
public sealed record ToolDiagnostic
{
    /// <summary>
    /// 诊断原因分类（工具特定，如 "StringNotFound", "PartialMatch", "WhitespaceMismatch", "SimilarFound", "NoResults", "UnknownSetting", "ContextMismatch", "CellNotFound"）。
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// 结构化详情键值对列表（行号、pattern、path、相似度等）。
    /// GUI 可遍历此列表渲染为表格或键值对。
    /// </summary>
    public IReadOnlyList<DiagnosticDetail> Details { get; init; } = [];

    /// <summary>
    /// 用户可操作的建议列表。
    /// 每条建议是一个完整的可操作提示（如 "使用 **/ 前缀递归搜索子目录"）。
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    /// <summary>
    /// 完整格式化消息（向后兼容 ErrorMessage 字符串）。
    /// 包含原因 + 详情 + 建议的完整文本表示。
    /// </summary>
    public required string FormattedMessage { get; init; }

    /// <summary>
    /// 快速创建诊断信息。
    /// </summary>
    public static ToolDiagnostic Create(
        string reason,
        string formattedMessage,
        IReadOnlyList<DiagnosticDetail>? details = null,
        IReadOnlyList<string>? suggestions = null) => new()
    {
        Reason = reason,
        FormattedMessage = formattedMessage,
        Details = details ?? [],
        Suggestions = suggestions ?? [],
    };

    /// <summary>
    /// 创建带单条详情的诊断信息。
    /// </summary>
    public static ToolDiagnostic Create(
        string reason,
        string formattedMessage,
        string detailKey,
        string detailValue,
        params string[] suggestions) => new()
    {
        Reason = reason,
        FormattedMessage = formattedMessage,
        Details = [new DiagnosticDetail(detailKey, detailValue)],
        Suggestions = suggestions,
    };
}

/// <summary>
/// 诊断详情键值对。
/// </summary>
public sealed record DiagnosticDetail(string Key, string Value);
