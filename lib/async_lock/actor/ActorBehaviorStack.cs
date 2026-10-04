namespace Core.Utils;

/// <summary>
/// 行为委托 — 处理命令,签名与 Handle 一致。
/// <para>Become/BecomeStacked 切换此委托,实现状态机行为切换(Akka 对齐)。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
/// <param name="command">待处理命令</param>
/// <param name="ct">取消令牌(Actor 释放时触发取消)</param>
public delegate void ActorReceive<TCommand>(TCommand command, CancellationToken ct);

/// <summary>
/// 行为栈 — Become/BecomeStacked/UnbecomeStacked 管理,Consumer 线程独占访问无需锁(Akka 对齐)。
/// <para>封装行为切换逻辑,ActorBase 持有此实例,Become/BecomeStacked/UnbecomeStacked 转发至此。</para>
/// <para>⚠️ 仅在 Handle/行为内调用(Consumer 线程),禁止跨线程调用。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
internal sealed class ActorBehaviorStack<TCommand> {
    private readonly Stack<ActorReceive<TCommand>?> _stack = new();
    private ActorReceive<TCommand>? _current;

    /// <summary>当前行为 — null 表示用 Handle 默认行为</summary>
    public ActorReceive<TCommand>? Current => _current;

    /// <summary>
    /// 替换当前行为(不入栈) — UnbecomeStacked 不会恢复到此行为之前的状态。
    /// <para>适合不可逆状态转换(如初始化→运行态)。</para>
    /// </summary>
    /// <param name="receive">新行为委托</param>
    public void Become(ActorReceive<TCommand> receive) => _current = receive;

    /// <summary>
    /// 压入新行为(入栈) — UnbecomeStacked 弹出恢复上一个行为。
    /// <para>可逆状态转换(如临时进入处理态后恢复)。</para>
    /// </summary>
    /// <param name="receive">新行为委托</param>
    public void BecomeStacked(ActorReceive<TCommand> receive) {
        _stack.Push(_current);
        _current = receive;
    }

    /// <summary>
    /// 弹出行为 — 恢复到上一个 BecomeStacked 之前的行为。
    /// <para>栈空时无操作(保持当前行为)。</para>
    /// </summary>
    public void UnbecomeStacked() {
        if (_stack.Count > 0) {
            _current = _stack.Pop();
        }
    }
}
