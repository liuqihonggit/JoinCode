namespace Tools.Handlers;

/// <summary>
/// 环境感知与撤销工具处理器 — 暴露为 MCP 工具（PRD E-01/E-03/U-03）
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class EnvironmentToolHandlers
{
    private readonly IEnvironmentAwarenessService _env;
    private readonly IUndoStack _undo;
    private readonly ILogger<EnvironmentToolHandlers>? _logger;

    public EnvironmentToolHandlers(
        IEnvironmentAwarenessService env,
        IUndoStack undo,
        ILogger<EnvironmentToolHandlers>? logger = null)
    {
        _env = env;
        _undo = undo;
        _logger = logger;
    }

    /// <summary>获取当前环境状态（E-01/E-03）— 光标状态 + 弹窗检测</summary>
    [McpTool("get_environment_state", "获取当前桌面环境状态：光标状态(正常/等待/沙漏)和弹窗检测", "desktop")]
    public async Task<ToolResult> GetEnvironmentStateAsync(CancellationToken ct = default)
    {
        var cursorState = await _env.GetCursorStateAsync(ct).ConfigureAwait(false);
        var popup = await _env.DetectPopupAsync(ct).ConfigureAwait(false);

        var sb = new StringBuilder(256);
        sb.AppendLine($"光标状态: {cursorState}");
        if (cursorState is CursorState.Wait or CursorState.AppStarting)
            sb.AppendLine("（异步操作进行中，建议 wait_for_idle 等待完成）");

        if (popup is not null)
        {
            sb.AppendLine($"检测到弹窗: 「{popup.Title}」 分类={popup.Category}");
            sb.AppendLine(popup.Category switch
            {
                PopupCategory.Closeable => "（可自主关闭）",
                PopupCategory.NeedsDecision => "（需用户决策，应暂停等待确认）",
                PopupCategory.Retryable => "（可重试）",
                _ => string.Empty
            });
        }
        else
        {
            sb.AppendLine("无弹窗");
        }

        sb.AppendLine($"撤销栈深度: {_undo.Count}");

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>等待异步操作完成（E-03）— 光标恢复空闲或超时</summary>
    [McpTool("wait_for_idle", "等待桌面异步操作完成（光标恢复空闲），避免固定等待", "desktop")]
    public async Task<ToolResult> WaitForIdleAsync(
        [McpToolParameter("最大等待秒数", Required = false)] int timeoutSeconds = 10,
        CancellationToken ct = default)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 120));
        var idle = await _env.WaitForIdleAsync(timeout, ct).ConfigureAwait(false);

        var msg = idle
            ? $"桌面已恢复空闲（等待内完成）"
            : $"等待超时（{timeoutSeconds}s 后仍未空闲）";

        return ToolResultBuilder.Success().WithText(msg).Build();
    }

    /// <summary>撤销上一步操作（U-03）</summary>
    [McpTool("undo_last_action", "撤销上一步桌面操作，返回被撤销的操作信息", "desktop")]
    public Task<ToolResult> UndoLastActionAsync(CancellationToken ct = default)
    {
        var popped = _undo.Pop();

        if (popped is null)
            return Task.FromResult(ToolResultBuilder.Success().WithText("撤销栈为空，无可撤销操作").Build());

        var sb = new StringBuilder(128);
        sb.AppendLine($"已撤销: {popped.Kind}");
        sb.AppendLine($"  坐标: ({popped.X}, {popped.Y})");
        if (popped.Text is not null)
            sb.AppendLine($"  文本: {popped.Text}");
        if (popped.MouseAction is not null)
            sb.AppendLine($"  鼠标动作: {popped.MouseAction}");
        if (popped.Modifiers is not null)
            sb.AppendLine($"  修饰键: {popped.Modifiers}");
        sb.AppendLine($"  时间: {popped.Timestamp:HH:mm:ss.fff}");
        sb.AppendLine($"  结果: {(popped.Succeeded ? "成功" : $"失败({popped.Error})")}");
        sb.AppendLine($"  剩余可撤销: {_undo.Count} 步");

        return Task.FromResult(ToolResultBuilder.Success().WithText(sb.ToString()).Build());
    }

    /// <summary>获取操作历史（U-03）— 查看最近 N 步操作记录</summary>
    [McpTool("get_operation_history", "获取最近N步桌面操作历史记录", "desktop")]
    public Task<ToolResult> GetOperationHistoryAsync(
        [McpToolParameter("查看最近几步（默认10）", Required = false)] int count = 10,
        CancellationToken ct = default)
    {
        var history = _undo.GetRecent(count);

        if (history.Count == 0)
            return Task.FromResult(ToolResultBuilder.Success().WithText("操作历史为空").Build());

        var sb = new StringBuilder(256);
        sb.AppendLine($"最近 {history.Count} 步操作（倒序）:");
        for (var i = 0; i < history.Count; i++)
        {
            var op = history[i];
            sb.AppendLine($"  [{i + 1}] {op.Kind} @ ({op.X},{op.Y})" +
                (op.Text is not null ? $" text=\"{op.Text}\"" : string.Empty) +
                $" {(op.Succeeded ? "✓" : "✗")}" +
                $" {op.Timestamp:HH:mm:ss}");
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(sb.ToString()).Build());
    }
}
