namespace Core.Context;

/// <summary>
/// 流式工具执行器中单个工具的执行结果
/// </summary>
public sealed class StreamingToolResult
{
    public required string ToolName { get; init; }
    public required string? ToolCallId { get; init; }
    public required ToolCallResult Result { get; init; }
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
public sealed class StreamingToolExecutor : IAsyncDisposable
{
    private readonly IToolExecutionHandler _toolHandler;
    private readonly IToolConcurrencyClassifier _concurrencyClassifier;
    private readonly ChatMiddlewareContext _context;
    private readonly int _maxConcurrency;
    private readonly ILogger? _logger;

    private readonly List<QueuedTool> _queue = [];
    private readonly List<StreamingToolResult> _completedBuffer = [];
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _executingCount;
    private bool _hasNonSafeExecuting;
    private readonly CancellationTokenSource _siblingCts = new();
    private volatile bool _discarded;

    /// <summary>
    /// 初始化流式工具执行器
    /// </summary>
    public StreamingToolExecutor(
        IToolExecutionHandler toolHandler,
        IToolConcurrencyClassifier concurrencyClassifier,
        ChatMiddlewareContext context,
        int maxConcurrency = 10,
        ILogger? logger = null)
    {
        _toolHandler = toolHandler;
        _concurrencyClassifier = concurrencyClassifier;
        _context = context;
        _maxConcurrency = maxConcurrency;
        _logger = logger;
    }

    /// <summary>
    /// 添加工具调用到队列 — 对齐 TS StreamingToolExecutor.addTool()
    /// 流式过程中每收到一个 tool_use block 就调用此方法
    /// </summary>
    public void AddTool(ToolCallEntry entry, int originalIndex)
    {
        if (_discarded) return;

        _semaphore.Wait();
        try
        {
            _queue.Add(new QueuedTool
            {
                Entry = entry,
                OriginalIndex = originalIndex,
                IsConcurrencySafe = false,
                Status = ToolStatus.Queued,
                CompletionSource = new TaskCompletionSource<StreamingToolResult>()
            });
        }
        finally
        {
            _semaphore.Release();
        }

        _ = ProcessQueueAsync();
    }

    /// <summary>
    /// 获取已完成的结果（按原始顺序） — 对齐 TS StreamingToolExecutor.getCompletedResults()
    /// </summary>
    public IReadOnlyList<StreamingToolResult> GetCompletedResults()
    {
        if (_discarded) return [];

        _semaphore.Wait();
        try
        {
            var results = _completedBuffer
                .OrderBy(r => r.OriginalIndex)
                .ToList();
            _completedBuffer.Clear();
            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 等待所有剩余工具完成并返回结果 — 对齐 TS StreamingToolExecutor.getRemainingResults()
    /// </summary>
#pragma warning disable VSTHRD003 // TaskCompletionSource 任务由各 ExecuteToolAsync 启动，此处仅等待完成
    public async Task<IReadOnlyList<StreamingToolResult>> GetRemainingResultsAsync()
    {
        if (_discarded) return [];

        List<Task<StreamingToolResult>> pendingTasks;
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            pendingTasks = _queue
                .Where(t => t.Status != ToolStatus.Completed)
                .Select(t => t.CompletionSource.Task)
                .ToList();
        }
        finally
        {
            _semaphore.Release();
        }

        if (pendingTasks.Count > 0)
        {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }

        return GetCompletedResults();
    }
#pragma warning restore VSTHRD003

    /// <summary>
    /// 获取取消令牌 — Bash 错误级联取消兄弟工具时使用
    /// </summary>
    public CancellationToken SiblingCancellationToken => _siblingCts.Token;

    /// <summary>
    /// 是否已被丢弃 — 对齐 TS StreamingToolExecutor.discarded
    /// </summary>
    public bool IsDiscarded => _discarded;

    /// <summary>
    /// 丢弃所有待处理和进行中的工具 —C 对齐 TS StreamingToolExecutor.discard()
    /// 在流式 fallback 发生且失败尝试的结果应被放弃时调用
    /// 排队工具不会启动，进行中工具将收到合成错误
    /// </summary>
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
            _logger?.LogDebug("[StreamingToolExecutor] SiblingCts already disposed during discard");
        }

