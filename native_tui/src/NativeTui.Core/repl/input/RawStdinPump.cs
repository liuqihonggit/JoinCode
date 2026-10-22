namespace NativeTui.Core.Repl;

/// <summary>
/// stdin 原始流输入泵（DSG042 P2）— 完整 VT 链：后台读线程阻塞读
/// <see cref="Console.OpenStandardInput"/> 字节流 → UTF-8 增量解码（跨块不完整多字节序列保留）
/// → <see cref="KeyPressParser"/> 逐键解析（不完整前缀攒齐重试；bracketed paste 整块直出
/// — ReadKey 通道的重组状态机在本通道天然不需要）→ 有界通道缓冲。
/// 主循环帧拍 <see cref="Drain"/> 非阻塞排空。与 <see cref="ReadKeyPump"/> 互斥
///（ReadKey 独占控制台输入缓冲）— 构造期二选一，通道切换由宿主开关注入。
/// 读线程 IsBackground：进程退出自动终止（stdin Read 阻塞不可中断 — 无需优雅停机）。
/// 线程模型：<see cref="_gate"/> 锁保护 pending 缓冲与通道投递（读线程解析 / Drain 续投
/// 双向触发）——投递纯非阻塞（TryWrite 满即停，剩余留 pending 下次触发续投，不丢键不阻塞），
/// 全链零同步等待 → 根除自死锁（同线程 Feed 后 Drain 的测试场景安全）。
/// </summary>
public sealed class RawStdinPump : IInputPump, IDisposable
{
    /// <summary>键事件通道容量（键风暴缓冲上限 — FullMode=Wait 下 TryWrite 满返 false 走回退续投
    /// 路径，不丢键不阻塞；DropWrite 会静默吞键与本泵"满即停"语义相反，禁用）。</summary>
    private const int ChannelCapacity = 256;

    /// <summary>字节读取块大小（单次 stdin Read 上限 — 粘贴大块跨多块到达）。</summary>
    private const int ReadBlockSize = 4096;

    private readonly Channel<KeyPress> _keys = Channel.CreateBounded<KeyPress>(
        new BoundedChannelOptions(ChannelCapacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _cts = new();
    private readonly Decoder _utf8Decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] _charBuffer = new char[2048];
    private readonly object _gate = new();
    private readonly StringBuilder _pending = new();
    private readonly Thread _reader;

    /// <summary>创建并启动读线程（构造即开始消费 stdin — 生命周期由 Dispose/进程退出终结）。</summary>
    public RawStdinPump()
    {
        _reader = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "raw-stdin-pump",
        };
        _reader.Start();
    }

    /// <summary>非阻塞排空已解析键（帧拍消费 — 通道无键且 pending 无残留立即返回 false）；
    /// 先续投 pending 残留（通道腾出空间时），再收通道键（收到键且有残留再续一轮 — 至多两轮防活锁）。</summary>
    public bool Drain(Action<KeyPress> dispatch)
    {
        var dispatched = false;
        for (var round = 0; round < 2; round++)
        {
            PumpPendingToChannel();
            var got = false;
            while (_keys.Reader.TryRead(out var key))
            {
                dispatch(key);
                dispatched = true;
                got = true;
            }

            // 本轮无新键或无残留 — 提前结束（无活锁：续投满停时 got=false 直接退出）
            if (!got || !HasPending)
            {
                break;
            }
        }

        return dispatched;
    }

    /// <summary>停泵（取消读循环 + 通道完成；阻塞中的 Read 由进程退出回收 — IsBackground 线程）。</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _keys.Writer.TryComplete();
    }

    /// <summary>读线程是否仍在运行（探针完成判定 — stdin EOF 后读循环退出线程终止；
    /// 等待完成 = 读线程不存活 + pending 无残留 + 通道空三条件齐）。</summary>
    public bool IsReaderAlive => _reader.IsAlive;

    /// <summary>pending 残留字符量（测试/探针断言面 — 键风暴续投与收敛判定）。</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Length;
            }
        }
    }

    /// <summary>
    /// 读循环（后台线程）— stdin 块读 → UTF-8 解码 → 前缀攒齐解析 → 通道投递（满即停）。
    /// 流 EOF（Read 返 0 — stdin 重定向/管道输入结束）或取消时静默退出：通道不关闭
    ///（EOF ≠ Dispose — 泵无产出但 Drain 语义不变；进程退出由 IsBackground 回收）；
    /// 解码/解析异常防御吞掉（读线程不可死 — 泵崩溃即无输入）。
    /// </summary>
    private void ReadLoop()
    {
        try
        {
            var stdin = Console.OpenStandardInput();
            var bytes = new byte[ReadBlockSize];
            while (!_cts.IsCancellationRequested)
            {
                var read = stdin.Read(bytes, 0, bytes.Length);
                if (read <= 0)
                {
                    break;
                }

                var charCount = _utf8Decoder.GetChars(bytes, 0, read, _charBuffer, 0, flush: false);
                lock (_gate)
                {
                    _pending.Append(_charBuffer, 0, charCount);
                }

                PumpPendingToChannel();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // 流关闭/Dispose 竞态 — 静默退出（通道保持开放 — EOF ≠ Dispose）
        }
    }

    /// <summary>
    /// 攒齐前缀逐键解析非阻塞投递通道 — <see cref="KeyPressParser.TryParseNext"/> 契约：
    /// 不完整前缀（如孤立 "\x1b["）不消耗留在 pending 等下块；孤立 ESC 按 chunk 边界
    /// 即时产出 Escape（ReadKey 通道的 KeyAvailable 语义近似）。通道满即停
    ///（剩余留 pending 由读线程下块 / Drain 续投触发 — 零等待根除自死锁）；
    /// 通道已关闭（Dispose 后）静默返回。
    /// </summary>
    private void PumpPendingToChannel()
    {
        lock (_gate)
        {
            if (_keys.Reader.Completion.IsCompleted || _pending.Length == 0)
            {
                return;
            }

            var span = _pending.ToString().AsSpan();
            var rest = span;
            while (true)
            {
                var before = rest;
                if (!KeyPressParser.TryParseNext(ref rest, out var key))
                {
                    break;
                }

                if (!_keys.Writer.TryWrite(key))
                {
                    // 通道满 — 回退本键起点（已解析未投递不消耗：解析器 ref 推进不可逆，
                    // span 局部重赋值回退）防丢键，剩余含本键留 pending 续投
                    rest = before;
                    break;
                }
            }

            var consumed = span.Length - rest.Length;
            if (consumed > 0)
            {
                _pending.Remove(0, consumed);
            }
        }
    }

    /// <summary>pending 残留判定（锁内 — Drain 轮次控制消费）。</summary>
    private bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Length > 0;
            }
        }
    }

    /// <summary>
    /// 测试注入面 — 绕过 stdin Read 直接喂解码后字符流（UTF-8 解码/解析/通道/Drain 全链验证；
    /// 纯非阻塞投递 — 同线程 Feed 后 Drain 安全无自死锁）。真实 stdin 阻塞行为由宿主
    /// exe 手动验收覆盖（ADR 0080）。
    /// </summary>
    internal void FeedForTest(string text)
    {
        lock (_gate)
        {
            _pending.Append(text);
        }

        PumpPendingToChannel();
    }
}
