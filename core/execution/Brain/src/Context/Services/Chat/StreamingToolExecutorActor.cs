namespace Core.Context;

/// <summary>
/// 流式工具执行器接口 — StreamingToolExecutor 与 StreamingToolExecutorActor 共同实现。
/// 用于特性开关在锁版与 Actor 版之间切换。
/// </summary>
public interface IStreamingToolExecutor : IAsyncDisposable
{
    /// <summary>添加工具调用到队列</summary>
    Task AddToolAsync(ToolCallEntry entry, int originalIndex);

    /// <summary>获取已完成的结果(按原始顺序)</summary>
    Task<IReadOnlyList<StreamingToolResult>> GetCompletedResultsAsync();

    /// <summary>等待所有剩余工具完成并返回结果</summary>
    Task<IReadOnlyList<StreamingToolResult>> GetRemainingResultsAsync();

    /// <summary>丢弃所有待处理和进行中的工具</summary>
    void Discard();

    /// <summary>取消令牌 — Bash 错误级联取消 + 用户取消的组合令牌</summary>
    CancellationToken CombinedCancellationToken { get; }

    /// <summary>是否已被丢弃</summary>
    bool IsDiscarded { get; }
}

/// <summary>
/// 流式工具执行器 Actor 版 — 单消费者 Channel + 命令模式,零锁。
/// <para>所有可变状态(_queue/_completedBuffer/_executingCount/_nonSafeExecutingCount)由 Consumer 线程独占访问。</para>
/// <para>工具执行(慢操作)分发到 Task.Run 并发执行,完成后发 ToolCompletedCommand 回 Consumer 更新状态。</para>
/// <para>对齐 TS StreamingToolExecutor,行为与 StreamingToolExecutor 等价。</para>
/// </summary>
public sealed class StreamingToolExecutorActor : ActorBase<StreamingToolExecutorActor.IToolCommand>, IStreamingToolExecutor
{
    private readonly IToolExecutionHandler _toolHandler;
    private readonly IToolConcurrencyClassifier _concurrencyClassifier;
    private readonly ChatMiddlewareContext _context;
    private readonly int _maxConcurrency;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _siblingCts = new();
    private readonly CancellationTokenSource? _linkedCts;
    private readonly CancellationToken _combinedCt;
    private volatile bool _discarded;

    private readonly List<QueuedTool> _queue = [];
    private readonly List<StreamingToolResult> _completedBuffer = [];
    private int _executingCount;
    private int _nonSafeExecutingCount;

    /// <summary>命令标记接口 — Consumer 串行处理(public 因泛型约束,具体命令类型为 private)</summary>
    public interface IToolCommand;

    private sealed record AddToolCommand(ToolCallEntry Entry, int OriginalIndex) : IToolCommand;
    private sealed record DiscardCmd : IToolCommand;
    private sealed record ToolCompletedCommand(QueuedTool Tool, StreamingToolResult Result, bool IsConcurrencySafe) : IToolCommand;
    private sealed record GetCompletedQuery(TaskCompletionSource<IReadOnlyList<StreamingToolResult>> Tcs) : IToolCommand;
    private sealed record GetRemainingQuery(TaskCompletionSource<List<Task<StreamingToolResult>>> Tcs) : IToolCommand;

    /// <summary>初始化流式工具执行器 Actor</summary>
    public StreamingToolExecutorActor(
        IToolExecutionHandler toolHandler,
        IToolConcurrencyClassifier concurrencyClassifier,
        ChatMiddlewareContext context,
        int maxConcurrency = 10,
        ILogger? logger = null,
        CancellationToken userCancellationToken = default)
        : base(boundedCapacity: null)
    {
        _toolHandler = toolHandler;
        _concurrencyClassifier = concurrencyClassifier;
        _context = context;
        _maxConcurrency = maxConcurrency;
        _logger = logger;

        if (userCancellationToken.CanBeCanceled)
        {
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(userCancellationToken, _siblingCts.Token);
            _combinedCt = _linkedCts.Token;
        }
        else
        {
            _combinedCt = _siblingCts.Token;
        }
    }

