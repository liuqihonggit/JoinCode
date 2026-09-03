namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 子代理事件通道 — 主对话管道（QueryLoop）与子代理执行中间件（AgentStreamExecution）之间的
/// 跨层实时事件桥。<see cref="Current"/> 为 AsyncLocal 环境态，模式对齐 <c>SubAgentContext.Current</c>：
/// QueryLoop 在回合开始时进入作用域并持续排空；子代理中间件在作用域内发射带身份的事件。
/// 无作用域时发射静默丢弃 — CLI 纯文本等无显示消费方场景零影响。
/// </summary>
public sealed class SubAgentEventChannel
{
    private static readonly AsyncLocal<SubAgentEventChannel?> CurrentAccessor = new();

    /// <summary>当前异步流绑定的通道（无作用域时为 null）</summary>
    public static SubAgentEventChannel? Current => CurrentAccessor.Value;

    private readonly AsyncLock _lock = new("SubAgentEventChannel");
    private readonly List<ChatStreamEvent> _buffer = [];
    private bool _completed;

    /// <summary>
    /// 进入通道作用域 — 作用域内的异步流（含工具执行、子代理管道）共享此通道；
    /// 嵌套子代理的 QueryLoop 会创建自己的内层作用域，事件不会跨层泄漏
    /// </summary>
    public IDisposable EnterScope()
    {
        var previous = CurrentAccessor.Value;
        CurrentAccessor.Value = this;
        return new ScopeRestore(previous);
    }

    /// <summary>
    /// 发射事件到本通道 — 完成后再发射静默丢弃（不抛异常，进度类事件允许有损）；
    /// 调用方通常经 <c>Current?.Emit(evt)</c> 无作用域时自然跳过
    /// </summary>
    public void Emit(ChatStreamEvent evt)
    {
        using (_lock.Lock())
        {
            if (_completed)
                return;
            _buffer.Add(evt);
        }
    }

    /// <summary>
    /// 尝试读取单条事件（FIFO）
    /// </summary>
    public bool TryRead(out ChatStreamEvent evt)
    {
        using (_lock.Lock())
        {
            if (_buffer.Count == 0)
            {
                evt = null!;
                return false;
            }
            evt = _buffer[0];
            _buffer.RemoveAt(0);
            return true;
        }
    }

    /// <summary>
    /// 排空全部缓冲事件（按发射顺序返回），随后缓冲清空
    /// </summary>
    public IReadOnlyList<ChatStreamEvent> TryDrain()
    {
        using (_lock.Lock())
        {
            if (_buffer.Count == 0)
                return [];
            var drained = _buffer.ToArray();
            _buffer.Clear();
            return drained;
        }
    }

    /// <summary>
    /// 标记完成 — 完成后的 Emit 静默丢弃，防止迟到的子代理事件泄漏到下一回合
    /// </summary>
    public void Complete()
    {
        using (_lock.Lock())
        {
            _completed = true;
            _buffer.Clear();
        }
    }

    private sealed class ScopeRestore(SubAgentEventChannel? previous) : IDisposable
    {
        public void Dispose() => CurrentAccessor.Value = previous;
    }
}
