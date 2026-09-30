namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 工具转发 partial — GUI 按钮直接调用引擎工具（任务5）。
/// 绕过 AI/聊天流，经 IJccChatSession.ExecuteToolAsync 委托到引擎 IToolRegistry。
/// 参数格式：纯工具名（如 "git_status"）或 工具名:JSON参数（如 "file_read:{\"path\":\"a.txt\"}"）。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>
    /// GUI 按钮直接执行引擎工具 — 解析参数 → 调用 ExecuteToolAsync → 结果回显为系统消息。
    /// 参数格式：toolName 或 toolName:{"key":"value"}。工具不存在时回显错误提示。
    /// </summary>
    [RelayCommand]
    private async Task ExecuteToolAsync(string? parameter) {
        if (string.IsNullOrWhiteSpace(parameter))
            return;

        var (toolName, arguments) = ParseToolParameter(parameter);
        AddSystemMessage($"🔧 {toolName}…");

        try {
            var result = await _session.ExecuteToolAsync(toolName, arguments);
            var text = ExtractToolResultText(result);
            var prefix = result.IsError ? "⚠" : "✓";
            AddSystemMessage(string.IsNullOrWhiteSpace(text)
                ? $"{prefix} {toolName} 完成（无输出）"
                : $"{prefix} {toolName}\n{text}");
        } catch (Exception ex) {
            AddSystemMessage($"⚠ {toolName} 执行失败: {ex.Message}");
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }

    /// <summary>解析工具参数字符串 — toolName 或 toolName:{"key":"value"} 格式</summary>
    internal static (string ToolName, Dictionary<string, JsonElement> Arguments) ParseToolParameter(string parameter) {
        var colonIdx = parameter.IndexOf(':');
        if (colonIdx < 0)
            return (parameter.Trim(), []);

        var toolName = parameter.AsSpan(0, colonIdx).Trim().ToString();
        var jsonSpan = parameter.AsSpan(colonIdx + 1).Trim();
        if (jsonSpan.IsEmpty)
            return (toolName, []);

        try {
            using var doc = JsonDocument.Parse(jsonSpan.ToString());
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
                args[prop.Name] = prop.Value.Clone();
            return (toolName, args);
        } catch (JsonException) {
            return (toolName, []);
        }
    }

    /// <summary>从 ToolResult 提取文本 — 拼接所有 Content 的 Text 字段</summary>
    internal static string ExtractToolResultText(ToolResult result) {
        if (result.Content is null or [])
            return string.Empty;
        var sb = new StringBuilder();
        foreach (var c in result.Content) {
            if (!string.IsNullOrEmpty(c.Text))
                sb.AppendLine(c.Text);
        }
        return sb.ToString().TrimEnd();
    }
}
