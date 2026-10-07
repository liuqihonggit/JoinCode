namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具异常诊断辅助类 — 将未处理异常转换为结构化 ToolDiagnostic。
/// 公共版本，供所有项目（Hands、Mcp、Services 等）在顶层 catch 中复用。
/// </summary>
public static class ToolExceptionDiagnosticHelper {
    /// <summary>
    /// 从异常构建结构化诊断信息。
    /// </summary>
    /// <param name="toolName">工具名称（如 "config_get", "git_status"）</param>
    /// <param name="ex">未处理异常</param>
    /// <param name="extraDetails">额外上下文详情（如参数值）</param>
    /// <returns>结构化诊断</returns>
    public static ToolDiagnostic BuildExceptionDiagnostic(string toolName, Exception ex, params DiagnosticDetail[] extraDetails) {
        var sb = new StringBuilder(256);
        sb.Append($"{toolName} 执行失败: {ex.Message}");

        var details = new List<DiagnosticDetail>(extraDetails.Length + 2);
        details.AddRange(extraDetails);
        details.Add(new DiagnosticDetail("exceptionType", ex.GetType().Name));
        details.Add(new DiagnosticDetail("toolName", toolName));

        var suggestions = BuildExceptionSuggestions(ex);

        return ToolDiagnostic.Create(
            reason: "UnhandledException",
            formattedMessage: sb.ToString(),
            details: details,
            suggestions: suggestions);
    }

    /// <summary>
    /// 构建错误 ToolResult，同时记录日志。
    /// </summary>
    public static ToolResult BuildErrorResult(string toolName, Exception ex, ILogger? logger = null, params DiagnosticDetail[] extraDetails) {
        logger?.LogError(ex, "{ToolName} 执行异常", toolName);
        var diagnostic = BuildExceptionDiagnostic(toolName, ex, extraDetails);
        return ToolResultBuilder.Error()
            .WithText(diagnostic.FormattedMessage)
            .WithDiagnostic(diagnostic)
            .Build();
    }

    /// <summary>
    /// 构建错误 ToolResult，带一组上下文键值对。
    /// </summary>
    public static ToolResult BuildErrorResult(string toolName, Exception ex, ILogger? logger, string contextKey, string contextValue, params DiagnosticDetail[] extraDetails) {
        logger?.LogError(ex, "{ToolName} 执行异常, {ContextKey}: {ContextValue}", toolName, contextKey, contextValue);
        var allDetails = new List<DiagnosticDetail>(extraDetails.Length + 1);
        allDetails.Add(new DiagnosticDetail(contextKey, contextValue));
        allDetails.AddRange(extraDetails);
        var diagnostic = BuildExceptionDiagnostic(toolName, ex, [.. allDetails]);
        return ToolResultBuilder.Error()
            .WithText(diagnostic.FormattedMessage)
            .WithDiagnostic(diagnostic)
            .Build();
    }

    /// <summary>
    /// 构建错误 ToolResult，带两组上下文键值对。
    /// </summary>
    public static ToolResult BuildErrorResult(string toolName, Exception ex, ILogger? logger, string contextKey1, string contextValue1, string contextKey2, string contextValue2, params DiagnosticDetail[] extraDetails) {
        logger?.LogError(ex, "{ToolName} 执行异常, {ContextKey1}: {ContextValue1}, {ContextKey2}: {ContextValue2}", toolName, contextKey1, contextValue1, contextKey2, contextValue2);
        var allDetails = new List<DiagnosticDetail>(extraDetails.Length + 2);
        allDetails.Add(new DiagnosticDetail(contextKey1, contextValue1));
        allDetails.Add(new DiagnosticDetail(contextKey2, contextValue2));
        allDetails.AddRange(extraDetails);
        var diagnostic = BuildExceptionDiagnostic(toolName, ex, [.. allDetails]);
        return ToolResultBuilder.Error()
            .WithText(diagnostic.FormattedMessage)
            .WithDiagnostic(diagnostic)
            .Build();
    }

    private static List<string> BuildExceptionSuggestions(Exception ex) {
        var suggestion = ex switch {
            OperationCanceledException               => "操作被取消，可能因超时或用户主动中断",
            ArgumentException or ArgumentNullException => "检查参数是否正确传递且非空",
            IOException                              => "检查文件路径、权限和磁盘空间",
            UnauthorizedAccessException               => "检查文件/目录访问权限",
            TimeoutException                         => "操作超时，考虑增加超时时间或拆分任务",
            KeyNotFoundException or FileNotFoundException => "确认目标资源存在且路径正确",
            _                                        => "查看日志获取详细堆栈信息",
        };
        return [suggestion];
    }
}