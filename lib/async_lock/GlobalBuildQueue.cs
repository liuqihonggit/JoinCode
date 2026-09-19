namespace Core.Utils;

/// <summary>编译请求 — 全局编译队列的工作单元。</summary>
public sealed record GlobalBuildRequest
{
    /// <summary>请求唯一标识。</summary>
    public required string RequestId { get; init; }

    /// <summary>项目路径（csproj/sln/slnx）。</summary>
    public required string ProjectPath { get; init; }

    /// <summary>编译参数（如 --no-incremental、-c Release）。</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>请求进程标识。</summary>
    public required string RequestingProcessId { get; init; }

    /// <summary>请求时间戳。</summary>
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>编译结果。</summary>
public sealed record GlobalBuildResult
{
    /// <summary>对应请求标识。</summary>
    public required string RequestId { get; init; }

    /// <summary>是否成功。</summary>
    public required bool Success { get; init; }

    /// <summary>编译输出（stdout+stderr）。</summary>
    public required string Output { get; init; }

    /// <summary>编译耗时。</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>完成时间戳。</summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>编译命令 — 全局编译队列的命令类型。</summary>
public abstract record GlobalBuildCommand;

/// <summary>入队编译请求。</summary>
public sealed record EnqueueGlobalBuildCmd(GlobalBuildRequest Request, TaskCompletionSource<GlobalBuildResult>? Tcs = null) : GlobalBuildCommand;

/// <summary>取消编译请求。</summary>
public sealed record CancelGlobalBuildCmd(string RequestId) : GlobalBuildCommand;

/// <summary>编译事件 — 全局编译队列的输出事件。</summary>
public abstract record GlobalBuildEvent;

/// <summary>编译开始事件。</summary>
public sealed record GlobalBuildStartedEvt(string RequestId, string ProjectPath) : GlobalBuildEvent;

/// <summary>编译完成事件。</summary>
public sealed record GlobalBuildCompletedEvt(GlobalBuildResult Result) : GlobalBuildEvent;

/// <summary>
/// 编译执行器委托 — 主机模式下调用以执行实际编译。
/// <para>agent 层注入具体实现（如 <c>dotnet build</c> 进程调用）。</para>
/// </summary>
/// <param name="request">编译请求</param>
/// <param name="ct">取消令牌</param>
/// <returns>编译结果</returns>
public delegate ValueTask<GlobalBuildResult> GlobalBuildExecutor(GlobalBuildRequest request, CancellationToken ct);

/// <summary>
/// 全局编译队列 — 跨进程串行编译，防止多进程并发编译导致 OOM。
/// <para>继承 <see cref="ActorBase{TCommand, TOut}"/>，Consumer 线程串行执行编译，同一时刻只有一个编译在跑。</para>
/// <para>背压：<see cref="ActorBackpressure.Build"/>（容量 100 + Wait + 60s 超时），防止编译请求堆积。</para>
/// <para>主机模式：本地执行编译，通过 <see cref="ITransportTopology"/> 接收从机请求并返回结果。</para>
/// <para>从机模式：编译请求转发到主机，等待主机执行结果（TODO: 跨进程请求-响应）。</para>
/// <para>防 OOM：全局串行保证内存峰值只有一个编译的量，多进程叠加不会 OOM。</para>
/// </summary>
public sealed class GlobalBuildQueue : ActorBase<GlobalBuildCommand, GlobalBuildEvent>
{
    private readonly GlobalBuildExecutor _executor;
    private readonly ITransportTopology? _transport;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<GlobalBuildResult>> _pending;
    private GlobalBuildRequest? _currentBuild;

    /// <summary>
    /// 构造全局编译队列。
    /// </summary>
    /// <param name="executor">编译执行器（agent 层注入 <c>dotnet build</c> 调用）</param>
    /// <param name="transport">可选传输层（主机接收从机请求，从机转发请求到主机）</param>
    /// <param name="logger">日志记录器</param>
    public GlobalBuildQueue(
        GlobalBuildExecutor executor,
        ITransportTopology? transport = null,
        ILogger? logger = null)
        : base(ActorBackpressure.Build, outputCapacity: 64)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _transport = transport;
        _logger = logger;
        _pending = new ConcurrentDictionary<string, TaskCompletionSource<GlobalBuildResult>>();
    }

