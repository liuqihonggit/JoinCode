namespace Services.SystemActuator;

/// <summary>
/// 工具执行信息 — 记录每个工具（如 bash）的子进程生命周期（ADR 0152 session 级别工具 map）
/// </summary>
/// <param name="ToolName">工具唯一描述名（如 "bash:ls" / "bash:dotnet build"）</param>
/// <param name="ProcessId">子进程 ID</param>
/// <param name="StartTime">开始时间（UTC）</param>
/// <param name="TimeoutDuration">超时时间（总超时上限）</param>
/// <param name="WarningThreshold">警报时间（到此时异步提示宿主代理"工具执行时间较长"）</param>
/// <param name="KillThreshold">真实杀死时间（到此时强制杀死子进程）</param>
/// <param name="AgentId">启动此工具的子代理 ID（子代理崩溃时杀死其所有工具子进程）</param>
public sealed record ToolLifecycleInfo(
    string ToolName,
    int ProcessId,
    DateTime StartTime,
    TimeSpan TimeoutDuration,
    TimeSpan WarningThreshold,
    TimeSpan KillThreshold,
    string? AgentId = null
) {
    /// <summary>是否到达警报时间</summary>
    public bool ShouldWarn(DateTime now) => now - StartTime >= WarningThreshold;

    /// <summary>是否到达杀死时间</summary>
    public bool ShouldKill(DateTime now) => now - StartTime >= KillThreshold;

    /// <summary>是否已超时</summary>
    public bool IsTimedOut(DateTime now) => now - StartTime >= TimeoutDuration;
}

/// <summary>
/// session 级别工具生命周期 map — 每个会话持有，管理所有工具子进程的生命周期（ADR 0152）
/// </summary>
public sealed class SessionToolMap : IToolLifecycleKiller {
    private readonly ConcurrentDictionary<string, ToolLifecycleInfo> _tools = new(StringComparer.Ordinal);
    private readonly ILogger? _logger;

    /// <summary>构造函数 — 注入可选日志记录器。</summary>
    /// <param name="logger">日志记录器（可选）。</param>
    public SessionToolMap(ILogger? logger = null) {
        _logger = logger;
    }

    /// <summary>注册工具执行信息</summary>
    public void Register(ToolLifecycleInfo info) {
        _tools[info.ToolName] = info;
        _logger?.LogDebug("[SessionToolMap] 注册工具 {Name} PID={Pid} Agent={Agent}", info.ToolName, info.ProcessId, info.AgentId);
    }

    /// <summary>注销工具（执行完成时调用）</summary>
    public bool Unregister(string toolName) {
        var removed = _tools.TryRemove(toolName, out var info);
        if (removed && info is not null)
            _logger?.LogDebug("[SessionToolMap] 注销工具 {Name} PID={Pid}", toolName, info.ProcessId);
        return removed;
    }

    /// <summary>获取工具信息</summary>
    public ToolLifecycleInfo? Get(string toolName) => _tools.TryGetValue(toolName, out var info) ? info : null;

    /// <summary>获取所有工具</summary>
    public IReadOnlyDictionary<string, ToolLifecycleInfo> GetAll() => _tools;

    /// <summary>获取指定子代理的所有工具</summary>
    public IReadOnlyList<ToolLifecycleInfo> GetByAgent(string agentId) =>
        [.. _tools.Values.Where(t => t.AgentId == agentId)];

    /// <summary>杀死指定子代理的所有工具子进程 — 子代理崩溃时调用（ADR 0152 双向绑定）</summary>
    public int KillByAgent(string agentId) {
        var tools = GetByAgent(agentId);
        var killed = 0;
        foreach (var info in tools) {
            if (TryKillProcess(info))
                killed++;
        }
        return killed;
    }

    /// <summary>杀死所有工具子进程 — session 结束时调用（兜底）</summary>
    public int KillAll() {
        var killed = 0;
        foreach (var kvp in _tools) {
            if (TryKillProcess(kvp.Value))
                killed++;
        }
        _tools.Clear();
        return killed;
    }

    /// <summary>检查超时工具 — 返回需要警报和需要杀死的工具列表（定时器调用）</summary>
    public (List<ToolLifecycleInfo> ToWarn, List<ToolLifecycleInfo> ToKill) CheckTimeouts(DateTime now) {
        var toWarn = new List<ToolLifecycleInfo>();
        var toKill = new List<ToolLifecycleInfo>();

        foreach (var kvp in _tools) {
            var info = kvp.Value;
            if (info.ShouldKill(now))
                toKill.Add(info);
            else if (info.ShouldWarn(now))
                toWarn.Add(info);
        }

        return (toWarn, toKill);
    }

    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;

    /// <summary>启动后台监控 — 定期检查超时工具，警报回调 + 杀死回调（ADR 0152）</summary>
    /// <param name="onWarn">警报回调（工具到达 WarningThreshold 时调用，异步提示宿主代理）</param>
    /// <param name="onKill">杀死回调（工具到达 KillThreshold 时调用，通知宿主代理后强制杀死）</param>
    /// <param name="checkInterval">检查间隔（默认 5s）</param>
    /// <param name="ct">取消令牌</param>
    public void StartMonitoring(Func<ToolLifecycleInfo, Task> onWarn, Func<ToolLifecycleInfo, Task> onKill, TimeSpan? checkInterval = null, CancellationToken ct = default) {
        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var interval = checkInterval ?? TimeSpan.FromSeconds(5);

        _monitoringTask = Task.Run(async () => {
            while (!_monitoringCts.Token.IsCancellationRequested) {
                try {
                    await Task.Delay(interval, _monitoringCts.Token).ConfigureAwait(false);
                    var now = DateTime.UtcNow;
                    var (toWarn, toKill) = CheckTimeouts(now);
                    foreach (var info in toWarn)
                        await onWarn(info).ConfigureAwait(false);
                    foreach (var info in toKill) {
                        await onKill(info).ConfigureAwait(false);
                        TryKillProcess(info);
                    }
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "[SessionToolMap] 监控循环异常");
                }
            }
        }, _monitoringCts.Token);
    }

    /// <summary>停止后台监控（不等待 — 释放函数禁止超时，ADR 0152）</summary>
    public void StopMonitoring() {
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _monitoringCts = null;
    }

    private bool TryKillProcess(ToolLifecycleInfo info) {
        try {
            var process = System.Diagnostics.Process.GetProcessById(info.ProcessId);
            process.Kill(entireProcessTree: true);
            _tools.TryRemove(info.ToolName, out _);
            _logger?.LogWarning("[SessionToolMap] 杀死工具 {Name} PID={Pid} Agent={Agent}", info.ToolName, info.ProcessId, info.AgentId);
            return true;
        } catch (ArgumentException) {
            _tools.TryRemove(info.ToolName, out _);
            return false;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[SessionToolMap] 杀死工具 {Name} PID={Pid} 失败", info.ToolName, info.ProcessId);
            return false;
        }
    }
}
