namespace Core.Context;

/// <summary>
/// 流式工具执行器中单个工具的执行结果
/// </summary>
public sealed class StreamingToolResult {
    /// <summary>工具名称</summary>
    public required string ToolName { get; init; }
    /// <summary>工具调用 ID</summary>
    public required string? ToolCallId { get; init; }
    /// <summary>工具执行结果</summary>
    public required ToolCallResult Result { get; init; }
    /// <summary>原始顺序索引</summary>
    public required int OriginalIndex { get; init; }

    /// <summary>
    /// 转换为 ChatStreamEvent.ToolEnd 事件
    /// </summary>
    public ChatStreamEvent ToToolEndEvent() => ChatStreamEvent.ToolEnd(
        ToolName, Result.ResultText, ToolCallId, Result.IsError, Result.StructuredPatch);
}

/// <summary>
/// 流式工具执行器 — 对齐 TS StreamingToolExecutor
/// 在 LLM 流式响应过程中，每收到一个完整的 tool_use block 就立即尝试执行
/// 并发安全工具可并行执行，非并发安全工具独占执行
/// 结果按原始顺序缓冲，通过 GetCompletedResults 按序输出
/// </summary>
public sealed class StreamingToolExecutor : IStreamingToolExecutor {
    private readonly IToolExecutionHandler _toolHandler;
    private readonly IToolConcurrencyClassifier _concurrencyClassifier;
    private readonly ChatMiddlewareContext _context;
    private readonly int _maxConcurrency;
    private readonly ILogger? _logger;

    private ExecutorState _state = ExecutorState.Empty;
    private int _scheduling;
    private int _scheduleRequested;
    private readonly CancellationTokenSource _siblingCts = new();
    private readonly CancellationTokenSource? _linkedCts;
    private readonly CancellationToken _combinedCt;
    private volatile bool _discarded;
    private int _disposed;

    /// <summary>
    /// 初始化流式工具执行器
    /// </summary>
    public StreamingToolExecutor(
        IToolExecutionHandler toolHandler,
        IToolConcurrencyClassifier concurrencyClassifier,
        ChatMiddlewareContext context,
        int maxConcurrency = 10,
        ILogger? logger = null,
        CancellationToken userCancellationToken = default) {
        _toolHandler = toolHandler;
        _concurrencyClassifier = concurrencyClassifier;
        _context = context;
        _maxConcurrency = maxConcurrency;
        _logger = logger;

        if (userCancellationToken.CanBeCanceled) {
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(userCancellationToken, _siblingCts.Token);
            _combinedCt = _linkedCts.Token;
        } else {
            _combinedCt = _siblingCts.Token;
        }
    }

