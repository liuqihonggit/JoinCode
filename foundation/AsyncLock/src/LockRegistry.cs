namespace Core.Utils;

/// <summary>
/// 全局锁注册表 — 记录所有 <see cref="AsyncLock"/> 实例的实时持有/等待状态。
/// 卡死时调用 <see cref="DumpAll"/> 精确定位"哪个锁被哪个线程持有、等了多久、获取调用栈"。
/// 后台扫描线程定时检测持有/等待超时并告警。
/// </summary>
public static class LockRegistry
{
    private static readonly ConcurrentDictionary<int, LockInfo> _locks = new();
    private static int _nextId;
    private static int _nextFlowId;
    private static readonly AsyncLocal<int?> _currentFlowId = new();

    /// <summary>
    /// 当前异步流的唯一身份。跨 await 保持（AsyncLocal 随 ExecutionContext 传播），
    /// 不同 Task.Run 各自独立（父流未设置时子流懒生成新 ID）。
    /// 用于 async 上下文中可靠地识别"同一个异步流持有锁A并等待锁B"。
    /// </summary>
    internal static int CurrentFlowId
    {
        get
        {
            if (_currentFlowId.Value is not int flowId)
            {
                flowId = Interlocked.Increment(ref _nextFlowId);
                _currentFlowId.Value = flowId;
            }
            return flowId;
        }
    }

    private static TimeSpan _waitTimeoutThreshold = TimeSpan.FromSeconds(30);
    private static TimeSpan _holdTooLongThreshold = TimeSpan.FromSeconds(5);
    private static Action<string>? _diagnosticSink = static msg => Console.Error.WriteLine(msg);
    private static Timer? _scanTimer;
    private static TimeSpan _scanInterval = TimeSpan.FromSeconds(5);
    private static int _scanStarted;
    private static int _diagnosticsEnabled = 1;

    /// <summary>
    /// 诊断总开关（默认开启）。设为 0 关闭所有诊断记录与后台扫描，退化为零开销。
    /// </summary>
    public static bool DiagnosticsEnabled
    {
        get => Interlocked.CompareExchange(ref _diagnosticsEnabled, 0, 0) != 0;
        set
        {
            Interlocked.Exchange(ref _diagnosticsEnabled, value ? 1 : 0);
            if (!value) StopBackgroundScan();
        }
    }

    /// <summary>
    /// 等待锁超过此阈值时由后台扫描输出告警（默认 30s）。
    /// </summary>
    public static TimeSpan WaitTimeoutThreshold
    {
        get => _waitTimeoutThreshold;
        set => _waitTimeoutThreshold = value;
    }

    /// <summary>
    /// 持有锁超过此阈值时输出告警（默认 5s）。
    /// </summary>
    public static TimeSpan HoldTooLongThreshold
    {
        get => _holdTooLongThreshold;
        set => _holdTooLongThreshold = value;
    }

    /// <summary>
    /// 诊断信息输出委托（默认 <c>Console.Error</c>）。替换为日志框架时设置此属性。
    /// </summary>
    public static Action<string>? DiagnosticSink
    {
        get => _diagnosticSink;
        set => _diagnosticSink = value;
    }

    /// <summary>
    /// 注册新锁实例，返回唯一 ID。
    /// </summary>
    internal static int Register(string name)
    {
        var id = Interlocked.Increment(ref _nextId);
        _locks[id] = new LockInfo { Id = id, Name = name };
        EnsureScanStarted();
        return id;
    }

    /// <summary>
    /// 注销锁实例（Dispose 时调用）。
    /// </summary>
    internal static void Unregister(int id)
    {
        if (_locks.TryRemove(id, out var info) && info.HoldingThread is not null)
        {
            var heldFor = info.AcquiredAt.HasValue
                ? DateTimeOffset.UtcNow - info.AcquiredAt.Value
                : TimeSpan.Zero;
            if (heldFor > _holdTooLongThreshold)
            {
                Emit(
                    $"[LOCK-HOLD-TOO-LONG] 锁 '{info.Name}' (#{id}) 释放时已持有 " +
                    $"{heldFor.TotalSeconds:F1}s 超过阈值 {_holdTooLongThreshold.TotalSeconds:F1}s。" +
                    $"持有线程: {info.HoldingThread.ManagedThreadId}");
            }
        }
    }

    /// <summary>
    /// 记录线程开始等待锁。
    /// </summary>
    internal static void OnWaitStart(int id, string name)
    {
        if (!IsEnabled) return;
        if (_locks.TryGetValue(id, out var info))
        {
            info.WaitingThread = Thread.CurrentThread;
            info.WaitingFlowId = CurrentFlowId;
            info.WaitStartedAt = DateTimeOffset.UtcNow;
            info.WaitStack = CaptureStackTrace(skipFrames: 3);
        }
        DetectDeadlock();
    }

