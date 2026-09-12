namespace Services.Build;

[Register(typeof(IBuildQueueService), ServiceLifetime.Singleton)]
public sealed partial class BuildQueueService : IBuildQueueService
{
    private readonly ISystemActuatorRegistry _actuatorRegistry;
    private readonly IFileSystem _fs;
    private readonly IPreventSleepService? _preventSleepService;
    private readonly ILogger<BuildQueueService>? _logger;

    private readonly Channel<BuildQueueEntry> _queue = Channel.CreateUnbounded<BuildQueueEntry>();
    private readonly ConcurrentDictionary<string, BuildQueueEntry> _entries = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BuildQueueResult>> _waitHandles = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _processingTask;

    private readonly CrossProcessBuildLock _crossProcessLock;
    private readonly BuildResultBuffer _resultBuffer;

    private int _buildCounter;
    private BuildQueueEntry? _currentBuild;
    private CancellationTokenSource? _currentBuildCts;
    private int _disposed;

    public BuildQueueService(
        ISystemActuatorRegistry actuatorRegistry,
        IFileSystem fs,
        IPreventSleepService? preventSleepService = null,
        ILogger<BuildQueueService>? logger = null,
        string? crossProcessLockPath = null)
    {
        _actuatorRegistry = actuatorRegistry;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _preventSleepService = preventSleepService;
        _logger = logger;

        var fingerprintCache = new SourceFingerprintCache(logger);
        _resultBuffer = new BuildResultBuffer(fingerprintCache, logger);
        _crossProcessLock = new CrossProcessBuildLock(fs, logger, crossProcessLockPath);

        _processingTask = ProcessQueueAsync(_shutdownCts.Token);
    }

    public Task<string> SubmitAsync(BuildRequest request, CancellationToken ct)
    {
        var bufferKey = BuildResultBuffer.BuildBufferKey(request.Command, request.WorkingDirectory);

        if (_resultBuffer.TryGet(bufferKey, out var bufferedResult))
        {
            _logger?.LogInformation("Build result buffer hit: {BufferKey}", bufferKey);
            var buildId = CreateCompletedEntry(request, bufferedResult);
            return Task.FromResult(buildId);
        }

        var newEntry = new BuildQueueEntry
        {
            BuildId = $"b-{Interlocked.Increment(ref _buildCounter):D4}",
            Request = request,
            Status = BuildQueueEntryStatus.Queued,
            QueuePosition = _entries.Count
        };

        _entries[newEntry.BuildId] = newEntry;
        _waitHandles[newEntry.BuildId] = new TaskCompletionSource<BuildQueueResult>();
        _queue.Writer.TryWrite(newEntry);

        _logger?.LogInformation("Build submitted: {BuildId}, command: {Command}", newEntry.BuildId, request.Command);

        return Task.FromResult(newEntry.BuildId);
    }

    /// <inheritdoc />
#pragma warning disable VSTHRD003
    public Task<BuildQueueResult> WaitAsync(string buildId, CancellationToken ct)
    {
        if (!_waitHandles.TryGetValue(buildId, out var tcs))
            throw new InvalidOperationException($"Build {buildId} not found");

        return tcs.Task;
    }
#pragma warning restore VSTHRD003

