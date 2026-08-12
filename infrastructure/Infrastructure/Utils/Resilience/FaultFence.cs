namespace Infrastructure.Utils.Resilience;

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

    public string Name => _name;
    public int TotalExecutions => _totalExecutions;
    public int TotalFailures => _totalFailures;
    public int TotalInterrupts => _totalInterrupts;

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

public sealed class FaultFenceResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public CrashSnapshot? Snapshot { get; }
    public bool ShouldInterrupt { get; }

    private FaultFenceResult(bool isSuccess, T? value, CrashSnapshot? snapshot, bool shouldInterrupt)
    {
        IsSuccess = isSuccess;
        Value = value;
        Snapshot = snapshot;
        ShouldInterrupt = shouldInterrupt;
    }

    public static FaultFenceResult<T> Success(T value) => new(true, value, null, false);
    public static FaultFenceResult<T> Failed(CrashSnapshot snapshot, bool interrupt) => new(false, default, snapshot, interrupt);
}
