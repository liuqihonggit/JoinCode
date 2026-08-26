namespace Infrastructure.Network.Downloader.Coordinator;

/// <summary>
/// 下载会话实现 — 协调状态机+探测+规划+PLINQ并发+合并,实现 IDownloadSession
/// <para>状态流转:Start→Downloading,Pause→Paused,Resume→Downloading,Cancel→Cancelled</para>
/// <para>PLINQ 并发:chunks.AsParallel().WithDegreeOfParallelism(maxThreads),符合项目规范(禁 Parallel.For)</para>
/// <para>断点续传:Pause 持久化 .meta.json,Resume 读取并跳过已完成分片</para>
/// </summary>
internal sealed class DownloadSession : IDownloadSession
{
    private readonly DownloadStateMachine _stateMachine = new();
    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fs;
    private readonly string _url;
    private readonly string _filePath;
    private readonly DownloadOptions _options;
    private readonly IProgress<DownloadProgress>? _progress;
    private readonly MetadataStore _metadataStore;
    private readonly RangeSupportProbe _probe;
    private readonly ChunkDownloader _chunkDownloader;
    private readonly TimeProvider _clock;

    private CancellationTokenSource? _cts;
    private Task<DownloadResult>? _downloadTask;
    private List<DownloadChunk> _chunks = [];
    private long _totalLength;
    private RangeSupportResult? _probeResult;
    private readonly DateTimeOffset _startTime;

    /// <summary>当前状态(线程安全读取)</summary>
    public DownloadState State => _stateMachine.State;

    internal DownloadSession(
        HttpClient httpClient,
        IFileSystem fs,
        string url,
        string filePath,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        TimeProvider? clock = null)
    {
        _httpClient = httpClient;
        _fs = fs;
        _url = url;
        _filePath = filePath;
        _options = options ?? new DownloadOptions();
        _options.Validate();
        _progress = progress;
        _clock = clock ?? TimeProvider.System;
        _metadataStore = new MetadataStore(fs);
        _probe = new RangeSupportProbe(httpClient);
        _chunkDownloader = new ChunkDownloader(httpClient, fs);
        _startTime = _clock.GetUtcNow();
    }

