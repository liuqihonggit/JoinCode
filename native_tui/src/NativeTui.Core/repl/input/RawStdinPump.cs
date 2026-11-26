namespace NativeTui.Core.Repl;

/// <summary>
/// stdin 原始流输入泵（DSG042 P2）— 完整 VT 链：后台读线程阻塞读
/// <see cref="Console.OpenStandardInput"/> 字节流 → UTF-8 增量解码（跨块不完整多字节序列保留）
/// → <see cref="KeyPressParser"/> 逐键解析（不完整前缀攒齐重试；bracketed paste 整块直出
/// — ReadKey 通道的重组状态机在本通道天然不需要）→ 有界通道缓冲。
/// 主循环帧拍 <see cref="Drain"/> 非阻塞排空。与旧 ReadKeyPump 通道互斥
///（ReadKey 独占控制台输入缓冲；该类已归档，见 TASK121）— 构造期选路由宿主注入。
/// 读线程 IsBackground：进程退出自动终止（stdin Read 阻塞不可中断 — 无需优雅停机）。
/// 线程模型：pending 缓冲与前缀状态由单写者解析 Actor 独占（读线程投递解码块，Drain/测试投递
/// 冲刷屏障）；键投递纯非阻塞（TryWrite 满即停，剩余留 pending 等下一条命令续投，不丢键不阻塞），
/// 命令邮箱有界 — 读线程满时阻塞等待 Actor 腾空（后台线程背压，不持有调用方线程）。
/// </summary>
public sealed class RawStdinPump : IInputPump, IDisposable
{
    /// <summary>键事件通道容量（键风暴缓冲上限 — FullMode=Wait 下 TryWrite 满返 false 走回退续投
    /// 路径，不丢键不阻塞；DropWrite 会静默吞键与本泵"满即停"语义相反，禁用）。</summary>
    private const int ChannelCapacity = 256;

    /// <summary>解析命令邮箱容量（读线程解码块 + 帧拍冲刷屏障；有界背压防无界增长）。</summary>
    private const int CommandCapacity = 64;

    /// <summary>字节读取块大小（单次 stdin Read 上限 — 粘贴大块跨多块到达）。</summary>
    private const int ReadBlockSize = 4096;

    private readonly Channel<KeyPress> _keys = Channel.CreateBounded<KeyPress>(
        new BoundedChannelOptions(ChannelCapacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly Channel<PumpCommand> _commands = Channel.CreateBounded<PumpCommand>(
        new BoundedChannelOptions(CommandCapacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _cts = new();
    private readonly Decoder _utf8Decoder = Encoding.UTF8.GetDecoder();

    /// <summary>解码目标容量 — 使用编码器最大字符数，覆盖跨块代理对产出多于当前块字节数的情况。</summary>
    private readonly char[] _charBuffer = new char[Encoding.UTF8.GetMaxCharCount(ReadBlockSize)];
    private char[] _pendingBuffer = new char[ReadBlockSize];
    private readonly Thread _reader;
    private readonly TerminalQuerier? _querier;
    private readonly Func<long> _clock;

    /// <summary>pending 数据起点（解析 Actor 独占 — 消费只推进起点，不反复前移整段正文）。</summary>
    private int _pendingStart;

    /// <summary>pending 残留字符量快照（解析 Actor 写，探针/测试读 — 仅近似观测面）。</summary>
    private int _pendingCount;

    /// <summary>不完整粘贴终止标记的增量扫描起点（解析 Actor 独占）。</summary>
    private int _pasteSearchOffset;

    /// <summary>不完整 ESC 前缀滞留起点（解析 Actor 独占：null = 无滞留；新数据到达重置 —
    /// 可能凑齐序列；超过 <see cref="EscapeFlushPolicy"/> 阈值时强制 flush 释放通道）。</summary>
    private long? _prefixStuckSince;

    /// <summary>创建并启动读线程（构造即开始消费 stdin — 生命周期由 Dispose/进程退出终结）。
    /// querier 可选注入 — 终端查询响应前置分流（探测回复路由给等待方，非键事件不入键流）。
    /// clock 可选注入 — 前缀滞留计时源（默认 Environment.TickCount64；测试注入手动时钟消除真实时间竞态）。</summary>
    public RawStdinPump(TerminalQuerier? querier = null, Func<long>? clock = null)
        : this(querier, clock, startReader: true)
    {
    }

    /// <summary>测试构造 — 可禁用 stdin 读线程，避免测试注入与宿主输入并发共用解码器。</summary>
    internal RawStdinPump(TerminalQuerier? querier, Func<long>? clock, bool startReader)
    {
        _querier = querier;
        _clock = clock ?? (static () => Environment.TickCount64);
        _reader = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "raw-stdin-pump",
        };
        if (startReader)
        {
            _reader.Start();
        }

        _ = Task.Run(ProcessCommandsAsync);
    }

    /// <summary>非阻塞排空已解析键（帧拍消费 — 通道无键且 pending 无残留立即返回 false）；
    /// 先经 Actor 冲刷屏障续投 pending 残留（通道腾出空间时），再收通道键（收到键且有残留
    /// 再续一轮 — 至多两轮防活锁）。</summary>
    public bool Drain(Action<KeyPress> dispatch)
    {
        var dispatched = false;
        for (var round = 0; round < 2; round++)
        {
            if (!TryFlushPending())
            {
                break;
            }

            var got = false;
            while (_keys.Reader.TryRead(out var key))
            {
                dispatch(key);
                dispatched = true;
                got = true;
            }

            // 本轮无新键或无残留 — 提前结束（无活锁：续投满停时 got=false 直接退出）
            if (!got || Volatile.Read(ref _pendingCount) == 0)
            {
                break;
            }
        }

        return dispatched;
    }

    /// <summary>停泵（取消读循环 + 通道完成 + 关闭命令邮箱；Actor 排空已排队命令后退出 — 阻塞中的 Read 由进程退出回收）。</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _keys.Writer.TryComplete();
        _commands.Writer.TryComplete();
    }

    /// <summary>读线程是否仍在运行（探针完成判定 — stdin EOF 后读循环退出线程终止；
    /// 等待完成 = 读线程不存活 + pending 无残留 + 通道空三条件齐）。</summary>
    public bool IsReaderAlive => _reader.IsAlive;

    /// <summary>pending 残留字符量（测试/探针断言面 — 键风暴续投与收敛判定）。</summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <summary>命令邮箱中尚未被 Actor 消费的命令数（Dispose 竞态测试的同步观测面）。</summary>
    internal int PendingCommandCount => _commands.Reader.Count;

    /// <summary>解码一个 stdin 字节块并投递解析 Actor；读取与字节回归测试共用同一容量边界。</summary>
    private void DecodeBytes(ReadOnlySpan<byte> bytes, TaskCompletionSource? completion)
    {
        var charCount = _utf8Decoder.GetChars(bytes, _charBuffer, flush: false);
        if (charCount == 0)
        {
            completion?.TrySetResult();
            return;
        }

        Post(new PumpCommand(new string(_charBuffer, 0, charCount), Pump: true, Completion: completion));
    }

    /// <summary>
    /// 读循环（后台线程）— stdin 块读 → UTF-8 解码 → 投递解析 Actor（满时阻塞背压）。
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

                DecodeBytes(bytes.AsSpan(0, read), completion: null);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or ChannelClosedException)
        {
            // 流关闭/Dispose 竞态 — 静默退出（通道保持开放 — EOF ≠ Dispose）
        }
    }

