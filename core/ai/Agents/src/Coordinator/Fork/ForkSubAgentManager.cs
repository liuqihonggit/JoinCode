namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 管理器依赖项 — 聚合非管道服务，减少构造函数参数
/// </summary>
[Register(typeof(ForkManagerDependencies), ServiceLifetime.Singleton)]
public sealed record ForkManagerDependencies(
    IAgentLifecycleManager LifecycleManager,
    IMailbox MessageBroker,
    IAgentWorktreeManager? WorktreeManager = null,
    IMailboxPoller? MailboxPoller = null,
    ITelemetryService? TelemetryService = null);

[Register(typeof(IForkSubAgentManager), ServiceLifetime.Singleton)]
public sealed partial class ForkSubAgentManager : IForkSubAgentManager, IAsyncDisposable, ISubAgentConcurrencyUpdater
{
    private sealed class ForkEntry
    {
        private ForkState _state = ForkState.Running;

        /// <summary>Fork 状态 — setter 校验转换合法性，非法转换抛 InvalidOperationException</summary>
        public ForkState State
        {
            get => _state;
            set
            {
                if (!ForkStateTransitions.CanTransitionTo(_state, value))
                {
                    throw new InvalidOperationException(
                        $"[FORK-ILLEGAL] 非法 Fork 状态转换: {_state} → {value}");
                }
                _state = value;
            }
        }

        public string? Result;
        public required string ParentSessionId;
        public string? AgentId;
        public ProgressTracker? ProgressTracker = null;
        public CancellationTokenSource? Cts;
        public DateTime CreatedAt;
    }

    private readonly MiddlewarePipeline<ForkContext> _pipeline;
    private readonly ForkManagerDependencies _deps;
    private readonly ILogger<ForkSubAgentManager>? _logger;
    private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, ForkEntry> _entries;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _sharedCache;
    private readonly AsyncLock _lock = new();
    private volatile AsyncLock? _forkSemaphore;

    public event EventHandler<ForkCompletedEventArgs>? ForkCompleted;

    public ForkSubAgentManager(
        MiddlewarePipeline<ForkContext> pipeline,
        ForkManagerDependencies deps,
        ILogger<ForkSubAgentManager>? logger = null,
        IClockService? clock = null,
        SubAgentConcurrencyOptions? concurrencyOptions = null)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _deps = deps ?? throw new ArgumentNullException(nameof(deps));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _entries = new ConcurrentDictionary<string, ForkEntry>();
        _sharedCache = new ConcurrentDictionary<string, Dictionary<string, string>>();


