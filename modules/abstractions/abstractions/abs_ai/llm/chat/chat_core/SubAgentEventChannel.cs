// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 子代理事件通道 — 主对话管道（QueryLoop）与子代理执行中间件（AgentStreamExecution）之间的
/// 跨层实时事件桥。<see cref="Current"/> 为 AsyncLocal 环境态，模式对齐 <c>SubAgentContext.Current</c>：
/// QueryLoop 在回合开始时进入作用域并持续排空；子代理中间件在作用域内发射带身份的事件。
/// 无作用域时发射静默丢弃 — CLI 纯文本等无显示消费方场景零影响。
/// 无锁实现：单不可变 ChannelState + Interlocked.CompareExchange CAS 循环，_completed 与 _buffer 原子更新
/// </summary>
public sealed class SubAgentEventChannel {
    private static readonly AsyncLocal<SubAgentEventChannel?> CurrentAccessor = new();

    /// <summary>当前异步流绑定的通道（无作用域时为 null）</summary>
    public static SubAgentEventChannel? Current => CurrentAccessor.Value;

    private volatile ChannelState _state = new(false, ImmutableList<ChatStreamEvent>.Empty);

    private sealed record ChannelState(bool Completed, ImmutableList<ChatStreamEvent> Buffer);

    /// <summary>
    /// 进入通道作用域 — 作用域内的异步流（含工具执行、子代理管道）共享此通道；
    /// 嵌套子代理的 QueryLoop 会创建自己的内层作用域，事件不会跨层泄漏
    /// </summary>
    public IDisposable EnterScope() => AsyncLocalScope<SubAgentEventChannel?>.Enter(CurrentAccessor, this);

    /// <summary>
    /// 发射事件到本通道 — 完成后再发射静默丢弃（不抛异常，进度类事件允许有损）；
    /// 调用方通常经 <c>Current?.Emit(evt)</c> 无作用域时自然跳过
    /// </summary>
    public void Emit(ChatStreamEvent evt) {
        while (true) {
            var snapshot = _state;
            if (snapshot.Completed) return;
            var updated = snapshot with { Buffer = snapshot.Buffer.Add(evt) };
            if (Interlocked.CompareExchange(ref _state, updated, snapshot) == snapshot) return;
        }
    }

    /// <summary>
    /// 尝试读取单条事件（FIFO）
    /// </summary>
    public bool TryRead(out ChatStreamEvent? evt) {
        while (true) {
            var snapshot = _state;
            if (snapshot.Buffer.Count == 0) {
                evt = null;
                return false;
            }
            evt = snapshot.Buffer[0];
            var updated = snapshot with { Buffer = snapshot.Buffer.RemoveAt(0) };
            if (Interlocked.CompareExchange(ref _state, updated, snapshot) == snapshot) return true;
        }
    }

    /// <summary>
    /// 排空全部缓冲事件（按发射顺序返回），随后缓冲清空
    /// </summary>
    public IReadOnlyList<ChatStreamEvent> TryDrain() {
        while (true) {
            var snapshot = _state;
            if (snapshot.Buffer.Count == 0)
                return [];
            var drained = snapshot.Buffer.ToArray();
            var updated = snapshot with { Buffer = ImmutableList<ChatStreamEvent>.Empty };
            if (Interlocked.CompareExchange(ref _state, updated, snapshot) == snapshot) return drained;
        }
    }

    /// <summary>
    /// 标记完成 — 完成后的 Emit 静默丢弃，防止迟到的子代理事件泄漏到下一回合
    /// </summary>
    public void Complete() {
        while (true) {
            var snapshot = _state;
            if (snapshot.Completed) return;
            var updated = snapshot with { Completed = true, Buffer = ImmutableList<ChatStreamEvent>.Empty };
            if (Interlocked.CompareExchange(ref _state, updated, snapshot) == snapshot) return;
        }
    }
}