    /// <inheritdoc/>
    public CancellationToken CombinedCancellationToken => _combinedCt;

    /// <inheritdoc/>
    public bool IsDiscarded => _discarded;

    /// <inheritdoc/>
    public Task AddToolAsync(ToolCallEntry entry, int originalIndex)
    {
        if (_discarded) return Task.CompletedTask;
        return SendAsync(new AddToolCommand(entry, originalIndex)).AsTask();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StreamingToolResult>> GetCompletedResultsAsync()
    {
        if (_discarded) return [];
        var tcs = new TaskCompletionSource<IReadOnlyList<StreamingToolResult>>();
        await SendAsync(new GetCompletedQuery(tcs)).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
#pragma warning disable VSTHRD003 // TCS 任务由 Consumer 线程设置结果,await 不会死锁
    public async Task<IReadOnlyList<StreamingToolResult>> GetRemainingResultsAsync()
    {
        if (_discarded) return [];

        var remainingTcs = new TaskCompletionSource<List<Task<StreamingToolResult>>>();
        await SendAsync(new GetRemainingQuery(remainingTcs)).ConfigureAwait(false);
        var pendingTasks = await remainingTcs.Task.ConfigureAwait(false);

        if (pendingTasks.Count > 0)
        {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }

        return await GetCompletedResultsAsync().ConfigureAwait(false);
    }
#pragma warning restore VSTHRD003

    /// <inheritdoc/>
    public void Discard()
    {
        _discarded = true;
        try
        {
            if (!_siblingCts.IsCancellationRequested)
                _siblingCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            _logger?.LogDebug("[StreamingToolExecutorActor] SiblingCts already disposed during discard");
        }
        TrySend(new DiscardCmd());
    }

    /// <inheritdoc/>
    public new async ValueTask DisposeAsync()
    {
        _siblingCts.Cancel();
        _linkedCts?.Dispose();
        _siblingCts.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Consumer 命令处理 — 串行访问所有可变状态,无锁</summary>
    protected override ValueTask HandleAsync(IToolCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case AddToolCommand(var entry, var idx):
                HandleAddTool(entry, idx);
                return ValueTask.CompletedTask;
            case DiscardCmd:
                HandleDiscard();
                return ValueTask.CompletedTask;
            case ToolCompletedCommand(var tool, var result, var isSafe):
                HandleToolCompleted(tool, result, isSafe);
                return ValueTask.CompletedTask;
            case GetCompletedQuery(var tcs):
                HandleGetCompleted(tcs);
                return ValueTask.CompletedTask;
            case GetRemainingQuery(var tcs):
                HandleGetRemaining(tcs);
                return ValueTask.CompletedTask;
            case SafetyDeterminedCommand(var tool):
                return HandleSafetyDetermined(tool);
            default:
                return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc/>
    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[StreamingToolExecutorActor] Consumer 命令处理异常");
    }

    private void HandleAddTool(ToolCallEntry entry, int originalIndex)
    {
        _queue.Add(new QueuedTool
        {
            Entry = entry,
            OriginalIndex = originalIndex,
            IsConcurrencySafe = false,
            Status = ToolStatus.Queued,
            CompletionSource = new TaskCompletionSource<StreamingToolResult>()
        });
        ScheduleNext();
    }

    private void HandleDiscard()
    {
        var uncompletedTools = _queue
            .Where(t => t.Status != ToolStatus.Completed && !t.CompletionSource.Task.IsCompleted)
            .ToList();

        foreach (var tool in _queue)
        {
            if (tool.Status != ToolStatus.Completed)
                tool.Status = ToolStatus.Completed;
        }

        _completedBuffer.Clear();

        foreach (var tool in uncompletedTools)
        {
            tool.CompletionSource.TrySetResult(new StreamingToolResult
            {
                ToolName = tool.Entry.Name,
                ToolCallId = tool.Entry.Id,
                Result = new ToolCallResult
                {
                    ResultText = "(discarded by streaming fallback)",
                    IsError = true
                },
                OriginalIndex = tool.OriginalIndex
            });
        }
    }

    private void HandleToolCompleted(QueuedTool tool, StreamingToolResult result, bool isConcurrencySafe)
    {
        tool.Status = ToolStatus.Completed;
        _completedBuffer.Add(result);
        _executingCount--;
        if (!isConcurrencySafe)
            _nonSafeExecutingCount--;
        ScheduleNext();
    }

    private void HandleGetCompleted(TaskCompletionSource<IReadOnlyList<StreamingToolResult>> tcs)
    {
        var results = _completedBuffer.OrderBy(r => r.OriginalIndex).ToList();
        _completedBuffer.Clear();
        tcs.SetResult(results);
    }

#pragma warning disable VSTHRD003 // TCS.Task 由 ExecuteToolAsync 设置结果,收集后由调用方 await,不死锁
    private void HandleGetRemaining(TaskCompletionSource<List<Task<StreamingToolResult>>> tcs)
    {
        var pending = _queue
            .Where(t => t.Status != ToolStatus.Completed)
            .Select(t => t.CompletionSource.Task)
            .ToList();
        tcs.SetResult(pending);
    }
#pragma warning restore VSTHRD003

    /// <summary>调度下一个可执行工具 — Consumer 线程内调用,无锁</summary>
    private void ScheduleNext()
    {
        var hasDetermining = false;
        foreach (var tool in _queue)
        {
            if (tool.Status == ToolStatus.Determining)
            {
                hasDetermining = true;
                break;
            }
        }

        foreach (var tool in _queue)
        {
            if (tool.Status != ToolStatus.Queued)
                continue;

            if (!tool.IsConcurrencySafeDetermined)
            {
                if (hasDetermining) continue;
                tool.Status = ToolStatus.Determining;
                hasDetermining = true;
                _ = Task.Run(() => DetermineSafetyAndScheduleAsync(tool));
                continue;
            }

            if (CanExecute(tool.IsConcurrencySafe))
            {
                tool.Status = ToolStatus.Executing;
                _executingCount++;
                if (!tool.IsConcurrencySafe)
                    _nonSafeExecutingCount++;
                _ = Task.Run(() => ExecuteToolAsync(tool));
            }
        }
    }

    /// <summary>异步确定工具并发安全性后继续调度</summary>
    private async Task DetermineSafetyAndScheduleAsync(QueuedTool tool)
    {
        try
        {
            if (tool.ParsedArguments.Count == 0)
                tool.ParsedArguments = JsonArgumentParser.Parse(tool.Entry.Arguments);
            tool.IsConcurrencySafe = await _concurrencyClassifier
                .IsConcurrencySafeAsync(tool.Entry.Name, tool.ParsedArguments, CancellationToken.None)
                .ConfigureAwait(false);
            tool.IsConcurrencySafeDetermined = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[StreamingToolExecutorActor] 确定并发安全性失败: {ToolName}", tool.Entry.Name);
            tool.IsConcurrencySafe = false;
            tool.IsConcurrencySafeDetermined = true;
        }
        TrySend(new SafetyDeterminedCommand(tool));
    }

    private sealed record SafetyDeterminedCommand(QueuedTool Tool) : IToolCommand;

    /// <summary>安全性确定后继续调度</summary>
    private ValueTask HandleSafetyDetermined(QueuedTool tool)
    {
        if (CanExecute(tool.IsConcurrencySafe))
        {
            tool.Status = ToolStatus.Executing;
            _executingCount++;
            if (!tool.IsConcurrencySafe)
                _nonSafeExecutingCount++;
            _ = Task.Run(() => ExecuteToolAsync(tool));
        }
        else
        {
            tool.Status = ToolStatus.Queued;
        }
        ScheduleNext();
        return ValueTask.CompletedTask;
    }

    /// <summary>并发执行工具 — 完成后发 ToolCompletedCommand 回 Consumer</summary>
    private async Task ExecuteToolAsync(QueuedTool tool)
    {
        StreamingToolResult result;

        if (_siblingCts.IsCancellationRequested)
        {
            result = BuildCancelledResult(tool);
        }
        else
        {
            try
            {
                if (tool.ParsedArguments.Count == 0)
                    tool.ParsedArguments = JsonArgumentParser.Parse(tool.Entry.Arguments);
                var args = tool.ParsedArguments;
                var toolCallResult = await _toolHandler.ExecuteToolCallAsync(
                    tool.Entry.Name, tool.Entry.Id, args, _context, _combinedCt).ConfigureAwait(false);

                result = new StreamingToolResult
                {
                    ToolName = tool.Entry.Name,
                    ToolCallId = tool.Entry.Id,
                    Result = toolCallResult,
                    OriginalIndex = tool.OriginalIndex
                };

                if (toolCallResult.IsError && IsShellTool(tool.Entry.Name))
                {
                    _logger?.LogWarning("[StreamingToolExecutorActor] Shell 工具错误,级联取消兄弟工具: {ToolName}", tool.Entry.Name);
                    try { _siblingCts.Cancel(); }
                    catch (ObjectDisposedException) { _logger?.LogDebug("[StreamingToolExecutorActor] SiblingCts 已释放"); }
                }
            }
            catch (OperationCanceledException) when (_combinedCt.IsCancellationRequested)
            {
                result = BuildCancelledResult(tool);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[StreamingToolExecutorActor] 工具执行失败: {ToolName}", tool.Entry.Name);
                result = new StreamingToolResult
                {
                    ToolName = tool.Entry.Name,
                    ToolCallId = tool.Entry.Id,
                    Result = new ToolCallResult { ResultText = $"工具执行失败: {ex.Message}", IsError = true },
                    OriginalIndex = tool.OriginalIndex
                };
            }
        }

        TrySend(new ToolCompletedCommand(tool, result, tool.IsConcurrencySafe));
        tool.CompletionSource.TrySetResult(result);
    }

    private static StreamingToolResult BuildCancelledResult(QueuedTool tool) => new()
    {
        ToolName = tool.Entry.Name,
        ToolCallId = tool.Entry.Id,
        Result = new ToolCallResult { ResultText = "(cancelled by sibling error)", IsError = true },
        OriginalIndex = tool.OriginalIndex
    };

    /// <summary>判断是否可以执行 — 无工具执行→可启动;新工具安全且当前全安全→可并发;否则等待</summary>
    private bool CanExecute(bool isConcurrencySafe)
    {
        if (_executingCount == 0)
            return true;
        if (_executingCount >= _maxConcurrency)
            return false;
        if (isConcurrencySafe && _nonSafeExecutingCount == 0)
            return true;
        return false;
    }

    private static bool IsShellTool(string toolName)
    {
        return string.Equals(toolName, ShellToolNameConstants.Bash, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ShellToolNameConstants.Powershell, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ShellToolNameConstants.PowershellScript, StringComparison.OrdinalIgnoreCase);
    }

    private enum ToolStatus
    {
        Queued,
        Determining,
        Executing,
        Completed
    }

    private sealed class QueuedTool
    {
        public required ToolCallEntry Entry { get; init; }
        public required int OriginalIndex { get; init; }
        public bool IsConcurrencySafe { get; set; }
        public bool IsConcurrencySafeDetermined { get; set; }
        public required ToolStatus Status { get; set; }
        public required TaskCompletionSource<StreamingToolResult> CompletionSource { get; init; }
        public Dictionary<string, JsonElement> ParsedArguments { get; set; } = [];
    }
}
