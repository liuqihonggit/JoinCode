namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 的前缀命令 partial — 处理 ! / !! 前缀命令（对齐 PI pi.dev 设计）。
/// 从 MainViewModel.cs 拆出以满足 JCC8001 文件行数限制。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// !! 前缀命令处理 — 静默执行/打开，输出回显到消息列表，不触发 AI（对齐 PI !! 设计）。
    /// </summary>
    private async Task HandleSilentPrefixCommandAsync(string message)
    {
        var context = new PrefixCommandContext
        {
            CancellationToken = System.Threading.CancellationToken.None,
        };
        var result = await Cli.Commands.Prefix.PrefixCommandRouter.ExecuteAsync(message, context);
        if (!result.Handled)
        {
            AddSystemMessage($"未识别的命令: {message}");
            return;
        }
        AddSystemMessage($"⚙️ {result.Output}");
    }

    /// <summary>
    /// ! 前缀命令处理 — 执行 shell 命令，输出注入 AI 上下文触发聊天流（对齐 PI ! 设计）。
    /// </summary>
    private async Task HandleShellPrefixCommandAsync(string message, System.Threading.CancellationToken ct)
    {
        var context = new PrefixCommandContext { CancellationToken = ct };
        var result = await Cli.Commands.Prefix.PrefixCommandRouter.ExecuteAsync(message, context, ct);
        if (!result.Handled || string.IsNullOrWhiteSpace(result.Output))
        {
            AddSystemMessage($"未识别的命令: {message}");
            return;
        }

        Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.System,
            Content = $"⚙️ {message}\n{result.Output}",
            Timestamp = DateTime.Now,
        });

        Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.User,
            Content = result.Output,
            Timestamp = DateTime.Now,
        });

        _turnProcessor = new ChatTurnProcessor(Messages);
        _turnProcessor.BeginTurn();
        var processor = _turnProcessor;

        await foreach (var evt in _session.StreamAsync(result.Output, ct))
        {
            RunStatus.ReportActivity(
                hasActiveTool: evt.Type == ChatStreamEventType.ToolCallStart,
                label: evt.Type == ChatStreamEventType.ToolCallStart ? evt.ToolName : null);
            if (evt.Type == ChatStreamEventType.Complete && evt.Usage is not null)
                RunStatus.AddTokens(evt.Usage.TotalTokens);
            processor.Process(evt, StreamingEnabled);
        }

        processor.CompleteTurn(StreamingEnabled);
        TokenUsageText = processor.TotalTokens > 0 ? $"Token:{processor.TotalTokens:N0}" : string.Empty;
    }
}
