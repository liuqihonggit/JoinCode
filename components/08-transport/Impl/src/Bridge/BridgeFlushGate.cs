namespace JoinCode.Transport.Bridge;

/// <summary>
/// 桥刷新门控 — 对齐 TS 端 flushGate.ts
/// 在初始历史消息刷新期间，排队新的写入消息，防止与历史消息交错
///
/// 生命周期:
///   Start() → enqueue 返回 true，消息排队
///   End()   → 返回排队的消息用于排空，enqueue 返回 false
///   Drop()  → 丢弃排队消息（永久传输关闭）
///   Deactivate() → 清除活跃标志但不丢弃消息（传输替换）
/// </summary>
public sealed class BridgeFlushGate<T>
{
    private bool _active;
    private readonly List<T> _pending = [];

    /// <summary>是否处于活跃状态 — 对齐 TS 端 active</summary>
    public bool Active => _active;

    /// <summary>排队消息数量 — 对齐 TS 端 pendingCount</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// 标记刷新进行中 — 对齐 TS 端 start()
    /// 调用后 enqueue() 将开始排队消息
    /// </summary>
    public void Start()
    {
        _active = true;
    }

    /// <summary>
    /// 结束刷新并返回排队的消息 — 对齐 TS 端 end()
    /// 调用方负责发送返回的消息
    /// </summary>
    public T[] End()
    {
        _active = false;
        var items = _pending.ToArray();
        _pending.Clear();
        return items;
    }

    /// <summary>
    /// 如果刷新活跃，排队消息并返回 true — 对齐 TS 端 enqueue()
    /// 如果刷新不活跃，返回 false（调用方应直接发送）
    /// </summary>
    public bool Enqueue(params ReadOnlySpan<T> items)
    {
        if (!_active) return false;
        foreach (var item in items)
        {
            _pending.Add(item);
        }
        return true;
    }

    /// <summary>
    /// 丢弃所有排队消息 — 对齐 TS 端 drop()
    /// 用于永久传输关闭场景
    /// </summary>
    /// <returns>丢弃的消息数量</returns>
    public int Drop()
    {
        _active = false;
        var count = _pending.Count;
        _pending.Clear();
        return count;
    }

    /// <summary>
    /// 清除活跃标志但不丢弃排队消息 — 对齐 TS 端 deactivate()
    /// 用于传输替换场景（新传输的 flush 将排空待处理消息）
    /// </summary>
    public void Deactivate()
    {
        _active = false;
    }
}
