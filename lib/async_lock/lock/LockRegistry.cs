namespace Core.Utils;

/// <summary>
/// 全局锁注册表 — 记录所有 <see cref="AsyncLock"/> 实例的实时持有/等待状态。
/// 卡死时调用 <see cref="DumpAll"/> 精确定位"哪个锁被哪个线程持有、等了多久、获取调用栈"。
/// 后台扫描线程定时检测持有/等待超时并告警。
/// </summary>
public static class LockRegistry {
    private static readonly ConcurrentDictionary<int, LockInfo> _locks = new();
    private static int _nextId;

    private static TimeSpan _waitTimeoutThreshold = TimeSpan.FromSeconds(30);
    private static TimeSpan _holdTooLongThreshold = TimeSpan.FromSeconds(5);
    private static Action<string>? _diagnosticSink = static msg => Console.Error.WriteLine(msg);
    private static Timer? _scanTimer;
    private static TimeSpan _scanInterval = TimeSpan.FromSeconds(5);
    private static int _scanStarted;
    private static int _diagnosticsEnabled = 0;
    private static int _nextFlowId;

    /// <summary>
    /// 当前 async 逻辑流的 FlowId — 在 async 流中自动流转,不随 await 线程切换变化。
    /// 用于死锁检测构建 wait-for graph,替代不可靠的 Thread.CurrentThread。
    /// </summary>
    public static int CurrentFlowId => AsyncFlowIdentity.CurrentFlowId;

    /// <summary>
    /// 注册新的逻辑流并分配唯一 FlowId — 在 async 流入口(如 Task.Run/Actor 启动)调用,
    /// 后续所有 await 后续自动继承此 FlowId,用于死锁检测正确构建 wait-for graph。
    /// </summary>
    public static int RegisterFlow() {
        var id = Interlocked.Increment(ref _nextFlowId);
        AsyncFlowIdentity.SetFlowId(id);
        return id;
    }

    /// <summary>
    /// 确保当前 async 逻辑流已注册 FlowId — 若未注册则分配新 ID。
    /// 返回当前 FlowId(保证非 0)。在 async 流入口(如 Task.Run/Actor 启动/PlanMode 创建)调用,
    /// 后续所有 await 后续自动继承此 FlowId,用于死锁检测正确构建 wait-for graph。
    /// </summary>
    public static int EnsureFlowRegistered() {
        var id = CurrentFlowId;
        if (id == 0)
            id = RegisterFlow();
        return id;
    }

    private static int ResolveFlowId() => AsyncFlowIdentity.CurrentFlowId;

    /// <summary>
    /// 诊断总开关（默认关闭，需 --debuglog 或 JCC_DEBUGLOG=1 开启）。设为 0 关闭所有诊断记录与后台扫描，退化为零开销。
    /// </summary>
    public static bool DiagnosticsEnabled {
        get => Interlocked.CompareExchange(ref _diagnosticsEnabled, 0, 0) != 0;
        set {
            Interlocked.Exchange(ref _diagnosticsEnabled, value ? 1 : 0);
            if (!value) StopBackgroundScan();
        }
    }

    /// <summary>
    /// 等待锁超过此阈值时由后台扫描输出告警（默认 30s）。
    /// </summary>
    public static TimeSpan WaitTimeoutThreshold {
        get => _waitTimeoutThreshold;
        set => _waitTimeoutThreshold = value;
    }

    /// <summary>
    /// 持有锁超过此阈值时输出告警（默认 5s）。
    /// </summary>
    public static TimeSpan HoldTooLongThreshold {
        get => _holdTooLongThreshold;
        set => _holdTooLongThreshold = value;
    }

    /// <summary>
    /// 诊断信息输出委托（默认 <c>Console.Error</c>）。替换为日志框架时设置此属性。
    /// </summary>
    public static Action<string>? DiagnosticSink {
        get => _diagnosticSink;
        set => _diagnosticSink = value;
    }

    /// <summary>
    /// 注册新锁实例，返回唯一 ID。
    /// </summary>
    internal static int Register(string name) {
        var id = Interlocked.Increment(ref _nextId);
        _locks[id] = new LockInfo { Id = id, Name = name };
        EnsureScanStarted();
        if (IsEnabled)
            Emit($"[LOCK-CREATED] 锁 '{name}' (#{id}) 创建。");
        return id;
    }