    /// <summary>当前正在执行的编译请求（null 表示空闲）。</summary>
    public GlobalBuildRequest? CurrentBuild => Volatile.Read(ref _currentBuild);

    /// <summary>等待中的编译请求数。</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// 异步提交编译请求 — tell 异步，入队后立即返回。
    /// <para>调用方可通过返回的 Task 等待编译结果（可选）。</para>
    /// <para>队列满时背压等待（容量 100 + 60s 超时）。</para>
    /// </summary>
    /// <param name="request">编译请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>编译结果 Task（await 等待结果）</returns>
    public async Task<GlobalBuildResult> EnqueueAsync(GlobalBuildRequest request, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<GlobalBuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.RequestId] = tcs;

        await SendAsync(new EnqueueGlobalBuildCmd(request, tcs), ct).ConfigureAwait(false);

        return await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 取消编译请求。
    /// </summary>
    /// <param name="requestId">请求标识</param>
    /// <param name="ct">取消令牌</param>
    public async ValueTask CancelAsync(string requestId, CancellationToken ct = default)
        => await SendAsync(new CancelGlobalBuildCmd(requestId), ct).ConfigureAwait(false);

    /// <summary>
    /// 命令处理 — Consumer 线程串行执行，保证同一时刻只有一个编译在跑。
    /// </summary>
    protected override async ValueTask HandleAsync(GlobalBuildCommand cmd, CancellationToken ct)
    {
        switch (cmd)
        {
            case EnqueueGlobalBuildCmd enqueue:
                await HandleEnqueueAsync(enqueue, ct).ConfigureAwait(false);
                break;
            case CancelGlobalBuildCmd cancel:
                HandleCancel(cancel.RequestId);
                break;
            default:
                throw new InvalidOperationException($"Unknown build command: {cmd?.GetType().Name}");
        }
    }

    private async ValueTask HandleEnqueueAsync(EnqueueGlobalBuildCmd cmd, CancellationToken ct)
    {
        var request = cmd.Request;
        Volatile.Write(ref _currentBuild, request);
        TryPublish(new GlobalBuildStartedEvt(request.RequestId, request.ProjectPath));

        _logger?.LogInformation(
            "GlobalBuildQueue: build started (id={Id}, project={Project}, from={Process})",
            request.RequestId, request.ProjectPath, request.RequestingProcessId);

        try
        {
            var result = await _executor(request, ct).ConfigureAwait(false);
            TryPublish(new GlobalBuildCompletedEvt(result));

            if (cmd.Tcs is not null)
            {
                cmd.Tcs.TrySetResult(result);
            }
            _pending.TryRemove(request.RequestId, out _);

            _logger?.LogInformation(
                "GlobalBuildQueue: build completed (id={Id}, success={Success}, duration={Duration:F1}s)",
                request.RequestId, result.Success, result.Duration.TotalSeconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            cmd.Tcs?.TrySetCanceled(ct);
            _pending.TryRemove(request.RequestId, out _);
        }
        catch (Exception ex)
        {
            var failedResult = new GlobalBuildResult
            {
                RequestId = request.RequestId,
                Success = false,
                Output = ex.ToString(),
                Duration = TimeSpan.Zero
            };
            TryPublish(new GlobalBuildCompletedEvt(failedResult));

            cmd.Tcs?.TrySetResult(failedResult);
            _pending.TryRemove(request.RequestId, out _);

            _logger?.LogError(ex, "GlobalBuildQueue: build failed (id={Id})", request.RequestId);
        }
        finally
        {
            Volatile.Write(ref _currentBuild, null);
        }
    }

    private void HandleCancel(string requestId)
    {
        if (_pending.TryRemove(requestId, out var tcs))
        {
            tcs.TrySetCanceled();
            _logger?.LogInformation("GlobalBuildQueue: build cancelled (id={Id})", requestId);
        }
    }

    /// <summary>
    /// 获取当前队列状态快照 — 用于主机上下文同步。
    /// </summary>
    public BuildQueueState GetQueueState()
    {
        var current = Volatile.Read(ref _currentBuild);
        return new BuildQueueState
        {
            PendingCount = _pending.Count,
            RunningCount = current is not null ? 1 : 0,
            PendingTasks = _pending.Keys.ToArray()
        };
    }

    /// <summary>
    /// 释放全局编译队列 — 释放传输层。
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (_transport is not null) await _transport.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