        _semaphore.Wait();
        try
        {
            foreach (var tool in _queue)
            {
                if (tool.Status != ToolStatus.Completed)
                {
                    tool.Status = ToolStatus.Completed;
                    if (!tool.CompletionSource.Task.IsCompleted)
                    {
                        tool.CompletionSource.SetResult(new StreamingToolResult
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
            }

            _completedBuffer.Clear();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _siblingCts.Cancel();
        _siblingCts.Dispose();
        _semaphore.Dispose();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            QueuedTool? toolToExecute;
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                toolToExecute = await FindNextExecutableAsync().ConfigureAwait(false);
                if (toolToExecute is null)
                    break;

                toolToExecute.Status = ToolStatus.Executing;
                _executingCount++;
                if (!toolToExecute.IsConcurrencySafe)
                    _hasNonSafeExecuting = true;
            }
            finally
            {
                _semaphore.Release();
            }

            _ = ExecuteToolAsync(toolToExecute);
        }
    }

    private async Task<QueuedTool?> FindNextExecutableAsync()
    {
        foreach (var tool in _queue)
        {
            if (tool.Status != ToolStatus.Queued)
                continue;

            if (!tool.IsConcurrencySafeDetermined)
            {
                tool.IsConcurrencySafe = await _concurrencyClassifier
                    .IsConcurrencySafeAsync(tool.Entry.Name, JsonArgumentParser.Parse(tool.Entry.Arguments), SiblingCancellationToken)
                    .ConfigureAwait(false);
                tool.IsConcurrencySafeDetermined = true;
            }

            if (CanExecute(tool.IsConcurrencySafe))
                return tool;

            break;
        }

        return null;
    }

    /// <summary>
    /// 判断是否可以执行 — 对齐 TS StreamingToolExecutor.canExecuteTool()
    /// 规则：无工具执行→可启动；新工具安全且当前全安全→可并发；否则等待
    /// </summary>
    private bool CanExecute(bool isConcurrencySafe)
    {
        if (_executingCount == 0)
            return true;

        if (_executingCount >= _maxConcurrency)
            return false;

        if (isConcurrencySafe && !_hasNonSafeExecuting)
            return true;

        return false;
    }

    private async Task ExecuteToolAsync(QueuedTool tool)
    {
        StreamingToolResult result;
        try
        {
            var args = JsonArgumentParser.Parse(tool.Entry.Arguments);
            var toolCallResult = await _toolHandler.ExecuteToolCallAsync(
                tool.Entry.Name, tool.Entry.Id, args, _context, SiblingCancellationToken).ConfigureAwait(false);

            result = new StreamingToolResult
            {
                ToolName = tool.Entry.Name,
                ToolCallId = tool.Entry.Id,
                Result = toolCallResult,
                OriginalIndex = tool.OriginalIndex
            };

            if (toolCallResult.IsError && IsBashTool(tool.Entry.Name))
            {
                _logger?.LogWarning("[StreamingToolExecutor] Bash 工具错误，级联取消兄弟工具: {ToolName}", tool.Entry.Name);
                try { _siblingCts.Cancel(); }
                catch (ObjectDisposedException) { _logger?.LogDebug("[StreamingToolExecutor] SiblingCts 已释放"); }
            }
        }
        catch (OperationCanceledException) when (SiblingCancellationToken.IsCancellationRequested)
        {
            result = new StreamingToolResult
            {
                ToolName = tool.Entry.Name,
                ToolCallId = tool.Entry.Id,
                Result = new ToolCallResult { ResultText = "(cancelled by sibling error)", IsError = true },
                OriginalIndex = tool.OriginalIndex
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[StreamingToolExecutor] 工具执行失败: {ToolName}", tool.Entry.Name);
            result = new StreamingToolResult
            {
                ToolName = tool.Entry.Name,
                ToolCallId = tool.Entry.Id,
                Result = new ToolCallResult { ResultText = $"工具执行失败: {ex.Message}", IsError = true },
                OriginalIndex = tool.OriginalIndex
            };
        }

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            tool.Status = ToolStatus.Completed;
            tool.CompletionSource.SetResult(result);
            _completedBuffer.Add(result);
            _executingCount--;
            if (!tool.IsConcurrencySafe)
            {
                _hasNonSafeExecuting = _queue.Any(t => t.Status == ToolStatus.Executing && !t.IsConcurrencySafe);
            }
        }
        finally
        {
            _semaphore.Release();
        }

        _ = ProcessQueueAsync();
    }

    private static bool IsBashTool(string toolName)
    {
        return string.Equals(toolName, ShellToolNameConstants.Bash, StringComparison.OrdinalIgnoreCase);
    }

    private enum ToolStatus
    {
        Queued,
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
    }
}