    /// <summary>解析 Actor — 唯一修改 pending 与前缀状态的线程；每条命令追加文本并续投可解析键。</summary>
    private async Task ProcessCommandsAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                try
                {
                    if (command.Text is { Length: > 0 } text)
                    {
                        AppendPending(text);
                        _prefixStuckSince = null;
                    }

                    if (command.Pump)
                    {
                        PumpPendingToChannel();
                        Volatile.Write(ref _pendingCount, _pendingCount);
                    }

                    command.Completion?.TrySetResult();
                }
                catch (Exception)
                {
                    command.Completion?.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose 取消 — 静默退出
        }
    }

    /// <summary>投递命令 — 邮箱满时有界等待（读线程背压；Actor 不等待调用方，无自死锁）。</summary>
    private void Post(PumpCommand command)
    {
        if (_commands.Writer.TryWrite(command))
        {
            return;
        }

        try
        {
            _commands.Writer.WriteAsync(command, _cts.Token).AsTask().GetAwaiter().GetResult();  // style-allow: sync-block — stdin 泵同步契约: 原始输入线程同步写命令并等待完成
        }
        catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException)
        {
            command.Completion?.TrySetResult();
        }
    }

    /// <summary>同步冲刷屏障 — 等 Actor 完成本轮 pending 续投；邮箱关闭/取消时返回 false。</summary>
    private bool TryFlushPending()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(new PumpCommand(Text: null, Pump: true, Completion: completion));
        completion.Task.GetAwaiter().GetResult();  // style-allow: sync-block — stdin 泵同步契约: 原始输入线程同步写命令并等待完成
        return !_keys.Reader.Completion.IsCompleted || _keys.Reader.Count > 0;
    }

    /// <summary>当前未消费输入的零拷贝视图（解析 Actor 独占）。</summary>
    private ReadOnlySpan<char> PendingSpan => _pendingBuffer.AsSpan(_pendingStart, _pendingCount);

    /// <summary>追加解码块；容量不足时先利用头部空间或指数扩容，避免粘贴持续增长时反复拷贝。</summary>
    private void AppendPending(string text)
    {
        EnsurePendingCapacity(text.Length);
        text.CopyTo(0, _pendingBuffer, _pendingStart + _pendingCount, text.Length);
        _pendingCount += text.Length;
    }

    /// <summary>确保尾部可容纳新增字符；优先压缩头部空洞，否则扩容后压缩一次。</summary>
    private void EnsurePendingCapacity(int additional)
    {
        var required = _pendingCount + additional;
        if (required <= _pendingBuffer.Length - _pendingStart)
        {
            return;
        }

        if (required > _pendingBuffer.Length)
        {
            Array.Resize(ref _pendingBuffer, Math.Max(required, _pendingBuffer.Length * 2));
        }

        CompactPending();
    }

    /// <summary>把残留字符压回缓冲起点，仅在头部空间不足或扩容后调用。</summary>
    private void CompactPending()
    {
        if (_pendingStart == 0)
        {
            return;
        }

        Array.Copy(_pendingBuffer, _pendingStart, _pendingBuffer, 0, _pendingCount);
        _pendingStart = 0;
    }

    /// <summary>推进消费起点；完全消费时复位，残留数据延迟到下次容量不足再压缩。</summary>
    private void ConsumePending(int consumed)
    {
        _pendingStart += consumed;
        _pendingCount -= consumed;
        if (_pendingCount == 0)
        {
            _pendingStart = 0;
            _pasteSearchOffset = 0;
        }
    }

    /// <summary>
    /// 攒齐前缀逐键解析非阻塞投递通道 — <see cref="KeyPressParser.TryParseNext"/> 契约：
    /// 不完整前缀（包括孤立 ESC）不消耗留在 pending 等下块；孤立 ESC 超过 50ms 后
    /// 由 force 路径产出 Escape。通道满即停
    ///（剩余留 pending 等下一命令触发 — 零等待根除自死锁）；
    /// 通道已关闭（Dispose 后）静默返回。仅解析 Actor 调用。
    /// </summary>
    private void PumpPendingToChannel()
    {
        if (_keys.Reader.Completion.IsCompleted || _pendingCount == 0)
        {
            return;
        }

        var span = PendingSpan;
        var rest = span;
        while (true)
        {
            // 终端查询响应前置分流 — 探测回复（DECRPM/DA1/DA2/kitty flags/DSR/OSC）路由给
            // 等待方 promise，非键事件不入键流（对齐基准 parse-keypress.ts:484-487）；
            // querier 未注入时保持键解析路径（纯泵场景回复序列走 Unknown 吞噬防泄漏）
            if (_querier is not null)
            {
                var response = TerminalResponseParser.TryParse(rest, out var consumedByResponse);
                if (response is not null)
                {
                    _querier.Route(response);
                    rest = rest[consumedByResponse..];
                    continue;
                }
            }

            var before = rest;
            if (!KeyPressParser.TryParseNext(ref rest, ref _pasteSearchOffset, out var key))
            {
                // 超时强制 flush（基准 App.tsx:309-333 — 不完整前缀滞留超阈值时
                // 强制产出释放通道：ESC 前缀 50ms / 粘贴 500ms）
                if (_prefixStuckSince is { } since
                    && EscapeFlushPolicy.ShouldForceFlush(rest, _clock() - since)
                    && KeyPressParser.TryParseNext(ref rest, ref _pasteSearchOffset, out key, force: true))
                {
                    if (_keys.Writer.TryWrite(key))
                    {
                        continue;
                    }

                    rest = before;
                }

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
            ConsumePending(consumed);
        }

        // 滞留计时维护：仍有残留且 ESC 开头 = 不完整前缀滞留（首次打点；新数据到达由
        // Feed/ReadLoop append 前置重置）；无残留或已消耗 = 解析推进，滞留清零
        _prefixStuckSince = rest.Length > 0 && rest[0] == '\u001b' ? _prefixStuckSince ?? _clock() : null;
    }

    /// <summary>测试注入面 — 绕过 stdin Read，经生产 UTF-8 解码路径喂原始字节并同步冲刷。</summary>
    internal void FeedBytesForTest(byte[] bytes)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DecodeBytes(bytes, completion);
        completion.Task.GetAwaiter().GetResult();  // style-allow: sync-block — stdin 泵同步契约: 原始输入线程同步写命令并等待完成
    }

    /// <summary>
    /// 测试注入面 — 绕过 stdin Read 直接喂解码后字符流（UTF-8 解码/解析/通道/Drain 全链验证；
    /// 投递解析 Actor 并同步冲刷 — 同线程 Feed 后 Drain 安全无自死锁）。真实 stdin 阻塞行为由宿主
    /// exe 手动验收覆盖（ADR 0080）。
    /// </summary>
    internal void FeedForTest(string text)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(new PumpCommand(text, Pump: true, Completion: completion));
        completion.Task.GetAwaiter().GetResult();  // style-allow: sync-block — stdin 泵同步契约: 原始输入线程同步写命令并等待完成
    }

    /// <summary>解析命令 — Text=追加字符，Pump=续投 pending，Completion=两条命令的同步屏障。</summary>
    private sealed record PumpCommand(string? Text, bool Pump, TaskCompletionSource? Completion);
}