    /// <summary>
    /// 重入检测：同一线程已持有此锁时抛 <see cref="LockReentrancyException"/>。
    /// 仅在会阻塞的 Lock/LockAsync 方法中调用 — TryLock/TryLockAsync 非阻塞，重入返回 null 即可。
    /// 将"持锁后再次获取同一把锁"的死锁转为立即失败，而非卡在 SemaphoreSlim.Wait()。
    /// 用线程 ID 而非 AsyncLocal FlowId：SemaphoreSlim 是线程相关的，同线程重入必然死锁；
    /// AsyncLocal.Value 在 async 方法内部设置不传播回调用方，无法可靠检测 async 重入。
    /// </summary>
    internal static void CheckReentrancy(int id, string name)
    {
        if (!IsEnabled) return;
        if (_locks.TryGetValue(id, out var info)
            && info.HoldingThread == Thread.CurrentThread)
        {
            var msg = $"[LOCK-REENTRANCY] 锁 '{name}' (#{id}) 被同一线程 {Thread.CurrentThread.ManagedThreadId} 重入。" +
                      $"AsyncLock 不支持重入，持锁时再次获取会永久阻塞。\n  原获取调用栈:\n{info.AcquireStack}";
            Emit(msg);
            throw new LockReentrancyException(name, id, Thread.CurrentThread.ManagedThreadId, msg);
        }
    }

    /// <summary>
    /// 清除等待标记（未获取到锁时调用，如超时/取消）。不记录持有。
    /// </summary>
    internal static void OnWaitEnd(int id, string name)
    {
        if (_locks.TryGetValue(id, out var info))
        {
            var waited = info.WaitStartedAt.HasValue
                ? DateTimeOffset.UtcNow - info.WaitStartedAt.Value
                : TimeSpan.Zero;
            if (waited > _waitTimeoutThreshold && IsEnabled)
            {
                Emit(
                    $"[LOCK-WAIT-ABORT] 锁 '{name}' (#{id}) 等待 {waited.TotalSeconds:F1}s 后未获取(超时/取消)。" +
                    $"线程: {Thread.CurrentThread.ManagedThreadId}");
            }
            info.WaitingThread = null;
            info.WaitingFlowId = 0;
            info.WaitStartedAt = null;
            info.WaitStack = null;
        }
    }

    /// <summary>
    /// 记录锁等待超时 — <see cref="AsyncLock.TryLock"/> 超时返回 null 时调用,*。
    /// 通过 <see cref="DiagnosticSink"/> 输出诊断,便于定位"哪个锁等太久"。
    /// </summary>
    internal static void OnLockTimeout(string name, TimeSpan timeout)
    {
        if (!IsEnabled) return;
        Emit(
            $"[LOCK-TIMEOUT] 锁 '{name}' 等待 {timeout.TotalSeconds:F1}s 超时,返回 null。" +
            $"线程: {Thread.CurrentThread.ManagedThreadId}");
    }

    /// <summary>
    /// 记录锁获取成功（清除等待标记 + 记录持有信息）。
    /// </summary>
    internal static void OnAcquired(int id, string name)
    {
        if (!IsEnabled) return;
        if (_locks.TryGetValue(id, out var info))
        {
            var waited = info.WaitStartedAt.HasValue
                ? DateTimeOffset.UtcNow - info.WaitStartedAt.Value
                : TimeSpan.Zero;
            if (waited > _waitTimeoutThreshold)
            {
                Emit(
                    $"[LOCK-WAIT-SLOW] 锁 '{name}' (#{id}) 等待 {waited.TotalSeconds:F1}s " +
                    $"才获取成功(超过阈值 {_waitTimeoutThreshold.TotalSeconds:F1}s)。" +
                    $"获取线程: {Thread.CurrentThread.ManagedThreadId}");
            }
            info.HoldingThread = Thread.CurrentThread;
            info.HoldingFlowId = CurrentFlowId;
            info.AcquiredAt = DateTimeOffset.UtcNow;
            info.AcquireStack = CaptureStackTrace(skipFrames: 3);
            info.WaitingThread = null;
            info.WaitingFlowId = 0;
            info.WaitStartedAt = null;
            info.WaitStack = null;
        }
    }

    /// <summary>
    /// 记录锁释放。
    /// </summary>
    internal static void OnReleased(int id, string name)
    {
        if (_locks.TryGetValue(id, out var info))
        {
            var heldFor = info.AcquiredAt.HasValue
                ? DateTimeOffset.UtcNow - info.AcquiredAt.Value
                : TimeSpan.Zero;
            if (heldFor > _holdTooLongThreshold && IsEnabled)
            {
                Emit(
                    $"[LOCK-HOLD-TOO-LONG] 锁 '{name}' (#{id}) 持有 {heldFor.TotalSeconds:F1}s " +
                    $"超过阈值 {_holdTooLongThreshold.TotalSeconds:F1}s。" +
                    $"持有线程: {info.HoldingThread?.ManagedThreadId}");
            }
            info.HoldingThread = null;
            info.HoldingFlowId = 0;
            info.AcquiredAt = null;
            info.AcquireStack = null;
        }
    }

