namespace Infrastructure.Utils.Resilience;

/// <summary>
/// 故障围栏 — 包裹执行动作捕获异常，生成 CrashSnapshot 入库；按分类器决定是否中断执行
/// </summary>
public sealed class FaultFence
{
    private readonly string _name;
    private readonly CrashSeverity _defaultSeverity;
    private readonly ICrashSnapshotStore? _store;
    private readonly Func<Exception, CrashSeverity>? _severityClassifier;
    private readonly Func<Exception, bool>? _shouldInterrupt;
    private int _totalExecutions;
    private int _totalFailures;
    private int _totalInterrupts;

    /// <summary>
    /// 围栏名称
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// 总执行次数
    /// </summary>
    public int TotalExecutions => _totalExecutions;

    /// <summary>
    /// 总失败次数
    /// </summary>
    public int TotalFailures => _totalFailures;

    /// <summary>
    /// 总中断次数
    /// </summary>
    public int TotalInterrupts => _totalInterrupts;

    /// <summary>
    /// 构造故障围栏
    /// </summary>
    /// <param name="name">围栏名称</param>
    /// <param name="defaultSeverity">默认崩溃严重级别</param>
    /// <param name="store">崩溃快照存储（可选）</param>
    /// <param name="severityClassifier">严重级别分类器（可选）</param>
    /// <param name="shouldInterrupt">是否中断执行的判定器（可选）</param>
    public FaultFence(
        string name,
        CrashSeverity defaultSeverity = CrashSeverity.Error,
        ICrashSnapshotStore? store = null,
        Func<Exception, CrashSeverity>? severityClassifier = null,
        Func<Exception, bool>? shouldInterrupt = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _name = name;
        _defaultSeverity = defaultSeverity;
        _store = store;
        _severityClassifier = severityClassifier;
        _shouldInterrupt = shouldInterrupt;
    }