    /// <summary>
    /// 添加工具调用到队列 — 对齐 TS StreamingToolExecutor.addTool()
    /// 流式过程中每收到一个 tool_use block 就调用此方法
    /// </summary>
    public Task AddToolAsync(ToolCallEntry entry, int originalIndex) {
        if (_discarded) return Task.CompletedTask;

        var tool = new QueuedTool {
            Entry = entry,
            OriginalIndex = originalIndex,
            Status = ToolStatus.Queued,
            CompletionSource = new TaskCompletionSource<StreamingToolResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        while (true) {
            var current = Volatile.Read(ref _state);
            var updated = current with { Queue = current.Queue.Add(tool) };
            if (Interlocked.CompareExchange(ref _state, updated, current) == current) break;
        }

        TrySchedule();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取已完成的结果（按原始顺序） — 对齐 TS StreamingToolExecutor.getCompletedResults()
    /// </summary>
    public Task<IReadOnlyList<StreamingToolResult>> GetCompletedResultsAsync() {
        if (_discarded) return Task.FromResult<IReadOnlyList<StreamingToolResult>>([]);
        return Task.FromResult(DrainCompleted());
    }

    /// <summary>
    /// 等待所有剩余工具完成并返回结果 — 对齐 TS StreamingToolExecutor.getRemainingResults()
    /// </summary>
    public async Task<IReadOnlyList<StreamingToolResult>> GetRemainingResultsAsync() {
        if (_discarded) return [];

        var pendingTasks = Volatile.Read(ref _state).Queue
            .Where(t => t.Status != ToolStatus.Completed)
            .Select(t => t.CompletionSource.Task)
            .ToList();

        if (pendingTasks.Count > 0) {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }

        return await GetCompletedResultsAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 获取取消令牌 — Bash 错误级联取消兄弟工具 + 用户取消的组合令牌
    /// </summary>
    public CancellationToken CombinedCancellationToken => _combinedCt;

    /// <summary>
    /// 是否已被丢弃 — 对齐 TS StreamingToolExecutor.discarded
    /// </summary>
    public bool IsDiscarded => _discarded;

    /// <summary>
    /// 丢弃所有待处理和进行中的工具 — 对齐 TS StreamingToolExecutor.d4iscard()
    /// 在流式 fallback 发生且失败尝试的结果应被放弃时调用
    /// 排队工具不会启动，进行中工具将收到合成错误
    /// </summary>
    public void Discard() {
        _discarded = true;

        try {
            if (!_siblingCts.IsCancellationRequested)
                _siblingCts.Cancel();
        } catch (ObjectDisposedException) {
            _logger?.LogDebug("[StreamingToolExecutor] SiblingCts already disposed during discard");
        }

        while (true) {
            var current = Volatile.Read(ref _state);
            var uncompletedTools = current.Queue
                .Where(t => t.Status != ToolStatus.Completed && !t.CompletionSource.Task.IsCompleted)
                .ToList();
            var updatedQueue = current.Queue
                .Select(t => t.Status != ToolStatus.Completed ? t with { Status = ToolStatus.Completed } : t)
                .ToImmutableList();
            var updated = current with { Queue = updatedQueue, Completed = [] };

            if (Interlocked.CompareExchange(ref _state, updated, current) != current) continue;

            foreach (var tool in uncompletedTools) {
                tool.CompletionSource.TrySetResult(new StreamingToolResult {
                    ToolName = tool.Entry.Name,
                    ToolCallId = tool.Entry.Id,
                    Result = new ToolCallResult {
                        ResultText = "(discarded by streaming fallback)",
                        IsError = true
                    },
                    OriginalIndex = tool.OriginalIndex
                });
            }
            return;
        }
    }

    /// <summary>
    /// 异步释放资源，取消所有进行中工具并释放取消令牌
    /// </summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _siblingCts.Cancel();
        _linkedCts?.Dispose();
        _siblingCts.Dispose();

        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<StreamingToolResult> DrainCompleted() {
        while (true) {
            var current = Volatile.Read(ref _state);
            if (current.Completed.Count == 0) return [];
            var results = current.Completed.OrderBy(r => r.OriginalIndex).ToList();
            var updated = current with { Completed = [] };
            if (Interlocked.CompareExchange(ref _state, updated, current) == current) return results;
        }
    }

    private void TrySchedule() {
        if (_discarded) return;
        if (Interlocked.CompareExchange(ref _scheduling, 1, 0) != 0) {
            // 已有调度循环在跑 — 只记下"队列有变化"，由它在退出前再扫一次。
            // 不能在这里直接判断"还有 Queued 就重排"：排队工具可能在等执行槽位
            // （非安全工具等安全工具跑完），那会让循环空转占满线程池。
            Volatile.Write(ref _scheduleRequested, 1);
            return;
        }

        _ = FireAndForgetTask<Unit>.Run(async _ => {
            try {
                await ScheduleLoopAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogError(ex, "[StreamingToolExecutor] 调度循环异常");
            } finally {
                Volatile.Write(ref _scheduling, 0);
                if (Interlocked.Exchange(ref _scheduleRequested, 0) != 0 && !_discarded) TrySchedule();
            }
        }, "streaming-tool-schedule");
    }

    private async Task ScheduleLoopAsync() {
        while (true) {
            if (_discarded) return;

            var queuedTools = Volatile.Read(ref _state).Queue
                .Where(t => t.Status == ToolStatus.Queued)
                .ToList();
            if (queuedTools.Count == 0) return;

            var executable = await FindNextExecutableAsync(queuedTools).ConfigureAwait(false);
            if (executable is null) return;

            var current = Volatile.Read(ref _state);
            var index = current.Queue.FindIndex(t => ReferenceEquals(t.CompletionSource, executable.CompletionSource));
            if (index < 0) continue;

            var updated = current with {
                Queue = current.Queue.SetItem(index, current.Queue[index] with { Status = ToolStatus.Executing }),
                ExecutingCount = current.ExecutingCount + 1,
                NonSafeExecutingCount = current.NonSafeExecutingCount + (executable.IsConcurrencySafe ? 0 : 1)
            };
            if (Interlocked.CompareExchange(ref _state, updated, current) != current) continue;

            RunFireAndForget(() => ExecuteToolAsync(executable));
        }
    }

    /// <summary>
    /// 按队列顺序扫描首个可执行工具 — 非安全排队工具不阻塞后续安全工具（对齐原 FindNextExecutableAsync）
    /// </summary>
    private async Task<QueuedTool?> FindNextExecutableAsync(IReadOnlyList<QueuedTool> queuedTools) {
        foreach (var queued in queuedTools) {
            var candidate = queued;
            if (!candidate.IsConcurrencySafeDetermined) {
                var parsedArguments = candidate.ParsedArguments.Count == 0
                    ? JsonArgumentParser.Parse(candidate.Entry.Arguments)
                    : candidate.ParsedArguments;
                var isSafe = await _concurrencyClassifier
                    .IsConcurrencySafeAsync(candidate.Entry.Name, parsedArguments, CancellationToken.None)
                    .ConfigureAwait(false);
                UpdateTool(candidate, t => t with {
                    ParsedArguments = parsedArguments,
                    IsConcurrencySafe = isSafe,
                    IsConcurrencySafeDetermined = true
                });

                var refreshed = Volatile.Read(ref _state).Queue
                    .FirstOrDefault(t => ReferenceEquals(t.CompletionSource, candidate.CompletionSource));
                if (refreshed is null) continue;
                candidate = refreshed;
            }

            if (CanExecute(Volatile.Read(ref _state), candidate.IsConcurrencySafe))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// 判断是否可以执行 — 对齐 TS StreamingToolExecutor.canExecuteTool()
    /// 规则：无工具执行→可启动；新工具安全且当前全安全→可并发；否则等待
    /// </summary>
    private bool CanExecute(ExecutorState state, bool isConcurrencySafe) {
        if (state.ExecutingCount == 0)
            return true;

        if (state.ExecutingCount >= _maxConcurrency)
            return false;

        if (isConcurrencySafe && state.NonSafeExecutingCount == 0)
            return true;

        return false;
    }

    private async Task ExecuteToolAsync(QueuedTool tool) {
        StreamingToolResult result;

        if (_siblingCts.IsCancellationRequested) {
            result = new StreamingToolResult {
                ToolName = tool.Entry.Name,
                ToolCallId = tool.Entry.Id,
                Result = new ToolCallResult { ResultText = "(cancelled by sibling error)", IsError = true },
                OriginalIndex = tool.OriginalIndex
            };
        } else {
            try {
                if (tool.ParsedArguments.Count == 0)
                    tool = tool with { ParsedArguments = JsonArgumentParser.Parse(tool.Entry.Arguments) };
                var args = tool.ParsedArguments;
                var toolCallResult = await _toolHandler.ExecuteToolCallAsync(
                    tool.Entry.Name, tool.Entry.Id, args, _context, _combinedCt).ConfigureAwait(false);

                result = new StreamingToolResult {
                    ToolName = tool.Entry.Name,
                    ToolCallId = tool.Entry.Id,
                    Result = toolCallResult,
                    OriginalIndex = tool.OriginalIndex
                };

                if (toolCallResult.IsError && IsShellTool(tool.Entry.Name)) {
                    _logger?.LogWarning("[StreamingToolExecutor] Shell 工具错误，级联取消兄弟工具: {ToolName}", tool.Entry.Name);
                    try { _siblingCts.Cancel(); } catch (ObjectDisposedException) { _logger?.LogDebug("[StreamingToolExecutor] SiblingCts 已释放"); }
                }
            } catch (OperationCanceledException) when (_combinedCt.IsCancellationRequested) {
                result = new StreamingToolResult {
                    ToolName = tool.Entry.Name,
                    ToolCallId = tool.Entry.Id,
                    Result = new ToolCallResult { ResultText = "(cancelled by sibling error)", IsError = true },
                    OriginalIndex = tool.OriginalIndex
                };
            } catch (Exception ex) {
                _logger?.LogError(ex, "[StreamingToolExecutor] 工具执行失败: {ToolName}", tool.Entry.Name);
                result = new StreamingToolResult {
                    ToolName = tool.Entry.Name,
                    ToolCallId = tool.Entry.Id,
                    Result = new ToolCallResult { ResultText = $"工具执行失败: {ex.Message}", IsError = true },
                    OriginalIndex = tool.OriginalIndex
                };
            }
        }

        CompleteTool(tool, result);
        tool.CompletionSource.TrySetResult(result);

        TrySchedule();
    }

    private void CompleteTool(QueuedTool tool, StreamingToolResult result) {
        while (true) {
            var current = Volatile.Read(ref _state);
            var index = current.Queue.FindIndex(t => ReferenceEquals(t.CompletionSource, tool.CompletionSource));
            if (index < 0) return;

            var updatedQueue = current.Queue.SetItem(index, current.Queue[index] with { Status = ToolStatus.Completed });
            var updated = current with {
                Queue = updatedQueue,
                Completed = current.Completed.Add(result),
                ExecutingCount = Math.Max(0, current.ExecutingCount - 1),
                NonSafeExecutingCount = Math.Max(0, current.NonSafeExecutingCount - (tool.IsConcurrencySafe ? 0 : 1))
            };
            if (Interlocked.CompareExchange(ref _state, updated, current) == current) return;
        }
    }

    private void UpdateTool(QueuedTool tool, Func<QueuedTool, QueuedTool> update) {
        while (true) {
            var current = Volatile.Read(ref _state);
            var index = current.Queue.FindIndex(t => ReferenceEquals(t.CompletionSource, tool.CompletionSource));
            if (index < 0) return;

            var updated = current with { Queue = current.Queue.SetItem(index, update(current.Queue[index])) };
            if (Interlocked.CompareExchange(ref _state, updated, current) == current) return;
        }
    }

    /// <summary>
    /// 安全启动 fire-and-forget 任务 — 观察未处理异常，避免静默吞掉或泄漏。
    /// </summary>
    private void RunFireAndForget(Func<Task> taskFactory) {
        _ = FireAndForgetTask<Unit>.Run(async ct => {
            try {
                await taskFactory().ConfigureAwait(false);
            } catch (OperationCanceledException) when (_discarded || _siblingCts.IsCancellationRequested) {
            } catch (Exception ex) {
                _logger?.LogError(ex, "[StreamingToolExecutor] 后台队列任务异常");
            }
        }, "streaming-tool-exec");
    }

    private static bool IsShellTool(string toolName) {
        return string.Equals(toolName, ShellToolNameEnumConstants.Bash, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ShellToolNameEnumConstants.Powershell, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ShellToolNameEnumConstants.PowershellScript, StringComparison.OrdinalIgnoreCase);
    }

    private enum ToolStatus {
        Queued,
        Executing,
        Completed
    }

    /// <summary>执行器不可变状态快照 — 所有字段一并 CAS 发布，读侧零同步</summary>
    private sealed record ExecutorState(
        ImmutableList<QueuedTool> Queue,
        ImmutableList<StreamingToolResult> Completed,
        int ExecutingCount,
        int NonSafeExecutingCount) {
        public static readonly ExecutorState Empty = new([], [], 0, 0);
    }

    private sealed record QueuedTool {
        /// <summary>获取工具调用条目</summary>
        public required ToolCallEntry Entry { get; init; }
        /// <summary>获取原始顺序索引</summary>
        public required int OriginalIndex { get; init; }
        /// <summary>获取是否并发安全</summary>
        public bool IsConcurrencySafe { get; init; }
        /// <summary>获取是否已确定并发安全性</summary>
        public bool IsConcurrencySafeDetermined { get; init; }
        /// <summary>获取工具执行状态</summary>
        public required ToolStatus Status { get; init; }
        /// <summary>获取任务完成源</summary>
        public required TaskCompletionSource<StreamingToolResult> CompletionSource { get; init; }
        /// <summary>获取解析后的参数字典</summary>
        public Dictionary<string, JsonElement> ParsedArguments { get; init; } = [];
    }
}
