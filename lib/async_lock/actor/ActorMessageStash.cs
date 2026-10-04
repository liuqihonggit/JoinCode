namespace Core.Utils;

/// <summary>
/// 消息暂存队列 — Stash/Unstash/UnstashAll 管理,Consumer 线程独占访问无需锁(Akka 对齐)。
/// <para>封装暂存逻辑,ActorBase 持有此实例,Stash/Unstash/UnstashAll 转发至此。</para>
/// <para>⚠️ 仅在 Handle/行为内调用(Consumer 线程),禁止跨线程调用。</para>
/// <para>⚠️ <b>无限循环陷阱</b>:UnstashAll 把暂存消息 Tell 回自己,若消息 Handle 逻辑又调 Stash() 会无限循环。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
internal sealed class ActorMessageStash<TCommand> {
    private readonly Queue<TCommand> _stash = new();
    private readonly Action<TCommand> _tell;
    private TCommand? _current;

    /// <summary>
    /// 构造暂存队列 — 传入 Tell 委托用于 Unstash 时回投消息给自己。
    /// </summary>
    /// <param name="tell">Tell 委托 — Unstash/UnstashAll 时调用,把消息回投给自己</param>
    public ActorMessageStash(Action<TCommand> tell) => _tell = tell;

    /// <summary>当前处理的消息 — Stash 时读此值,SetCurrent 在 ProcessSingleCommand 开头设置</summary>
    public TCommand? Current => _current;

    /// <summary>设置当前消息 — ProcessSingleCommand 开头调用,供 Stash() 读取</summary>
    public void SetCurrent(TCommand cmd) => _current = cmd;

    /// <summary>
    /// 暂存当前消息 — 存入内部队列,稍后 Unstash 取出处理(Akka 对齐)。
    /// <para>⚠️ 仅在 Handle/行为内调用(Consumer 线程),Stash 后通常 return 不处理当前命令。</para>
    /// </summary>
    public void Stash() => _stash.Enqueue(_current!);

    /// <summary>
    /// 取出一条暂存消息 — FIFO 顺序 Tell 回自己,进入 Channel 尾部(Akka 对齐)。
    /// <para>⚠️ 仅在 Handle/行为内调用(Consumer 线程)。</para>
    /// </summary>
    public void Unstash() {
        if (_stash.Count > 0) {
            _tell(_stash.Dequeue());
        }
    }

    /// <summary>
    /// 取出所有暂存消息 — FIFO 顺序 Tell 回自己(Akka 对齐)。
    /// <para>⚠️ 仅在 Handle/行为内调用(Consumer 线程)。</para>
    /// <para>⚠️ <b>无限循环陷阱</b>:若暂存消息的 Handle 逻辑又调 Stash() 会无限循环,调用方需确保不重复 Stash。</para>
    /// </summary>
    public void UnstashAll() {
        while (_stash.Count > 0) {
            _tell(_stash.Dequeue());
        }
    }
}