    /// <inheritdoc />
    public Task<bool> CancelAsync(string buildId, CancellationToken ct)
    {
        if (!_entries.TryGetValue(buildId, out var entry))
            return Task.FromResult(false);

        switch (entry.Status)
        {
            case BuildQueueEntryStatus.Queued:
                entry.Status = BuildQueueEntryStatus.Cancelled;
                entry.CompletedAt = DateTimeOffset.UtcNow;
                CompleteWithCancellation(buildId, entry);
                _logger?.LogInformation("Build cancelled (was queued): {BuildId}", buildId);
                return Task.FromResult(true);

            case BuildQueueEntryStatus.Building:
                entry.Status = BuildQueueEntryStatus.Cancelling;
                _currentBuildCts?.Cancel();
                _logger?.LogInformation("Build cancelling (was building): {BuildId}", buildId);
                return Task.FromResult(true);

            default:
                return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public BuildQueueEntry? GetBuild(string buildId)
    {
        return _entries.TryGetValue(buildId, out var entry) ? entry : null;
    }

    /// <inheritdoc />
    public BuildQueueStatus GetStatus()
    {
        var pendingCount = _entries.Values.Count(e => e.Status == BuildQueueEntryStatus.Queued);
        var isBuilding = _currentBuild is not null && _currentBuild.Status == BuildQueueEntryStatus.Building;

        return new BuildQueueStatus
        {
            PendingCount = pendingCount,
            IsBuilding = isBuilding,
            CurrentBuildId = _currentBuild?.BuildId,
            CurrentBuildAgentId = _currentBuild?.Request.AgentId,
            RecentBuilds = _entries.Values
                .OrderByDescending(e => e.Request.SubmittedAt)
                .Take(10)
                .ToList()
        };
    }

    /// <inheritdoc />
    public Task ClearCacheAsync(CancellationToken ct)
    {
        _resultBuffer.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GetOutputRange(string buildId, int startLine, int endLine)
    {
        var entry = _entries.TryGetValue(buildId, out var e) ? e : null;
        if (entry?.Result is null)
            return $"Build {buildId} not found or has no result";

        var output = entry.Result.ExitCode == 0
            ? entry.Result.Output
            : $"{entry.Result.ErrorOutput}\n{entry.Result.Output}";

        var lines = output.Split('\n');
        if (endLine <= 0) endLine = lines.Length;

        startLine = Math.Max(1, startLine);
        endLine = Math.Min(lines.Length, endLine);

        if (startLine > endLine)
            return $"Invalid range: start={startLine}, end={endLine}, total={lines.Length}";

        var selected = lines[(startLine - 1)..endLine];
        return string.Join('\n', selected);
    }

    private string CreateCompletedEntry(BuildRequest request, BuildQueueResult result)
    {
        var buildId = $"b-{Interlocked.Increment(ref _buildCounter):D4}";
        var entry = new BuildQueueEntry
        {
            BuildId = buildId,
            Request = request,
            Status = BuildQueueEntryStatus.Completed,
            Result = result with { BuildId = buildId },
            CompletedAt = DateTimeOffset.UtcNow,
        };
        _entries[buildId] = entry;
        _waitHandles[buildId] = new TaskCompletionSource<BuildQueueResult>();
        _waitHandles[buildId].TrySetResult(entry.Result ?? throw new InvalidOperationException("Build result not set."));
        return buildId;
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var entry in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (entry.Status == BuildQueueEntryStatus.Cancelled) continue;

            _currentBuild = entry;
            entry.Status = BuildQueueEntryStatus.Building;
            entry.StartedAt = DateTimeOffset.UtcNow;

            _logger?.LogInformation("Build {BuildId} started (checkpoint): queuePos={QueuePos}, pending={Pending}",
                entry.BuildId, entry.QueuePosition, _entries.Values.Count(e => e.Status == BuildQueueEntryStatus.Queued));

            var waitStart = DateTimeOffset.UtcNow;

            try
            {
                var result = await ExecuteBuildAsync(entry, ct).ConfigureAwait(false);
                entry.Result = result;
                entry.CompletedAt = DateTimeOffset.UtcNow;

                entry.Status = result.Cancelled
                    ? BuildQueueEntryStatus.Cancelled
                    : result.ExitCode == 0
                        ? BuildQueueEntryStatus.Completed
                        : BuildQueueEntryStatus.Failed;

                var bufferKey = BuildResultBuffer.BuildBufferKey(entry.Request.Command, entry.Request.WorkingDirectory);
                _resultBuffer.Add(bufferKey, result, entry.Request.WorkingDirectory);

                _waitHandles.TryGetValue(entry.BuildId, out var tcs);
                tcs?.TrySetResult(result);

                _logger?.LogInformation("Build {BuildId} completed: {Status}, exit={ExitCode}",
                    entry.BuildId, entry.Status, result.ExitCode);
            }
            catch (OperationCanceledException) when (entry.Status == BuildQueueEntryStatus.Cancelling)
            {
                entry.Status = BuildQueueEntryStatus.Cancelled;
                entry.CompletedAt = DateTimeOffset.UtcNow;
                CompleteWithCancellation(entry.BuildId, entry);
                _logger?.LogInformation("Build {BuildId} cancelled", entry.BuildId);
            }
            catch (Exception ex)
            {
                entry.Status = BuildQueueEntryStatus.Failed;
                entry.CompletedAt = DateTimeOffset.UtcNow;

                var failResult = new BuildQueueResult
                {
                    BuildId = entry.BuildId,
                    ExitCode = -1,
                    Output = string.Empty,
                    ErrorOutput = ex.Message,
                    WaitDuration = DateTimeOffset.UtcNow - waitStart,
                    BuildDuration = TimeSpan.Zero,
                    QueuePosition = entry.QueuePosition
                };
                entry.Result = failResult;

                _waitHandles.TryGetValue(entry.BuildId, out var tcs);
                tcs?.TrySetResult(failResult);

                _logger?.LogError(ex, "Build {BuildId} failed with exception", entry.BuildId);
            }
            finally
            {
                _currentBuild = null;
                _currentBuildCts?.Dispose();
                _currentBuildCts = null;

                _logger?.LogDebug("Build {BuildId} checkpoint: status={Status}, remaining={Remaining}",
                    entry.BuildId, entry.Status, _entries.Values.Count(e => e.Status == BuildQueueEntryStatus.Queued));
            }
        }
    }

    private async Task<BuildQueueResult> ExecuteBuildAsync(BuildQueueEntry entry, CancellationToken ct)
    {
        await using var sleepScope = await PreventSleepScope.CreateAsync(_preventSleepService, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        await using var scope = new BuildExecutionScope(this, ct);
        var buildCt = scope.Token;

        if (buildCt.IsCancellationRequested)
        {
            return CancelledResult(entry, "Build cancelled while waiting for build lock");
        }

        try
        {
            await scope.AcquireLockAsync(buildCt).ConfigureAwait(false);
            _logger?.LogInformation("Build lock acquired for {BuildId} via {LockPath}",
                entry.BuildId, _crossProcessLock.LockPath);
        }
        catch (OperationCanceledException)
        {
            return CancelledResult(entry, "Build cancelled while waiting for build lock");
        }

        var sw = Stopwatch.StartNew();
        var wallStart = DateTimeOffset.UtcNow;

        var result = await _actuatorRegistry.Get(SystemActuatorKind.Bash).ExecuteAsync(
            entry.Request.Command,
            workingDirectory: entry.Request.WorkingDirectory,
            cancellationToken: buildCt).ConfigureAwait(false);

        sw.Stop();
        var wallElapsed = DateTimeOffset.UtcNow - wallStart;

        var sleepDetected = wallElapsed > sw.Elapsed + TimeSpan.FromSeconds(30);
        if (sleepDetected)
        {
            _logger?.LogWarning(
                "Sleep detected during build {BuildId}: wall={Wall}, cpu={Cpu}",
                entry.BuildId, wallElapsed, sw.Elapsed);
        }

        return new BuildQueueResult
        {
            BuildId = entry.BuildId,
            ExitCode = result.ExitCode ?? -1,
            Output = result.Stdout ?? string.Empty,
            ErrorOutput = result.Stderr ?? string.Empty,
            WaitDuration = entry.StartedAt.HasValue
                ? entry.StartedAt.Value - entry.Request.SubmittedAt
                : TimeSpan.Zero,
            BuildDuration = sw.Elapsed,
            QueuePosition = entry.QueuePosition,
            SleepDetected = sleepDetected,
            Cancelled = buildCt.IsCancellationRequested
        };
    }

    private static BuildQueueResult CancelledResult(BuildQueueEntry entry, string message)
    {
        return new BuildQueueResult
        {
            BuildId = entry.BuildId,
            ExitCode = -1,
            Output = string.Empty,
            ErrorOutput = message,
            WaitDuration = TimeSpan.Zero,
            BuildDuration = TimeSpan.Zero,
            QueuePosition = entry.QueuePosition,
            Cancelled = true,
        };
    }

    private void CompleteWithCancellation(string buildId, BuildQueueEntry entry)
    {
        var result = new BuildQueueResult
        {
            BuildId = buildId,
            ExitCode = -1,
            Output = string.Empty,
            ErrorOutput = "Build was cancelled",
            WaitDuration = entry.StartedAt.HasValue
                ? entry.StartedAt.Value - entry.Request.SubmittedAt
                : TimeSpan.Zero,
            BuildDuration = TimeSpan.Zero,
            QueuePosition = entry.QueuePosition,
            Cancelled = true
        };
        entry.Result = result;

        _waitHandles.TryGetValue(buildId, out var tcs);
        tcs?.TrySetResult(result);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _shutdownCts.Cancel();
        _queue.Writer.TryComplete();

        try
        {
#pragma warning disable VSTHRD003
            await _processingTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException) { }

        foreach (var tcs in _waitHandles.Values)
            tcs.TrySetCanceled();

        _shutdownCts.Dispose();
        _currentBuildCts?.Dispose();
        await _crossProcessLock.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class BuildExecutionScope : IAsyncDisposable
    {
        private readonly BuildQueueService _owner;
        private readonly CancellationTokenSource _cts;
        private bool _lockAcquired;
        private int _disposed;

        public BuildExecutionScope(BuildQueueService owner, CancellationToken externalCt)
        {
            _owner = owner;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            _owner._currentBuildCts = _cts;
        }

        public CancellationToken Token => _cts.Token;

        public async Task AcquireLockAsync(CancellationToken ct)
        {
            await _owner._crossProcessLock.AcquireAsync(ct).ConfigureAwait(false);
            _lockAcquired = true;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return default;

            if (_lockAcquired)
            {
                _owner._crossProcessLock.Release();
            }

            if (_owner._currentBuildCts == _cts)
            {
                _owner._currentBuildCts = null;
            }

            _cts.Dispose();
            return default;
        }
    }
}
