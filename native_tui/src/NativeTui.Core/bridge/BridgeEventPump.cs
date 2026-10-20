namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 事件泵 — 后台 reader 线程把引擎 stdout（行分格 JSON）转入并发队列
/// （Actor 邮箱模型：读线程生产、主循环消费，无共享锁，ADR 0141）。
/// 事件队列供 DrainEvents 排空；未知 type / 坏 JSON 行进诊断队列（不中断事件流）；
/// 流终止（Eof）后 IsCompleted 置位；IOException（引擎 stdout 崩溃）记录诊断后完成。
/// </summary>
public sealed class BridgeEventPump : IDisposable
{
    private readonly TextReader _reader;
    private readonly Thread _thread;
    private readonly ConcurrentQueue<BridgeEvent> _events = new();
    private readonly ConcurrentQueue<string> _diagnostics = new();
    private readonly ManualResetEventSlim _completed = new();

    /// <summary>创建事件泵（持有 reader；Start 前不启动线程）。</summary>
    public BridgeEventPump(TextReader reader)
    {
        _reader = reader;
        _thread = new Thread(PumpLoop) { IsBackground = true, Name = "BridgeEventPump" };
    }

    /// <summary>泵已完成（Eof 或流异常）。</summary>
    public bool IsCompleted => _completed.IsSet;

    /// <summary>启动后台读线程。</summary>
    public void Start() => _thread.Start();

    /// <summary>阻塞等待泵完成（无超时 — 测试与 Dispose 路径使用；引擎退出 → stdout Eof 后返回）。</summary>
    public void Wait() => _completed.Wait();

    /// <summary>排空事件队列（返回本次取出的全部事件，再调用返回空）。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() => Drain(_events);

    /// <summary>排空诊断队列（未知 type / 坏 JSON / 流异常，调用方记 stderr）。</summary>
    public IReadOnlyList<string> DrainDiagnostics() => Drain(_diagnostics);

    /// <summary>等待完成并释放等待句柄（未 Start 的泵直接释放；无超时 — 释放函数禁止超时）。
    /// ThreadState 是 [Flags]：IsBackground 线程未启动时为 Background|Unstarted，必须位测试而非相等比较。</summary>
    public void Dispose()
    {
        if ((_thread.ThreadState & System.Threading.ThreadState.Unstarted) == 0 && !_completed.IsSet)
        {
            _completed.Wait();
        }

        _completed.Dispose();
    }

    /// <summary>泵循环：逐行读事件入队，Eof 退出，异常记录诊断，最终置完成。</summary>
    private void PumpLoop()
    {
        try
        {
            PumpLines();
        }
        catch (IOException ex)
        {
            _diagnostics.Enqueue($"bridge 事件流中断（引擎 stdout 关闭）：{ex.Message}");
        }
        finally
        {
            _completed.Set();
        }
    }

    /// <summary>逐行泵送：Event 入事件队列；Skipped 入诊断队列；Eof 返回。</summary>
    private void PumpLines()
    {
        while (true)
        {
            var result = BridgeProtocol.TryReadEvent(_reader, out var bridgeEvent, out var diagnostic);
            if (result == BridgeReadResult.Eof)
            {
                return;
            }

            if (result == BridgeReadResult.Skipped)
            {
                _diagnostics.Enqueue(diagnostic!);
            }
            else
            {
                _events.Enqueue(bridgeEvent!);
            }
        }
    }

    /// <summary>排空并发队列到快照列表。</summary>
    private static IReadOnlyList<T> Drain<T>(ConcurrentQueue<T> queue)
    {
        var list = new List<T>();
        while (queue.TryDequeue(out var item))
        {
            list.Add(item);
        }

        return list;
    }
}