        var maxForks = (concurrencyOptions ?? new SubAgentConcurrencyOptions()).MaxConcurrentForks;
        _forkSemaphore = maxForks > 0
            ? new AsyncLock("Fork-Concurrency", maxForks, maxForks)
            : null;
    }

    public async Task<ForkResult> ForkAsync(ForkOptions options, CancellationToken ct = default)
    {
        var sem = _forkSemaphore;
        IDisposable? releaser = null;
        if (sem is not null)
        {
            try
            {
                releaser = await sem.TryLockAsync(ct).ConfigureAwait(false)
                    ?? throw new System.TimeoutException($"锁 '{sem.Name}' 等待超时");
            }
            catch (ObjectDisposedException)
            {
                sem = _forkSemaphore;
                if (sem is not null)
                    releaser = await sem.TryLockAsync(ct).ConfigureAwait(false)
                        ?? throw new System.TimeoutException($"锁 '{sem.Name}' 等待超时");
            }
        }
        var semaphoreTransferredToBackground = false;
        try
        {
        // 预计算 Fork 深度（需要访问 _entries 内部状态）
        var forkDepth = CalculateForkDepth(options.ParentSessionId);

        var forkId = $"fork-{Guid.NewGuid():N}";
        var createdAt = _clock.GetUtcNow();

        var context = new ForkContext
        {
            Options = options,
            ForkId = forkId,
            CreatedAt = createdAt,
            ForkDepth = forkDepth,
            CancellationToken = ct
        };

        // 在管道执行前设置缓存和条目（确保 CancelForkAsync 可见）
        SetupForkEntry(options, forkId, createdAt, context);

        // 执行中间件管道
        try
        {
            await _pipeline.ExecuteAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (_entries.TryGetValue(forkId, out var cancelEntry))
                cancelEntry.State = ForkState.Cancelled;
            RecordForkMetrics("fork_cancelled", false);
            _logger?.LogInformation("Fork {ForkId} was cancelled", forkId);

            await FireForkCompletedAsync(forkId, options.TaskDescription).ConfigureAwait(false);

            return BuildForkResult(forkId);
        }
        catch (Exception ex)
        {
            if (_entries.TryGetValue(forkId, out var errorEntry))
            {
                errorEntry.State = ForkState.Failed;
                errorEntry.Result = ex.Message;
            }
            RecordForkMetrics("fork_error", false);
            _logger?.LogError(ex, "Fork {ForkId} failed with exception", forkId);

            await FireForkCompletedAsync(forkId, options.TaskDescription).ConfigureAwait(false);

            return BuildForkResult(forkId);
        }

        // 验证失败: 清理条目并返回
        if (!context.IsValidated)
        {
            CleanupForkEntry(forkId);
            return new ForkResult
            {
                ForkId = context.ForkId,
                State = ForkState.Failed,
                Result = context.ValidationFailureReason
            };
        }

        // 更新条目中的 AgentId
        if (context.Agent is not null && _entries.TryGetValue(forkId, out var spawnEntry))
        {
            spawnEntry.AgentId = context.Agent.ObjectId.UniqueId;
        }

        // 后台模式: 启动后台任务
        if (context.IsBackground && context.Agent is not null)
        {
            var forkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (_entries.TryGetValue(forkId, out var bgEntry))
                bgEntry.Cts = forkCts;

            // 先读取 token 到局部变量 — RunBackgroundForkAsync 的 finally 块会 dispose forkCts,
            // 若 RunBackgroundForkAsync 同步完成(如 ExecuteAsync 同步抛出异常),
            // .WaitAsync 再访问 forkCts.Token 会抛 ObjectDisposedException
            var forkToken = forkCts.Token;
            semaphoreTransferredToBackground = true;
            _ = RunBackgroundForkAsync(forkId, context.Agent, options.TaskDescription, options.EventChannel, forkToken, releaser)
                .WaitAsync(TimeSpan.FromSeconds(10), forkToken).ConfigureAwait(false);

            return new ForkResult
            {
                ForkId = forkId,
                State = ForkState.Running,
                SharedCache = _sharedCache.GetValueOrDefault(forkId, new Dictionary<string, string>())
            };
        }

        // 同步模式: 更新条目最终状态
        if (_entries.TryGetValue(forkId, out var syncEntry))
        {
            syncEntry.State = context.FinalState;
            syncEntry.Result = context.FinalResult;
        }

        await FireForkCompletedAsync(forkId, options.TaskDescription).ConfigureAwait(false);

        return BuildForkResult(forkId);
        }
        finally
        {
            if (!semaphoreTransferredToBackground)
            {
                try { releaser?.Dispose(); }
                catch (ObjectDisposedException) { _logger?.LogDebug("fork 信号量在 Release 时已被热重载 Dispose"); }
            }
        }
    }

    /// <summary>在管道执行前设置缓存和条目（确保 CancelForkAsync 可见）</summary>
    private void SetupForkEntry(ForkOptions options, string forkId, DateTime createdAt, ForkContext context)
    {
        using var guard = _lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");
        if (options.ShareCache)
        {
            var parentCache = _sharedCache.GetValueOrDefault(options.ParentSessionId, new Dictionary<string, string>());
            _sharedCache[options.ParentSessionId] = parentCache;
            _sharedCache[forkId] = parentCache;
            context.SharedCache = parentCache;
        }
        else
        {
            var forkCache = new Dictionary<string, string>();
            _sharedCache[forkId] = forkCache;
            context.SharedCache = forkCache;
        }

        _entries[forkId] = new ForkEntry
        {
            State = ForkState.Running,
            ParentSessionId = options.ParentSessionId,
            CreatedAt = createdAt
        };
    }

    /// <summary>清理失败的 Fork 条目和缓存</summary>
    private void CleanupForkEntry(string forkId)
    {
        using var guard = _lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");
        _entries.TryRemove(forkId, out _);
        _sharedCache.TryRemove(forkId, out _);
    }

    public async Task<IReadOnlyList<ForkSubAgent>> GetActiveForksAsync(CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return _entries
            .Where(kvp => kvp.Value.State == ForkState.Running
                       || kvp.Value.State == ForkState.Completed
                       || kvp.Value.State == ForkState.Failed)
            .Select(kvp => new ForkSubAgent
            {
                ForkId = kvp.Key,
                ParentSessionId = kvp.Value.ParentSessionId,
                State = kvp.Value.State,
                CreatedAt = kvp.Value.CreatedAt,
                Result = kvp.Value.Result
            })
            .ToList();
    
    }

    public async Task<ForkResult> MergeForkAsync(string forkId, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (!_entries.TryGetValue(forkId, out var entry))
        {
            return new ForkResult
            {
                ForkId = forkId,
                State = ForkState.Failed,
                Result = "Fork not found"
            };
        }

        if (entry.State != ForkState.Completed)
        {
            return new ForkResult
            {
                ForkId = forkId,
                State = entry.State,
                Result = $"Fork is in {entry.State} state, cannot merge"
            };
        }

        var parentSessionId = entry.ParentSessionId;

        if (IsSharedCacheForFork(forkId, parentSessionId))
        {
            var forkCache = _sharedCache.GetValueOrDefault(forkId, new Dictionary<string, string>());
            var parentCache = _sharedCache.GetValueOrDefault(parentSessionId, new Dictionary<string, string>());

            foreach (var kvp in forkCache)
            {
                parentCache[kvp.Key] = kvp.Value;
            }

            _sharedCache[parentSessionId] = parentCache;
        }

        entry.State = ForkState.Merged;

        _logger?.LogInformation("Fork {ForkId} merged into parent {ParentSessionId}",
            forkId, parentSessionId);
    

        var cache = _sharedCache.GetValueOrDefault(forkId, new Dictionary<string, string>());
        var mergedEntry = _entries.GetValueOrDefault(forkId);

        return new ForkResult
        {
            ForkId = forkId,
            State = ForkState.Merged,
            Result = mergedEntry?.Result,
            SharedCache = cache
        };
    }

    public async Task CancelForkAsync(string forkId, CancellationToken ct = default)
    {
        ForkEntry? entry;
        using (var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            if (!_entries.TryGetValue(forkId, out entry))
                return;
            if (entry.State != ForkState.Running)
                return;
        }

        if (entry.Cts is not null)
        {
            await entry.Cts.CancelAsync().ConfigureAwait(false);
            entry.Cts.Dispose();
            entry.Cts = null;
        }

        string? agentIdToCancel = null;
        if (entry.AgentId is not null)
        {
            agentIdToCancel = entry.AgentId;
            entry.AgentId = null;
            StopMailboxPollingIfNeeded(agentIdToCancel);
        }

        if (agentIdToCancel is not null)
        {
            await _deps.LifecycleManager.CancelAgentAsync(agentIdToCancel, ct).ConfigureAwait(false);
        }

        using (var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            entry.State = ForkState.Cancelled;
        }

        _logger?.LogInformation("Fork {ForkId} cancelled", forkId);

        await FireForkCompletedAsync(forkId, string.Empty).ConfigureAwait(false);
    }

    private async Task RunBackgroundForkAsync(string forkId, IAgent agent, string taskDescription,
        JoinCode.Abstractions.LLM.Chat.SubAgentEventChannel? eventChannel, CancellationToken cancellationToken,
        IDisposable? forkReleaser = null)
    {
        // 终态发射辅助 — 通道由调用方在回合作用域内捕获传入；
        // fork 完成晚于回合时事件写入死通道自然丢弃（GUI 靠 task-notification 回填补足）
        void EmitFinished(bool success, string? output)
        {
            if (eventChannel is null)
                return;
            _entries.TryGetValue(forkId, out var entry);
            eventChannel.Emit(JoinCode.Abstractions.LLM.Chat.ChatStreamEvent.AgentFinished(
                entry?.AgentId ?? forkId,
                success: success,
                finalOutput: output));
        }

        try
        {
            var result = await _deps.LifecycleManager.ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);

            if (_entries.TryGetValue(forkId, out var entry))
            {
                if (result.IsSuccess)
                {
                    entry.State = ForkState.Completed;
                    entry.Result = result.Output;
                    RecordForkMetrics("fork_background", true);
                    _logger?.LogInformation("Background Fork {ForkId} completed successfully", forkId);
                }
                else
                {
                    entry.State = ForkState.Failed;
                    entry.Result = result.Error ?? "Unknown error";
                    RecordForkMetrics("fork_background", false);
                    _logger?.LogWarning("Background Fork {ForkId} failed: {Error}", forkId, result.Error);
                }
            }

            EmitFinished(result.IsSuccess, result.IsSuccess ? result.Output : result.Error);
            await FireForkCompletedAsync(forkId, taskDescription).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (_entries.TryGetValue(forkId, out var cancelEntry))
                cancelEntry.State = ForkState.Cancelled;
            RecordForkMetrics("fork_background_cancelled", false);
            _logger?.LogInformation("Background Fork {ForkId} was cancelled", forkId);

            EmitFinished(success: false, "已取消");
            await FireForkCompletedAsync(forkId, taskDescription).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_entries.TryGetValue(forkId, out var errorEntry))
            {
                errorEntry.State = ForkState.Failed;
                errorEntry.Result = ex.Message;
            }
            RecordForkMetrics("fork_background_error", false);
            _logger?.LogError(ex, "Background Fork {ForkId} failed with exception", forkId);

            EmitFinished(success: false, ex.Message);
            await FireForkCompletedAsync(forkId, taskDescription).ConfigureAwait(false);
        }
        finally
        {
            if (_entries.TryGetValue(forkId, out var finallyEntry) && finallyEntry.Cts is not null)
            {
                finallyEntry.Cts.Dispose();
                finallyEntry.Cts = null;
            }
            if (forkReleaser is not null)
            {
                try { forkReleaser.Dispose(); }
                catch (ObjectDisposedException) { _logger?.LogDebug("fork 信号量在后台完成 Release 时已被热重载 Dispose"); }
            }
        }
    }

    private async Task FireForkCompletedAsync(string forkId, string taskDescription)
    {
        try
        {
            var entry = _entries.GetValueOrDefault(forkId);
            if (entry is null) return;

            string? worktreePath = null;

            if (_deps.WorktreeManager != null && entry.AgentId is not null)
            {
                var session = await _deps.WorktreeManager.GetWorktreeSessionAsync(entry.AgentId).ConfigureAwait(false);
                worktreePath = session?.WorktreePath;
            }

            ForkCompleted?.Invoke(this, new ForkCompletedEventArgs
            {
                ForkId = forkId,
                State = entry.State,
                TaskDescription = taskDescription,
                Result = entry.Result,
                Error = entry.State == ForkState.Failed ? entry.Result : null,
                WorktreePath = worktreePath
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to fire ForkCompleted event for {ForkId}", forkId);
        }
    }

    private ForkResult BuildForkResult(string forkId)
    {
        var cache = _sharedCache.GetValueOrDefault(forkId, new Dictionary<string, string>());
        var entry = _entries.GetValueOrDefault(forkId);

        return new ForkResult
        {
            ForkId = forkId,
            State = entry?.State ?? ForkState.Failed,
            Result = entry?.Result,
            SharedCache = cache
        };
    }

    private bool IsSharedCacheForFork(string forkId, string parentSessionId)
    {
        if (!_sharedCache.TryGetValue(forkId, out var forkCache)) return false;
        if (!_sharedCache.TryGetValue(parentSessionId, out var parentCache)) return false;
        return ReferenceEquals(forkCache, parentCache);
    }

    private void RecordForkMetrics(string operation, bool isSuccess)
        => _deps.TelemetryService?.RecordCount("fork.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Fork operation count");

    private int CalculateForkDepth(string parentSessionId)
    {
        var depth = 0;
        var current = parentSessionId;

        for (var i = 0; i < 100; i++)
        {
            var entry = _entries.GetValueOrDefault(current);
            if (entry is null) break;
            depth++;
            current = entry.ParentSessionId;
        }

        return depth;
    }

    private void StopMailboxPollingIfNeeded(string agentId)
    {
        if (_deps.MailboxPoller == null) return;

        try
        {
            var sessionId = _deps.MessageBroker.GetSessionId(agentId);
            if (sessionId is not null)
            {
                _deps.MailboxPoller.StopPolling(agentId, sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to stop mailbox polling for agent {AgentId}", agentId);
        }
    }

    /// <summary>
    /// 热重载 fork 并发上限 — 原子替换 SemaphoreSlim，旧的 Dispose（ADR 0048）
    /// </summary>
    public void UpdateConcurrencyOptions(SubAgentConcurrencyOptions options)
    {
        var maxForks = options.MaxConcurrentForks;
        var newSem = maxForks > 0
            ? new AsyncLock("Fork-Concurrency", maxForks, maxForks)
            : null;
        var old = Interlocked.Exchange(ref _forkSemaphore, newSem);
        old?.Dispose();
        _logger?.LogInformation("fork 并发上限已热重载为 {Limit}", maxForks);
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupForkEntriesAsync().ConfigureAwait(false);
        _lock.Dispose();
        _forkSemaphore?.Dispose();
    }

    /// <summary>清理所有 Fork 条目和缓存（在锁保护下执行）</summary>
    private async Task CleanupForkEntriesAsync()
    {
        using var guard = _lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        var ctsEntries = _entries.Values
            .Select(e => e.Cts)
            .OfType<CancellationTokenSource>()
            .ToList();
        if (ctsEntries.Count > 0)
        {
            await Task.WhenAll(ctsEntries.Select(c => c.CancelAsync())).ConfigureAwait(false);
            foreach (var cts in ctsEntries)
            {
                cts.Dispose();
            }
        }
        _entries.Clear();
        _sharedCache.Clear();
    }}