    /// <summary>
    /// 注销锁实例（Dispose 时调用）。
    /// </summary>
    internal static void Unregister(int id) {
        if (!_locks.TryRemove(id, out var info)) return;
        if (info.HoldingFlowId != 0) {
            var acquiredTicks = info.AcquiredTicks;
            var heldFor = acquiredTicks != 0
                ? TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - acquiredTicks)
                : TimeSpan.Zero;
            if (heldFor > _holdTooLongThreshold) {
                Emit(
                    $"[LOCK-HOLD-TOO-LONG] 锁 '{info.Name}' (#{id}) 释放时已持有 " +
                    $"{heldFor.TotalSeconds:F1}s 超过阈值 {_holdTooLongThreshold.TotalSeconds:F1}s。" +
                    $"持有流: {info.HoldingFlowId}");
            }
        }
        if (IsEnabled)
            Emit($"[LOCK-DISPOSED] 锁 '{info.Name}' (#{id}) 注销。");
    }

    /// <summary>
    /// 记录线程开始等待锁。
    /// </summary>
    internal static void OnWaitStart(int id, string name) {
        if (!IsEnabled) return;
        if (_locks.TryGetValue(id, out var info)) {
            info.WaitingFlowId = ResolveFlowId();
            info.WaitStartedTicks = DateTimeOffset.UtcNow.Ticks;
            info.WaitStack = CaptureStackTrace(skipFrames: 3);
            Emit($"[LOCK-WAIT-START] 锁 '{name}' (#{id}) 流 {ResolveFlowId()} 开始等待。");
            var currentFlowId = ResolveFlowId();
            foreach (var other in _locks.Values) {
                if (other.HoldingFlowId == currentFlowId && other.Id > id)
                    Emit(
                        $"[LOCK-ORDER-VIOLATION] 锁顺序违反: 流 {currentFlowId} " +
                        $"已持有锁 #{other.Id} '{other.Name}', 现在获取锁 #{id} '{name}' (ID 更小)。" +
                        $"按锁 ID 升序获取可避免死锁。");
            }
            // 即时死锁检测:当前流刚加入等待边,沿边走若回到起点则死锁。
            // 不依赖后台扫描调度,消除 CI 高负载下 Timer 回调延迟导致的偶发漏检。
            DetectDeadlockFromCurrentFlow(currentFlowId);
        }
    }

    /// <summary>
    /// 清除等待标记（未获取到锁时调用，如超时/取消）。不记录持有。
    /// </summary>
    internal static void OnWaitEnd(int id, string name) {
        if (_locks.TryGetValue(id, out var info)) {
            var waitTicks = info.WaitStartedTicks;
            var waited = waitTicks != 0
                ? TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - waitTicks)
                : TimeSpan.Zero;
            if (waited > _waitTimeoutThreshold && IsEnabled) {
                Emit(
                    $"[LOCK-WAIT-ABORT] 锁 '{name}' (#{id}) 等待 {waited.TotalSeconds:F1}s 后未获取(超时/取消)。" +
                    $"流: {ResolveFlowId()}");
            }
            info.WaitingFlowId = 0;
            info.WaitStartedTicks = 0;
            info.WaitStack = null;
        }
    }

    /// <summary>
    /// 记录锁等待超时 — <c>AsyncLock.TryLock</c> 超时返回 null 时调用,*。
    /// 通过 <see cref="DiagnosticSink"/> 输出诊断,便于定位"哪个锁等太久"。
    /// </summary>
    internal static void OnLockTimeout(string name, TimeSpan timeout) {
        if (!IsEnabled) return;
        Emit(
            $"[LOCK-TIMEOUT] 锁 '{name}' 等待 {timeout.TotalSeconds:F1}s 超时,返回 null。" +
            $"流: {ResolveFlowId()}");
    }

    /// <summary>
    /// 记录锁获取成功（清除等待标记 + 记录持有信息）。
    /// </summary>
    internal static void OnAcquired(int id, string name) {
        if (!IsEnabled) return;
        if (_locks.TryGetValue(id, out var info)) {
            var waitTicks = info.WaitStartedTicks;
            var waited = waitTicks != 0
                ? TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - waitTicks)
                : TimeSpan.Zero;
            if (waited > _waitTimeoutThreshold) {
                Emit(
                    $"[LOCK-WAIT-SLOW] 锁 '{name}' (#{id}) 等待 {waited.TotalSeconds:F1}s " +
                    $"才获取成功(超过阈值 {_waitTimeoutThreshold.TotalSeconds:F1}s)。" +
                    $"获取流: {ResolveFlowId()}");
            }
            Emit(
                $"[LOCK-ACQUIRED] 锁 '{name}' (#{id}) 流 {ResolveFlowId()} " +
                $"获取成功,等待 {waited.TotalSeconds:F3}s。");
            info.HoldingFlowId = ResolveFlowId();
            info.AcquiredTicks = DateTimeOffset.UtcNow.Ticks;
            info.AcquireStack = CaptureStackTrace(skipFrames: 3);
            info.WaitingFlowId = 0;
            info.WaitStartedTicks = 0;
            info.WaitStack = null;
        }
    }

    /// <summary>
    /// 记录锁释放。
    /// </summary>
    internal static void OnReleased(int id, string name) {
        if (_locks.TryGetValue(id, out var info)) {
            var acquiredTicks = info.AcquiredTicks;
            var heldFor = acquiredTicks != 0
                ? TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - acquiredTicks)
                : TimeSpan.Zero;
            if (heldFor > _holdTooLongThreshold && IsEnabled) {
                Emit(
                    $"[LOCK-HOLD-TOO-LONG] 锁 '{name}' (#{id}) 持有 {heldFor.TotalSeconds:F1}s " +
                    $"超过阈值 {_holdTooLongThreshold.TotalSeconds:F1}s。" +
                    $"持有流: {info.HoldingFlowId}");
            }
            if (IsEnabled)
                Emit(
                    $"[LOCK-RELEASED] 锁 '{name}' (#{id}) 流 {ResolveFlowId()} " +
                    $"释放,持有 {heldFor.TotalSeconds:F3}s。");
            info.HoldingFlowId = 0;
            info.AcquiredTicks = 0;
            info.AcquireStack = null;
        }
    }

    private static bool IsEnabled => Interlocked.CompareExchange(ref _diagnosticsEnabled, 0, 0) != 0;

    private static Exception? _lastSinkError;

    private static void Emit(string msg) {
        var sink = _diagnosticSink;
        if (sink is null) return;
        try { sink($"[{DateTimeOffset.UtcNow:HH:mm:ss.fff}] {msg}"); } catch (Exception ex) { Volatile.Write(ref _lastSinkError, ex); }
    }

    /// <summary>
    /// 捕获当前调用栈。用 <see cref="Environment.StackTrace"/>（AOT 兼容，返回方法名）。
    /// 跳过前 <paramref name="skipFrames"/> 帧内部诊断代码，最多保留 25 帧。
    /// </summary>
    private static string CaptureStackTrace(int skipFrames) {
        try {
            var stack = Environment.StackTrace;
            var sb = new StringBuilder(256);
            var count = 0;
            foreach (var rawLine in stack.Split('\n')) {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0) continue;
                if (skipFrames > 0) { skipFrames--; continue; }
                if (count >= 25) break;
                sb.Append("    ").Append(line).Append('\n');
                count++;
            }
            return sb.ToString();
        } catch (Exception ex) {
            Volatile.Write(ref _lastSinkError, ex);
            return "<stack unavailable>";
        }
    }

    /// <summary>
    /// 输出所有锁的实时状态，卡死时调用以定位死锁。
    /// </summary>
    public static string DumpAll() {
        var sb = new StringBuilder(512);
        var now = DateTimeOffset.UtcNow;
        sb.Append($"[LOCK-DUMP] 共 {_locks.Count} 把锁，时间 {now:HH:mm:ss.fff}\n");
        foreach (var info in _locks.Values.OrderBy(x => x.Id)) {
            string status;
            if (info.HoldingFlowId != 0) {
                var acquiredTicks = info.AcquiredTicks;
                var held = acquiredTicks != 0 ? TimeSpan.FromTicks(now.Ticks - acquiredTicks) : TimeSpan.Zero;
                status = $"持有中(流 {info.HoldingFlowId}, 已持有 {held.TotalSeconds:F1}s)";
            } else if (info.WaitingFlowId != 0) {
                var waitTicks = info.WaitStartedTicks;
                var waited = waitTicks != 0 ? TimeSpan.FromTicks(now.Ticks - waitTicks) : TimeSpan.Zero;
                status = $"等待中(流 {info.WaitingFlowId}, 已等 {waited.TotalSeconds:F1}s)";
            } else {
                status = "空闲";
            }
            sb.Append($"  #{info.Id} '{info.Name}' — {status}\n");
            if (info.WaitingFlowId != 0)
                sb.Append($"    (等待中)\n");
            if (info.HoldingFlowId != 0 && info.AcquireStack is { Length: > 0 })
                sb.Append("    获取调用栈:\n").Append(info.AcquireStack);
            if (info.WaitingFlowId != 0 && info.WaitStack is { Length: > 0 })
                sb.Append("    等待调用栈:\n").Append(info.WaitStack);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 启动后台扫描线程，定时检测持有/等待时间过长的锁并告警。
    /// 幂等：重复调用仅更新扫描间隔。
    /// </summary>
    public static void StartBackgroundScan(TimeSpan? interval = null) {
        if (interval.HasValue) _scanInterval = interval.Value;
        _scanTimer?.Dispose();
        _scanTimer = new Timer(static _ => ScanHoldsSafe(), null, _scanInterval, _scanInterval);
        Interlocked.Exchange(ref _scanStarted, 1);
        if (IsEnabled)
            Emit($"[LOCK-SCAN-START] 后台扫描启动,间隔 {_scanInterval.TotalSeconds:F1}s。");
    }

    /// <summary>
    /// 停止后台扫描。
    /// </summary>
    public static void StopBackgroundScan() {
        _scanTimer?.Dispose();
        _scanTimer = null;
        Interlocked.Exchange(ref _scanStarted, 0);
        if (IsEnabled)
            Emit($"[LOCK-SCAN-STOP] 后台扫描停止。");
    }

    private static void EnsureScanStarted() {
        if (Interlocked.CompareExchange(ref _scanStarted, 0, 0) == 0
            && Interlocked.CompareExchange(ref _scanStarted, 1, 0) == 0) {
            StartBackgroundScan();
        }
    }

    /// <summary>
    /// 后台扫描入口 — 吞掉所有异常,保证 Timer 回调永不抛出(否则终止进程)。
    /// 这是纵深防御的兜底层:即使 ScanHolds 内部出现任何未预见异常,也只记日志不崩进程。
    /// </summary>
    private static void ScanHoldsSafe() {
        try {
            ScanHolds();
        } catch (Exception ex) {
            Emit($"[LOCK-SCAN-ERROR] 后台扫描异常,已吞并以继续: {ex}");
        }
    }

    private static void ScanHolds() {
        if (!IsEnabled) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var info in _locks.Values) {
            var holdingFlowId = info.HoldingFlowId;
            var acquiredTicks = info.AcquiredTicks;
            var held = acquiredTicks != 0 ? TimeSpan.FromTicks(now.Ticks - acquiredTicks) : TimeSpan.Zero;
            if (holdingFlowId != 0 && held > _holdTooLongThreshold) {
                Emit(
                    $"[LOCK-SCAN-HOLD] 锁 '{info.Name}' (#{info.Id}) 持有 {held.TotalSeconds:F1}s " +
                    $"超过阈值(流 {holdingFlowId})。\n{info.AcquireStack}");
            }
            var waitingFlowId = info.WaitingFlowId;
            var waitTicks = info.WaitStartedTicks;
            var waited = waitTicks != 0 ? TimeSpan.FromTicks(now.Ticks - waitTicks) : TimeSpan.Zero;
            if (waitingFlowId != 0 && waited > _waitTimeoutThreshold) {
                Emit(
                    $"[LOCK-SCAN-WAIT] 锁 '{info.Name}' (#{info.Id}) 等待 {waited.TotalSeconds:F1}s " +
                    $"超过阈值(流 {waitingFlowId})。\n{info.WaitStack}");
            }
        }
        DetectDeadlock();
    }

    /// <summary>
    /// 检测死锁环（wait-for graph DFS）。每个线程最多等一把锁，出度≤1，沿等待边走回到起点即死锁。
    /// 只考虑等待超过 <see cref="_waitTimeoutThreshold"/> 的锁，避免 FlowId 复用下 stale 误报。
    /// 检测到死锁时自动通过 DiagnosticSink 输出完整诊断（锁链+线程+调用栈），无需手动调用。
    /// </summary>
    internal static void DetectDeadlock() {
        if (!IsEnabled) return;
        var waitEdges = BuildWaitEdges(exemptThreshold: false);
        if (waitEdges.Count == 0) return;
        Emit($"[LOCK-DEADLOCK-SCAN] 检查 {waitEdges.Count} 条等待边: {string.Join(", ", waitEdges.Select(e => $"F{e.Key}→F{e.Value.holderFlowId}(#{e.Value.lk.Id})"))}");
        foreach (var startId in waitEdges.Keys) {
            if (FindCycleFrom(waitEdges, startId, out var chain)) {
                EmitDeadlockReport(chain);
                return;
            }
        }
    }

    /// <summary>
    /// 即时死锁检测 — 在 <see cref="OnWaitStart"/> 时从当前流出发沿等待边走,若回到起点则死锁。
    /// 豁免等待门槛(刚加入的边等待时间≈0,且环上所有边均为"正在等待"的活跃状态),
    /// 不依赖后台扫描调度,消除 CI 高负载下 Timer 回调延迟导致的偶发漏检。
    /// </summary>
    internal static void DetectDeadlockFromCurrentFlow(int startFlowId) {
        if (!IsEnabled || startFlowId == 0) return;
        var waitEdges = BuildWaitEdges(exemptThreshold: true);
        if (waitEdges.Count == 0) return;
        if (FindCycleFrom(waitEdges, startFlowId, out var chain))
            EmitDeadlockReport(chain);
    }

    /// <summary>
    /// 构建 wait-for graph 的等待边集合。<paramref name="exemptThreshold"/> 为 true 时豁免等待门槛(即时检测用),
    /// 为 false 时仅纳入等待超过 <see cref="_waitTimeoutThreshold"/> 的边(后台扫描用,避免 FlowId 复用下 stale 误报)。
    /// </summary>
    private static Dictionary<int, (int holderFlowId, LockInfo lk)> BuildWaitEdges(bool exemptThreshold) {
        var now = DateTimeOffset.UtcNow;
        var edges = new Dictionary<int, (int holderFlowId, LockInfo lk)>();
        foreach (var info in _locks.Values) {
            var waitingFlowId = info.WaitingFlowId;
            var holdingFlowId = info.HoldingFlowId;
            if (waitingFlowId == 0 || holdingFlowId == 0)
                continue;
            if (!exemptThreshold) {
                var waitStart = info.WaitStartedTicks;
                if (waitStart == 0 || now.Ticks - waitStart < _waitTimeoutThreshold.Ticks)
                    continue;
            }
            edges[waitingFlowId] = (holdingFlowId, info);
        }
        return edges;
    }

    /// <summary>
    /// 从 <paramref name="startId"/> 出发沿等待边走,若回到起点则找到死锁环。
    /// </summary>
    private static bool FindCycleFrom(Dictionary<int, (int holderFlowId, LockInfo lk)> waitEdges, int startId, out List<(int flowId, LockInfo lk)> chain) {
        chain = new List<(int flowId, LockInfo lk)>();
        var current = startId;
        for (var step = 0; step <= waitEdges.Count; step++) {
            if (!waitEdges.TryGetValue(current, out var edge))
                return false;
            chain.Add((current, edge.lk));
            current = edge.holderFlowId;
            if (current == startId)
                return true;
        }
        return false;
    }

    private static void EmitDeadlockReport(List<(int flowId, LockInfo lk)> chain) {
        var sb = new StringBuilder(512);
        sb.Append($"[DEADLOCK-DETECTED] 检测到死锁环（{chain.Count} 把锁）\n");
        for (var i = 0; i < chain.Count; i++) {
            var (flowId, lk) = chain[i];
            var next = chain[(i + 1) % chain.Count];
            sb.Append($"  流{flowId} 持有锁 '{lk.Name}' (#{lk.Id})，等待锁 '{next.lk.Name}' (#{next.lk.Id})\n");
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
    internal static void ClearForTesting() {
        StopBackgroundScan();
        _locks.Clear();
        Interlocked.Exchange(ref _nextId, 0);
        Interlocked.Exchange(ref _nextFlowId, 0);
        Interlocked.Exchange(ref _deadlockDetected, 0);
        Volatile.Write(ref _lastDeadlockReport, null);
        AsyncFlowIdentity.Clear();
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

internal sealed class LockInfo {
    public int Id;
    public string Name = "";
    public int HoldingFlowId;
    public long AcquiredTicks;
    public string? AcquireStack;
    public int WaitingFlowId;
    public long WaitStartedTicks;
    public string? WaitStack;
}