    private static bool IsEnabled => Interlocked.CompareExchange(ref _diagnosticsEnabled, 0, 0) != 0;

    private static Exception? _lastSinkError;

    private static void Emit(string msg)
    {
        var sink = _diagnosticSink;
        if (sink is null) return;
        try { sink(msg); }
        catch (Exception ex) { Volatile.Write(ref _lastSinkError, ex); }
    }

    /// <summary>
    /// 捕获当前调用栈。用 <see cref="Environment.StackTrace"/>（AOT 兼容，返回方法名）。
    /// 跳过前 <paramref name="skipFrames"/> 帧内部诊断代码，最多保留 25 帧。
    /// </summary>
    private static string CaptureStackTrace(int skipFrames)
    {
        try
        {
            var stack = Environment.StackTrace;
            var sb = new StringBuilder(256);
            var count = 0;
            foreach (var rawLine in stack.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0) continue;
                if (skipFrames > 0) { skipFrames--; continue; }
                if (count >= 25) break;
                sb.Append("    ").Append(line).Append('\n');
                count++;
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastSinkError, ex);
            return "<stack unavailable>";
        }
    }

    /// <summary>
    /// 输出所有锁的实时状态，卡死时调用以定位死锁。
    /// </summary>
    public static string DumpAll()
    {
        var sb = new StringBuilder(512);
        var now = DateTimeOffset.UtcNow;
        sb.Append($"[LOCK-DUMP] 共 {_locks.Count} 把锁，时间 {now:HH:mm:ss.fff}\n");
        foreach (var info in _locks.Values.OrderBy(x => x.Id))
        {
            string status;
            if (info.HoldingThread is not null)
            {
                var held = info.AcquiredAt.HasValue ? now - info.AcquiredAt.Value : TimeSpan.Zero;
                status = $"持有中(流#{info.HoldingFlowId}, 线程 {info.HoldingThread.ManagedThreadId}, 已持有 {held.TotalSeconds:F1}s)";
            }
            else if (info.WaitingThread is not null)
            {
                var waited = info.WaitStartedAt.HasValue ? now - info.WaitStartedAt.Value : TimeSpan.Zero;
                status = $"等待中(流#{info.WaitingFlowId}, 线程 {info.WaitingThread.ManagedThreadId}, 已等 {waited.TotalSeconds:F1}s)";
            }
            else
            {
                status = "空闲";
            }
            sb.Append($"  #{info.Id} '{info.Name}' — {status}\n");
            if (info.WaitingThread is not null)
                sb.Append($"    (等待流#{info.WaitingFlowId})\n");
            if (info.HoldingThread is not null && info.AcquireStack is { Length: > 0 })
                sb.Append("    获取调用栈:\n").Append(info.AcquireStack);
            if (info.WaitingThread is not null && info.WaitStack is { Length: > 0 })
                sb.Append("    等待调用栈:\n").Append(info.WaitStack);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 启动后台扫描线程，定时检测持有/等待时间过长的锁并告警。
    /// 幂等：重复调用仅更新扫描间隔。
    /// </summary>
    public static void StartBackgroundScan(TimeSpan? interval = null)
    {
        if (interval.HasValue) _scanInterval = interval.Value;
        _scanTimer?.Dispose();
        _scanTimer = new Timer(static _ => ScanHolds(), null, _scanInterval, _scanInterval);
        Interlocked.Exchange(ref _scanStarted, 1);
    }

    /// <summary>
    /// 停止后台扫描。
    /// </summary>
    public static void StopBackgroundScan()
    {
        _scanTimer?.Dispose();
        _scanTimer = null;
        Interlocked.Exchange(ref _scanStarted, 0);
    }

    private static void EnsureScanStarted()
    {
        if (Interlocked.CompareExchange(ref _scanStarted, 0, 0) == 0
            && Interlocked.CompareExchange(ref _scanStarted, 1, 0) == 0)
        {
            StartBackgroundScan();
        }
    }

    private static void ScanHolds()
    {
        if (!IsEnabled) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var info in _locks.Values)
        {
            if (info.HoldingThread is not null && info.AcquiredAt.HasValue)
            {
                var held = now - info.AcquiredAt.Value;
                if (held > _holdTooLongThreshold)
                {
                    Emit(
                        $"[LOCK-SCAN-HOLD] 锁 '{info.Name}' (#{info.Id}) 持有 {held.TotalSeconds:F1}s " +
                        $"超过阈值(线程 {info.HoldingThread.ManagedThreadId})。\n{info.AcquireStack}");
                }
            }
            if (info.WaitingThread is not null && info.WaitStartedAt.HasValue)
            {
                var waited = now - info.WaitStartedAt.Value;
                if (waited > _waitTimeoutThreshold)
                {
                    Emit(
                        $"[LOCK-SCAN-WAIT] 锁 '{info.Name}' (#{info.Id}) 等待 {waited.TotalSeconds:F1}s " +
                        $"超过阈值(线程 {info.WaitingThread.ManagedThreadId})。\n{info.WaitStack}");
                }
            }
        }
        DetectDeadlock();
    }

    /// <summary>
    /// 检测死锁环（wait-for graph DFS）。每个线程最多等一把锁，出度≤1，沿等待边走回到起点即死锁。
    /// 检测到死锁时自动通过 DiagnosticSink 输出完整诊断（锁链+线程+调用栈），无需手动调用。
    /// </summary>
    internal static void DetectDeadlock()
    {
        if (!IsEnabled) return;
        var waitEdges = new Dictionary<int, (int holderThreadId, LockInfo lk)>();
        foreach (var info in _locks.Values)
        {
            if (info.WaitingThread is not null && info.HoldingThread is not null)
                waitEdges[info.WaitingThread.ManagedThreadId] = (info.HoldingThread.ManagedThreadId, info);
        }
        if (waitEdges.Count == 0) return;
        foreach (var startId in waitEdges.Keys)
        {
            var chain = new List<(int threadId, LockInfo lk)>();
            var current = startId;
            for (int step = 0; step <= waitEdges.Count; step++)
            {
                if (!waitEdges.TryGetValue(current, out var edge))
                    break;
                chain.Add((current, edge.lk));
                current = edge.holderThreadId;
                if (current == startId)
                {
                    EmitDeadlockReport(chain);
                    return;
                }
            }
        }
    }

    private static void EmitDeadlockReport(List<(int threadId, LockInfo lk)> chain)
    {
        var sb = new StringBuilder(512);
        sb.Append($"[DEADLOCK-DETECTED] 检测到死锁环（{chain.Count} 把锁），时间 {DateTimeOffset.UtcNow:HH:mm:ss.fff}\n");
        for (var i = 0; i < chain.Count; i++)
        {
            var (threadId, lk) = chain[i];
            var next = chain[(i + 1) % chain.Count];
            sb.Append($"  线程{threadId} 持有锁 '{lk.Name}' (#{lk.Id}, 流#{lk.HoldingFlowId})，等待锁 '{next.lk.Name}' (#{next.lk.Id}, 流#{next.lk.WaitingFlowId})\n");
            if (lk.AcquireStack is { Length: > 0 })
                sb.Append($"    获取调用栈:\n{lk.AcquireStack}");
            if (next.lk.WaitStack is { Length: > 0 })
                sb.Append($"    等待调用栈:\n{next.lk.WaitStack}");
        }
        var msg = sb.ToString();
        Volatile.Write(ref _lastDeadlockReport, msg);
        Interlocked.Exchange(ref _deadlockDetected, 1);
        Emit(msg);
    }

    /// <summary>
    /// 清空注册表并重置 ID（仅测试用）。
    /// </summary>
    internal static void ClearForTesting()
    {
        StopBackgroundScan();
        _locks.Clear();
        Interlocked.Exchange(ref _nextId, 0);
        Interlocked.Exchange(ref _nextFlowId, 0);
        Interlocked.Exchange(ref _deadlockDetected, 0);
        Volatile.Write(ref _lastDeadlockReport, null);
    }

    /// <summary>
    /// 获取当前注册的锁数量（诊断/测试用）。
    /// </summary>
    public static int Count => _locks.Count;

    private static string? _lastDeadlockReport;
    private static int _deadlockDetected;

    /// <summary>
    /// 最近一次自动检测到的死锁报告（null 表示未检测到死锁）。
    /// 死锁检测在 OnWaitStart（线程开始等待时）和后台扫描时自动触发，无需手动调用。
    /// </summary>
    public static string? LastDeadlockReport => Volatile.Read(ref _lastDeadlockReport);

    /// <summary>
    /// 是否曾检测到死锁环（诊断/测试用）。
    /// </summary>
    public static bool DeadlockDetected => Interlocked.CompareExchange(ref _deadlockDetected, 0, 0) != 0;
}

internal sealed class LockInfo
{
    public int Id;
    public string Name = "";
    public Thread? HoldingThread;
    public int HoldingFlowId;
    public DateTimeOffset? AcquiredAt;
    public string? AcquireStack;
    public Thread? WaitingThread;
    public int WaitingFlowId;
    public DateTimeOffset? WaitStartedAt;
    public string? WaitStack;
}
