namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具错误 JSON 构建器 — 统一构建、序列化和转换为 ToolResult 的便捷工具。
/// 所有工具执行错误产生点（中间件、网关、编排器）都应通过此构建器生成错误结果，
/// 确保 AI 收到的错误信息是结构化 JSON 而非纯文本。
/// </summary>
public static class ToolErrorJsonBuilder {
    /// <summary>
    /// 构建 ToolErrorJson 对象。
    /// </summary>
    /// <param name="type">错误类型枚举</param>
    /// <param name="message">错误消息</param>
    /// <param name="tool">工具名称（可选）</param>
    /// <param name="details">结构化详情（可选）</param>
    /// <param name="suggestions">可操作建议（可选）</param>
    /// <param name="retryable">是否可重试</param>
    public static ToolErrorJson Build(
        ToolErrorType type,
        string message,
        string? tool = null,
        IReadOnlyList<DiagnosticDetail>? details = null,
        IReadOnlyList<string>? suggestions = null,
        bool retryable = false) => new() {
            Type = type.ToValue(),
            Message = message,
            Tool = tool,
            Details = details ?? [],
            Suggestions = suggestions ?? [],
            Retryable = retryable,
        };

    /// <summary>
    /// 序列化 ToolErrorJson 为 JSON 字符串。
    /// </summary>
    public static string Serialize(ToolErrorJson error) =>
        JsonSerializer.Serialize(error, ToolErrorJsonContext.Default.ToolErrorJson);

    /// <summary>
    /// 构建 + 序列化一步完成，返回 JSON 字符串。
    /// </summary>
    public static string BuildJson(
        ToolErrorType type,
        string message,
        string? tool = null,
        IReadOnlyList<DiagnosticDetail>? details = null,
        IReadOnlyList<string>? suggestions = null,
        bool retryable = false) =>
        Serialize(Build(type, message, tool, details, suggestions, retryable));

    /// <summary>
    /// 构建 ToolErrorJson 并转换为 ToolResult（含 Diagnostic）。
    /// </summary>
    public static ToolResult ToToolResult(
        ToolErrorType type,
        string message,
        string? tool = null,
        IReadOnlyList<DiagnosticDetail>? details = null,
        IReadOnlyList<string>? suggestions = null,
        bool retryable = false) {
        var error = Build(type, message, tool, details, suggestions, retryable);
        var json = Serialize(error);
        return new ToolResult {
            Content = [new ToolContent { Type = ToolContentType.Text, Text = json }],
            IsError = true,
            Diagnostic = ToolDiagnostic.Create(
                error.Type,
                message,
                details,
                suggestions),
        };
    }

    /// <summary>
    /// 从异常构建 ToolResult — 用于 catch (Exception ex) 路径。
    /// </summary>
    public static ToolResult FromException(
        Exception ex,
        string? tool = null,
        ToolErrorType type = ToolErrorType.ExecutionFailed,
        bool retryable = false) => ToToolResult(
            type,
            $"{ex.GetType().Name}: {ex.Message}",
            tool,
            retryable: retryable);

    /// <summary>
    /// 从 OperationCanceledException 构建 ToolResult — 不再 rethrow，转为 JSON。
    /// </summary>
    public static ToolResult FromCancellation(
        string? tool = null) => ToToolResult(
            ToolErrorType.Cancelled,
            "工具执行被取消",
            tool);
}
