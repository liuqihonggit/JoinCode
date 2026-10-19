namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 事件 → REPL 视图状态应用器 — ADR 0141 事件语义的唯一翻译层：
/// 回合生命周期驱动 Busy + SpinnerItem（BeginTurn/EndTurn），消息事件插入滚动区
/// （spinner 恒为末项），token_delta 累加 ResponseLength，错误追加 ErrorMessageItem。
/// 模拟路径与真实 bridge 路径共用同一套回合视图状态。
/// </summary>
public sealed class ReplBridgeDriver
{
    private readonly ReplView _view;
    private readonly SpinnerItem _spinner;

    /// <summary>创建应用器（view 与 spinner 由调用方构造并挂载，spinner 须为 Messages 末项）。</summary>
    public ReplBridgeDriver(ReplView view, SpinnerItem spinner)
    {
        _view = view;
        _spinner = spinner;
    }

    /// <summary>回合进行中（turn_started 后未 completed/interrupted）。</summary>
    public bool IsBusy { get; private set; }

    /// <summary>应用一条事件（全量分派 — switch 模式匹配穷尽由编译器保证）。</summary>
    public void Apply(BridgeEvent bridgeEvent)
    {
        switch (bridgeEvent)
        {
            case BridgeEvent.TurnStarted:
                BeginTurn();
                break;
            case BridgeEvent.ThinkingStarted:
                _spinner.Mode = SpinnerMode.Thinking;
                _spinner.ResetVerb();
                break;
            case BridgeEvent.ThinkingEnded:
                // 思考时长由 ThinkingLifecycle 内部按墙钟计时（M6d）；
                // durationMs 供引擎侧比对，视图侧模式切换由后续事件（tool_call/token_delta）驱动。
                break;
            case BridgeEvent.ToolCall(var name, var args):
                AddMessage(new ToolCallItem(name, args));
                _spinner.Mode = SpinnerMode.ToolUse;
                _spinner.HasActiveTools = true;
                break;
            case BridgeEvent.ToolResult(var summary):
                AddMessage(new ToolResultItem(summary));
                _spinner.HasActiveTools = false;
                break;
            case BridgeEvent.TokenDelta(var length):
                _spinner.Mode = SpinnerMode.Responding;
                _spinner.HasActiveTools = false;
                _spinner.ResponseLength += length;
                break;
            case BridgeEvent.AssistantMessage(var text):
                AddMessage(new AssistantMessageItem(text));
                break;
            case BridgeEvent.TurnCompleted:
                EndTurn();
                break;
            case BridgeEvent.TurnInterrupted:
                EndTurn();
                break;
            case BridgeEvent.Error(var message):
                AddMessage(new ErrorMessageItem(message));
                break;
        }
    }

    /// <summary>开始回合：busy + spinner 挂载（Thinking 乐观模式，动词重采样，停滞计时重置）。</summary>
    public void BeginTurn()
    {
        IsBusy = true;
        _view.Busy = true;
        _spinner.Message = string.Empty;
        _spinner.ResetVerb();
        _spinner.Mode = SpinnerMode.Thinking;
        _spinner.LoadingStartMs = Environment.TickCount64;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
    }

    /// <summary>结束回合：idle + spinner 复位（消息由事件驱动，interrupted 不额外追加）。</summary>
    public void EndTurn()
    {
        IsBusy = false;
        _view.Busy = false;
        _spinner.Message = string.Empty;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
    }

    /// <summary>消息插入 spinner 之前（spinner 恒为滚动区最后一项）。</summary>
    public void AddMessage(IScrollItem item) =>
        _view.Messages.Insert(_view.Messages.Count - 1, item);
}
