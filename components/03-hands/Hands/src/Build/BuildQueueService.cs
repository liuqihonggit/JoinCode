namespace Services.Build;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// 编译队列服务 — 编译请求串行化 + 结果缓冲区
/// 核心特性：串行执行 + 结果缓冲区（源码指纹失效）+ 防睡眠
/// </summary>
[Register]
public sealed partial class BuildQueueService : IBuildQueueService
{
    [Inject] private readonly IShellExecutionService _shellExecutionService;
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly IPreventSleepService? _preventSleepService;
    [Inject] private readonly ILogger<BuildQueueService>? _logger;

    private const string DefaultCrossProcessLockFileName = "JoinCode.Build.lock";
    private const string GitDirName = ".git";
    private static readonly string GitWorktreesMarker = $"{GitDirName}{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}";
    private static readonly TimeSpan CrossProcessLockPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly Channel<BuildQueueEntry> _queue = Channel.CreateUnbounded<BuildQueueEntry>();
    private readonly ConcurrentDictionary<string, BuildQueueEntry> _entries = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BuildQueueResult>> _waitHandles = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _processingTask;

    /// <summary>
    /// 跨进程构建锁文件路径 — 多个 subAgent 进程（不同 git worktree）通过此文件串行编译
    /// 默认放在 git 仓库的 .git/ 目录下（所有 worktree 共享），通过 IFileSystem.CreateStream + FileShare.None 实现跨进程互斥
    /// </summary>
    private readonly string _crossProcessLockPath;
    private Stream? _crossProcessLockFile;

    private int _buildCounter;
    private BuildQueueEntry? _currentBuild;
    private CancellationTokenSource? _currentBuildCts;

    private readonly ConcurrentDictionary<string, BuildBufferEntry> _resultBuffer = new();

    private long _lastFingerprintTicks;
    private DateTimeOffset _fingerprintComputedAt = DateTimeOffset.MinValue;
    private readonly object _fingerprintLock = new();

    public BuildQueueService(
        IShellExecutionService shellExecutionService,
        IFileSystem fs,
        IPreventSleepService? preventSleepService = null,
        ILogger<BuildQueueService>? logger = null,
        string? crossProcessLockPath = null)
    {
        _shellExecutionService = shellExecutionService;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _preventSleepService = preventSleepService;
        _logger = logger;
        _crossProcessLockPath = crossProcessLockPath
            ?? ResolveDefaultLockPath(_fs);
        _processingTask = ProcessQueueAsync(_shutdownCts.Token);
    }

    /// <summary>
    /// 解析默认锁文件路径 — 优先放在 git 仓库的 .git/ 目录下（所有 worktree 共享同一锁）
    /// <para>主 worktree: .git 是目录 → {repo-root}/.git/JoinCode.Build.lock</para>
    /// <para>链接 worktree: .git 是文件 → 解析 gitdir 得到公共 .git 目录 → {common-git-dir}/JoinCode.Build.lock</para>
    /// <para>非 git 仓库: 回退到 %TEMP%/JoinCode.Build.lock</para>
    /// </summary>
    private static string ResolveDefaultLockPath(IFileSystem fs)
    {
        var currentDir = fs.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(currentDir))
        {
            var gitPath = fs.CombinePath(currentDir, GitDirName);

            // 主 worktree — .git 是目录,所有 worktree 共享
            if (fs.DirectoryExists(gitPath))
                return fs.CombinePath(gitPath, DefaultCrossProcessLockFileName);

            // 链接 worktree — .git 是文件,解析得到公共 .git 目录
            if (fs.FileExists(gitPath))
            {
                var commonGitDir = ResolveCommonGitDir(fs, gitPath, currentDir);
                if (commonGitDir is not null && fs.DirectoryExists(commonGitDir))
                    return fs.CombinePath(commonGitDir, DefaultCrossProcessLockFileName);
            }

            var parent = fs.GetParentPath(currentDir);
            if (parent is null || parent == currentDir) break;
            currentDir = parent;
        }