    /// <summary>启动下载(由 RangeDownloader.StartDownload 调用)</summary>
    internal Task<DownloadResult> StartAsync(CancellationToken externalCt = default)
    {
        var startResult = _stateMachine.TryStart();
        if (!startResult.Success)
            return Task.FromResult(FailureResult(startResult.Error));

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _downloadTask = Task.Run(RunDownloadAsync, externalCt);
        return _downloadTask;
    }

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken ct = default)
    {
        var pauseResult = _stateMachine.TryPause();
        if (!pauseResult.Success)
            throw new InvalidOperationException(pauseResult.Error);

        _cts?.Cancel();
        if (_downloadTask is not null)
        {
#pragma warning disable VSTHRD003
            try { await _downloadTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
#pragma warning restore VSTHRD003
        }

        if (_chunks.Count > 0)
            _metadataStore.Save(_filePath, BuildMetadata());
    }

    /// <inheritdoc />
    public Task ResumeAsync(CancellationToken ct = default)
    {
        var resumeResult = _stateMachine.TryResume();
        if (!resumeResult.Success)
            throw new InvalidOperationException(resumeResult.Error);

        _cts = new CancellationTokenSource();
        _downloadTask = Task.Run(RunDownloadAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CancelAsync(CancellationToken ct = default)
    {
        var cancelResult = _stateMachine.TryCancel();
        if (!cancelResult.Success)
            throw new InvalidOperationException(cancelResult.Error);

        _cts?.Cancel();
        CleanupTempFiles();
    }

    /// <inheritdoc />
    public async Task<DownloadResult> WaitForCompletionAsync(CancellationToken ct = default)
    {
        if (_downloadTask is null)
            return FailureResult("下载未启动");

        try
        {
#pragma warning disable VSTHRD003
            return await _downloadTask.WaitAsync(ct).ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            return new DownloadResult(false, _filePath, _totalLength, GetDownloadedBytes(),
                _clock.GetUtcNow() - _startTime, _stateMachine.State, "已取消");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        if (_downloadTask is not null)
        {
#pragma warning disable VSTHRD003
            try { await _downloadTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
#pragma warning restore VSTHRD003
        }
    }

    // === 核心下载逻辑 ===

    private async Task<DownloadResult> RunDownloadAsync()
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        try
        {
            var probeResult = await _probe.ProbeAsync(_url, ct).ConfigureAwait(false);
            _probeResult = probeResult;
            _totalLength = probeResult.ContentLength ?? 0;

            if (!LoadOrPlanChunks(probeResult))
                return FailureResult("[DOWN009] 无法确定文件长度或分片规划失败");

            var pendingChunks = _chunks.Where(c => !c.Completed).ToList();
            if (pendingChunks.Count == 0)
                return await MergeAndCompleteAsync(ct).ConfigureAwait(false);

            var results = await DownloadChunksParallelAsync(pendingChunks, ct).ConfigureAwait(false);

            var failed = results.FirstOrDefault(r => !r.Success);
            if (failed is not null)
            {
                _stateMachine.TryFail();
                return FailureResult(failed.ErrorMessage);
            }

            return await MergeAndCompleteAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (_stateMachine.State == DownloadState.Paused)
                return new DownloadResult(false, _filePath, _totalLength, GetDownloadedBytes(),
                    _clock.GetUtcNow() - _startTime, DownloadState.Paused, "已暂停");
            return FailureResult("已取消");
        }
        catch (Exception ex)
        {
            _stateMachine.TryFail();
            return FailureResult(ex.Message);
        }
    }

    /// <summary>
    /// AIO 并发下载分片 — Task.WhenAll + SemaphoreSlim 限流,0 线程阻塞
    /// <para>替代 PLINQ + .GetAwaiter().GetResult()(BIO),用真异步并发避免线程池浪费</para>
    /// <para>SemaphoreSlim.WaitAsync 限制并发度=MaxThreads,不阻塞线程</para>
    /// </summary>
    private async Task<ChunkDownloadResult[]> DownloadChunksParallelAsync(
        List<DownloadChunk> chunks, CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(_options.MaxThreads, _options.MaxThreads);
        var tasks = chunks.Select(async chunk =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await _chunkDownloader
                    .DownloadAsync(_url, chunk, GetPartPath(chunk.Index), ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private bool LoadOrPlanChunks(RangeSupportResult probe)
    {
        if (_options.Resume)
        {
            var existing = _metadataStore.TryLoad(_filePath);
            if (existing is not null && MetadataStore.Matches(existing, _url, probe.ETag, probe.LastModified))
            {
                _chunks = existing.Chunks;
                return true;
            }
        }

        if (probe.ContentLength is null or 0)
            return false;

        var length = probe.ContentLength.Value;
        _chunks = [.. ChunkPlanner.Plan(length, _options.MaxThreads, _options.ChunkSize)];
        return true;
    }

    private async Task<DownloadResult> MergeAndCompleteAsync(CancellationToken ct)
    {
        var mergeResult = _stateMachine.TryEnterMerging();
        if (!mergeResult.Success)
            return FailureResult(mergeResult.Error);

        var partPaths = _chunks
            .OrderBy(c => c.Index)
            .Select(c => GetPartPath(c.Index))
            .Where(p => _fs.FileExists(p))
            .ToArray();

        using var destStream = _fs.CreateStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        await using var dest = destStream.ConfigureAwait(false);

        foreach (var partPath in partPaths)
        {
            using var partStream = _fs.OpenRead(partPath);
            await partStream.CopyToAsync(destStream, ct).ConfigureAwait(false);
        }

        CleanupTempFiles();

        var completeResult = _stateMachine.TryComplete();
        if (!completeResult.Success)
            return FailureResult(completeResult.Error);

        var elapsed = _clock.GetUtcNow() - _startTime;
        return new DownloadResult(true, _filePath, _totalLength, _totalLength, elapsed, DownloadState.Completed, null);
    }

    // === 辅助 ===

    private string GetPartPath(int index) => $"{_filePath}.part{index}";

    private long GetDownloadedBytes() => _chunks.Sum(c => c.Downloaded);

    private DownloadMetadata BuildMetadata() =>
        new()
        {
            Url = _url,
            TotalLength = _totalLength,
            ETag = _probeResult?.ETag,
            LastModified = _probeResult?.LastModified,
            Chunks = _chunks
        };

    private void CleanupTempFiles()
    {
        _metadataStore.Delete(_filePath);
        if (_chunks.Count == 0) return;
        foreach (var chunk in _chunks)
        {
            var partPath = GetPartPath(chunk.Index);
            if (_fs.FileExists(partPath)) _fs.DeleteFile(partPath);
        }
    }

    private DownloadResult FailureResult(string? error) =>
        new(false, _filePath, _totalLength, GetDownloadedBytes(),
            _clock.GetUtcNow() - _startTime, _stateMachine.State, error);
}
