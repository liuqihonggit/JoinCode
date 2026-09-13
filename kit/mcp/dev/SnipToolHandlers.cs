

namespace McpToolDispatch;

/// <summary>
/// 上下文裁剪工具处理器 — 提供对话历史的回退、回退到指定位置、清空等操作以释放上下文窗口空间
/// </summary>
[McpToolDispatch(ToolCategory.Snip, Optional = true)]
public partial class SnipToolHandlers
{
    private readonly IChatContextManager _contextManager;
    private readonly ILogger<SnipToolHandlers>? _logger;

    /// <summary>
    /// 初始化 <see cref="SnipToolHandlers"/> 实例
    /// </summary>
    /// <param name="contextManager">聊天上下文管理器</param>
    /// <param name="logger">日志记录器（可选）</param>
    public SnipToolHandlers(IChatContextManager contextManager, ILogger<SnipToolHandlers>? logger = null)
    {
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _logger = logger;
    }

    /// <summary>
    /// 裁剪对话历史消息以释放上下文窗口空间
    /// </summary>
    /// <param name="mode">裁剪模式：rewind/rewind_to/clear（默认 rewind）</param>
    /// <param name="message_index">消息索引（rewind_to 模式下使用）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameConstants.Snip, "Snip old messages from conversation history to free context window space", "context")]
    public async Task<ToolResult> SnipHistoryAsync(
        [McpToolParameter("Snip mode: rewind/rewind_to/clear (default rewind)", Required = false)] string? mode = "rewind",
        [McpToolParameter("Message index (used in rewind_to mode)", Required = false)] int? message_index = null,
        CancellationToken cancellationToken = default)
    {
        var snipMode = SnipModeExtensions.FromValue(mode);
        if (snipMode == null)
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SnipUnknownMode, mode)).Build();

        try
        {
            var response = new System.Text.StringBuilder();

            switch (snipMode.Value)
            {
                case SnipMode.Rewind:
                    var rewindResult = await _contextManager.RewindLastTurnAsync(cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.SnipRewindSuccess));
                    response.AppendLine(L.T(StringKey.SnipRemovedCount, rewindResult.RemovedCount));
                    break;

                case SnipMode.RewindTo:
                    if (message_index == null)
                        return ToolResultBuilder.Error().WithText(L.T(StringKey.SnipRewindToRequiresIndex)).Build();
                    var rewindToResult = await _contextManager.RewindToMessageIndexAsync(message_index.Value, cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.SnipRewindToSuccess, message_index));
                    response.AppendLine(L.T(StringKey.SnipRemovedCount, rewindToResult.RemovedCount));
                    break;

                case SnipMode.Clear:
                    var clearResult = await _contextManager.RewindToStartAsync(cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.SnipClearSuccess));
                    response.AppendLine(L.T(StringKey.SnipRemovedCount, clearResult.RemovedCount));
                    break;
            }

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.SnipFailedLog));
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SnipFailed, ex.Message)).Build();
        }
    }
}