        // 不在 git 仓库内 — 回退到 TEMP
        return fs.CombinePath(Path.GetTempPath(), DefaultCrossProcessLockFileName);
    }

    /// <summary>
    /// 解析链接 worktree 的 .git 文件,得到公共 .git 目录路径
    /// .git 文件内容格式: "gitdir: /path/to/main/.git/worktrees/{name}"
    /// 公共 .git 目录: /path/to/main/.git
    /// </summary>
    private static string? ResolveCommonGitDir(IFileSystem fs, string gitFilePath, string worktreePath)
    {
        try
        {
            var content = fs.ReadAllText(gitFilePath).Trim();
            const string prefix = "gitdir:";
            if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var gitdirRelative = content[prefix.Length..].Trim();
            var gitdirAbs = fs.CombinePath(worktreePath, gitdirRelative);
            var normalizedGitdir = fs.GetFullPath(gitdirAbs);

            // 查找 .git/worktrees/ 标记,提取公共 .git 目录
            var markerIdx = normalizedGitdir.IndexOf(GitWorktreesMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIdx < 0) return null;

            var commonGitDir = normalizedGitdir[..(markerIdx + GitDirName.Length)];
            return commonGitDir;
        }
        catch (IOException)
        {
            // .git 文件读取失败,回退到 TEMP
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<string> SubmitAsync(BuildRequest request, CancellationToken ct)
    {
        var bufferKey = BuildBufferKey(request.Command, request.WorkingDirectory);

        if (TryGetBufferedResult(bufferKey, out var bufferedResult))
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
        _logger?.LogInformation("Build result buffer cleared");
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

    /// <summary>
    /// 构建结果缓冲区 key — 原始命令 + 工作目录
    /// </summary>
    internal static string BuildBufferKey(string command, string? workingDirectory)
    {
        return $"{workingDirectory ?? ""}|{command}";
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
        _waitHandles[buildId].TrySetResult(entry.Result!);
        return buildId;
    }

    /// <summary>
    /// 检查结果缓冲区 — 源码指纹未变则命中
    /// </summary>
    private bool TryGetBufferedResult(string bufferKey, [NotNullWhen(true)] out BuildQueueResult? result)
    {
        result = null;

        if (!_resultBuffer.TryGetValue(bufferKey, out var bufferEntry))
            return false;

        var currentFingerprint = ComputeSourceFingerprint(bufferEntry.WorkingDirectory);
        if (currentFingerprint != 0 && bufferEntry.SourceFingerprint != 0 && currentFingerprint != bufferEntry.SourceFingerprint)
        {
            _resultBuffer.TryRemove(bufferKey, out _);
            _logger?.LogDebug("Result buffer invalidated by source fingerprint: {BufferKey}", bufferKey);
            return false;
        }

        result = bufferEntry.Result;
        return true;
    }

    /// <summary>
    /// 将编译结果写入缓冲区
    /// </summary>
    private void BufferResult(string bufferKey, BuildQueueResult result, string? workingDirectory)
    {
        var fingerprint = ComputeSourceFingerprint(workingDirectory);

        _resultBuffer[bufferKey] = new BuildBufferEntry
        {
            Result = result,
            WorkingDirectory = workingDirectory,
            SourceFingerprint = fingerprint,
        };

        _logger?.LogInformation("Build result buffered: {BufferKey}", bufferKey);
    }

    /// <summary>
    /// 计算源码指纹 — 工作目录下 .cs/.csproj 文件的最大 LastWriteTime ticks
    /// 结果缓存 1 秒避免频繁 IO
    /// </summary>
    private long ComputeSourceFingerprint(string? workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory)) return 0;

        lock (_fingerprintLock)
        {
            if (DateTimeOffset.UtcNow - _fingerprintComputedAt < TimeSpan.FromSeconds(1) && _lastFingerprintTicks != 0)
                return _lastFingerprintTicks;
        }

        long maxTicks = 0;
        try
        {
            var dir = new DirectoryInfo(workingDirectory!);
            if (!dir.Exists) return 0;

            foreach (var file in dir.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (file.LastWriteTimeUtc.Ticks > maxTicks)
                    maxTicks = file.LastWriteTimeUtc.Ticks;
            }
            foreach (var file in dir.EnumerateFiles("*.csproj", SearchOption.AllDirectories))
            {
                if (file.LastWriteTimeUtc.Ticks > maxTicks)
                    maxTicks = file.LastWriteTimeUtc.Ticks;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogDebug(ex, "Access denied scanning source files in {Directory}", workingDirectory);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger?.LogDebug(ex, "Directory not found scanning source files: {Directory}", workingDirectory);
        }

        lock (_fingerprintLock)
        {
            _lastFingerprintTicks = maxTicks;
            _fingerprintComputedAt = DateTimeOffset.UtcNow;
        }

        return maxTicks;
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var entry in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (entry.Status == BuildQueueEntryStatus.Cancelled) continue;

            _currentBuild = entry;
            entry.Status = BuildQueueEntryStatus.Building;
            entry.StartedAt = DateTimeOffset.UtcNow;

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

                var bufferKey = BuildBufferKey(entry.Request.Command, entry.Request.WorkingDirectory);
                BufferResult(bufferKey, result, entry.Request.WorkingDirectory);

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
            }
        }
    }

    /// <summary>
    /// 异步获取跨进程构建锁 — 以独占方式打开锁文件，其他进程尝试打开将抛 IOException
    /// 异步轮询避免阻塞调用线程，支持取消令牌
    /// </summary>
    private async Task AcquireCrossProcessLockAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _crossProcessLockFile = _fs.CreateStream(
                    _crossProcessLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return;
            }
            catch (IOException ex)
            {
                // 文件被其他进程锁定，等待后重试
                _logger?.LogDebug(ex, "Build lock file is held by another process, retrying: {LockPath}", _crossProcessLockPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                // 路径权限不足，等待后重试（罕见情况）
                _logger?.LogDebug(ex, "Build lock file access denied, retrying: {LockPath}", _crossProcessLockPath);
            }

            await Task.Delay(CrossProcessLockPollInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 释放跨进程构建锁 — 关闭文件流即释放文件锁
    /// </summary>
    private void ReleaseCrossProcessLock()
    {
        _crossProcessLockFile?.Dispose();
        _crossProcessLockFile = null;
    }

    private async Task<BuildQueueResult> ExecuteBuildAsync(BuildQueueEntry entry, CancellationToken ct)
    {
        await (_preventSleepService?.PreventSleepAsync(cancellationToken: CancellationToken.None) ?? Task.CompletedTask).ConfigureAwait(false);
        try
        {
            _currentBuildCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var buildCt = _currentBuildCts.Token;

            // 跨进程构建锁 — FileStream + FileShare.None 实现进程间互斥
            // 多个 subAgent 进程（不同 git worktree）通过此锁串行编译，避免并行编译冲突
            // 异步轮询获取锁，无线程亲和性，可安全跨 await 使用（修复原 Mutex 死锁问题）
            var lockAcquired = false;
            try
            {
                if (buildCt.IsCancellationRequested)
                {
                    return new BuildQueueResult
                    {
                        BuildId = entry.BuildId,
                        ExitCode = -1,
                        Output = string.Empty,
                        ErrorOutput = "Build cancelled while waiting for build lock",
                        WaitDuration = TimeSpan.Zero,
                        BuildDuration = TimeSpan.Zero,
                        QueuePosition = entry.QueuePosition,
                        Cancelled = true,
                    };
                }

                await AcquireCrossProcessLockAsync(buildCt).ConfigureAwait(false);
                lockAcquired = true;
                _logger?.LogInformation("Build lock acquired for {BuildId} via {LockPath}",
                    entry.BuildId, _crossProcessLockPath);
            }
            catch (OperationCanceledException)
            {
                return new BuildQueueResult
                {
                    BuildId = entry.BuildId,
                    ExitCode = -1,
                    Output = string.Empty,
                    ErrorOutput = "Build cancelled while waiting for build lock",
                    WaitDuration = TimeSpan.Zero,
                    BuildDuration = TimeSpan.Zero,
                    QueuePosition = entry.QueuePosition,
                    Cancelled = true,
                };
            }

            try
            {
                var sw = Stopwatch.StartNew();
                var wallStart = DateTimeOffset.UtcNow;

                var result = await _shellExecutionService.ExecuteAsync(
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
            finally
            {
                if (lockAcquired)
                {
                    ReleaseCrossProcessLock();
                    _logger?.LogInformation("Build lock released for {BuildId}", entry.BuildId);
                }
            }
        }
        finally
        {
            await (_preventSleepService?.AllowSleepAsync(CancellationToken.None) ?? Task.CompletedTask).ConfigureAwait(false);
        }
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
        _crossProcessLockFile?.Dispose();
        _crossProcessLockFile = null;
    }

    private sealed class BuildBufferEntry
    {
        public required BuildQueueResult Result { get; init; }
        public string? WorkingDirectory { get; init; }
        public long SourceFingerprint { get; init; }
    }
}