    /// <summary>
    /// 异步执行带返回值的动作，异常时捕获快照并按策略决定是否中断
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="action">要执行的异步动作</param>
    /// <param name="context">崩溃执行上下文（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>动作返回值</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        CrashExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalExecutions);
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailures);
            var snapshot = CaptureSnapshot(ex, context);

            if (ShouldInterrupt(ex))
            {
                Interlocked.Increment(ref _totalInterrupts);
                Diag.WriteError($"[FaultFence:{_name}] 中断执行: {snapshot.ToSummary()}", ex);
                throw;
            }

            Diag.WriteError($"[FaultFence:{_name}] 围栏捕获: {snapshot.ToSummary()}", ex);
            throw;
        }
    }

    /// <summary>
    /// 异步执行无返回值的动作，异常时捕获快照并按策略决定是否中断
    /// </summary>
    /// <param name="action">要执行的异步动作</param>
    /// <param name="context">崩溃执行上下文（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ExecuteAsync(
        Func<Task> action,
        CrashExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 同步执行带返回值的动作，异常时捕获快照并按策略决定是否中断
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="action">要执行的动作</param>
    /// <param name="context">崩溃执行上下文（可选）</param>
    /// <returns>动作返回值</returns>
    public T Execute<T>(
        Func<T> action,
        CrashExecutionContext? context = null)
    {
        Interlocked.Increment(ref _totalExecutions);
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailures);
            var snapshot = CaptureSnapshot(ex, context);

            if (ShouldInterrupt(ex))
            {
                Interlocked.Increment(ref _totalInterrupts);
                Diag.WriteError($"[FaultFence:{_name}] 中断执行: {snapshot.ToSummary()}", ex);
                throw;
            }

            Diag.WriteError($"[FaultFence:{_name}] 围栏捕获: {snapshot.ToSummary()}", ex);
            throw;
        }
    }

    /// <summary>
    /// 尝试同步执行带返回值的动作，异常时不抛出而返回 FaultFenceResult
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="action">要执行的动作</param>
    /// <param name="context">崩溃执行上下文（可选）</param>
    /// <returns>包含成功值或失败快照的结果</returns>
    public FaultFenceResult<T> TryExecute<T>(
        Func<T> action,
        CrashExecutionContext? context = null)
    {
        Interlocked.Increment(ref _totalExecutions);
        try
        {
            var result = action();
            return FaultFenceResult<T>.Success(result);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailures);
            var snapshot = CaptureSnapshot(ex, context);

            if (ShouldInterrupt(ex))
            {
                Interlocked.Increment(ref _totalInterrupts);
                Diag.WriteError($"[FaultFence:{_name}] 中断执行: {snapshot.ToSummary()}", ex);
                return FaultFenceResult<T>.Failed(snapshot, interrupt: true);
            }

            return FaultFenceResult<T>.Failed(snapshot, interrupt: false);
        }
    }

    /// <summary>
    /// 尝试异步执行带返回值的动作，异常时不抛出而返回 FaultFenceResult
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="action">要执行的异步动作</param>
    /// <param name="context">崩溃执行上下文（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含成功值或失败快照的结果</returns>
    public async Task<FaultFenceResult<T>> TryExecuteAsync<T>(
        Func<Task<T>> action,
        CrashExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalExecutions);
        try
        {
            var result = await action().ConfigureAwait(false);
            return FaultFenceResult<T>.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailures);
            var snapshot = CaptureSnapshot(ex, context);

            if (ShouldInterrupt(ex))
            {
                Interlocked.Increment(ref _totalInterrupts);
                Diag.WriteError($"[FaultFence:{_name}] 中断执行: {snapshot.ToSummary()}", ex);
                return FaultFenceResult<T>.Failed(snapshot, interrupt: true);
            }

            return FaultFenceResult<T>.Failed(snapshot, interrupt: false);
        }
    }

    /// <summary>
    /// 捕获异常生成 CrashSnapshot，并写入存储（若提供）
    /// </summary>
    /// <param name="ex">异常</param>
    /// <param name="context">崩溃执行上下文（可选）</param>
    /// <returns>生成的崩溃快照</returns>
    public CrashSnapshot CaptureSnapshot(Exception ex, CrashExecutionContext? context = null)
    {
        var severity = _severityClassifier?.Invoke(ex) ?? _defaultSeverity;
        var snapshot = new CrashSnapshot(_name, severity, ex, context);
        _store?.Add(snapshot);
        return snapshot;
    }

    private bool ShouldInterrupt(Exception ex)
    {
        if (_shouldInterrupt is not null)
            return _shouldInterrupt(ex);

        return ex is OutOfMemoryException or TypeInitializationException or StackOverflowException;
    }
}

/// <summary>
/// 故障围栏执行结果 — 包含成功值或失败快照与中断标志
/// </summary>
/// <typeparam name="T">成功值类型</typeparam>
public sealed class FaultFenceResult<T>
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 成功时的值；失败时为 default
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// 失败时的崩溃快照；成功时为 null
    /// </summary>
    public CrashSnapshot? Snapshot { get; }

    /// <summary>
    /// 是否应中断执行
    /// </summary>
    public bool ShouldInterrupt { get; }

    private FaultFenceResult(bool isSuccess, T? value, CrashSnapshot? snapshot, bool shouldInterrupt)
    {
        IsSuccess = isSuccess;
        Value = value;
        Snapshot = snapshot;
        ShouldInterrupt = shouldInterrupt;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    /// <param name="value">成功值</param>
    /// <returns>成功结果实例</returns>
    public static FaultFenceResult<T> Success(T value) => new(true, value, null, false);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    /// <param name="snapshot">崩溃快照</param>
    /// <param name="interrupt">是否应中断</param>
    /// <returns>失败结果实例</returns>
    public static FaultFenceResult<T> Failed(CrashSnapshot snapshot, bool interrupt) => new(false, default, snapshot, interrupt);
}
