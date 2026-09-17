namespace Core.Bridge;

/// <summary>
/// 子进程 IO 通道 — 封装进程读写、队列缓冲、transcript 写入、stdin 锁、读取任务生命周期
/// 从 BridgeSubprocessHandle 提取,所有进程交互和 IO 资源释放集中于此
/// </summary>
internal sealed class SubprocessIoChannels : IAsyncDisposable
{
    private const int MaxStderrLines = 10;
    private const int MaxActivities = 10;

    private readonly IInteractiveProcess _process;
    private readonly ResilientSubprocess? _resilientSubprocess;
    private readonly AsyncLock _stdinLock = new();
    private readonly Queue<string> _stderrQueue;
    private readonly Queue<string> _activityQueue;
    private readonly ILogger? _logger;
    private readonly string _sessionId;
    private readonly CancellationTokenSource _readCts;
    private Task? _stdoutReadTask;
    private StreamWriter? _transcriptStream;
    private bool _ioDisposed;

    /// <summary>读取取消令牌 — 供外部启动读取循环</summary>
    public CancellationToken ReadCancellationToken => _readCts.Token;

    /// <summary>最近的活动 — 对齐 TS 端 activities（遍历器，不分配新集合）</summary>
    public IEnumerable<string> Activities => _activityQueue;

    /// <summary>最近的 stderr 输出 — 对齐 TS 端 lastStderr（遍历器，不分配新集合）</summary>
    public IEnumerable<string> StderrLines => _stderrQueue;

    /// <summary>当前活动 — 对齐 TS 端 currentActivity</summary>
    public string? CurrentActivity { get; private set; }

    /// <summary>进程是否仍在运行</summary>
    public bool IsRunning
    {
        get { try { return !_process.HasExited; } catch { return false; } }
    }

    /// <summary>进程退出码（仅在进程退出后有效）</summary>
    public int ExitCode => _process.ExitCode;

    /// <summary>
    /// 构造 IO 通道 — 订阅 stderr 事件、初始化队列和读取取消令牌
    /// </summary>
    public SubprocessIoChannels(IInteractiveProcess process, ResilientSubprocess? resilientSubprocess, ILogger? logger, string sessionId)
    {
        _process = process;
        _resilientSubprocess = resilientSubprocess;
        _logger = logger;
        _sessionId = sessionId;
        _stderrQueue = new Queue<string>(MaxStderrLines);
        _activityQueue = new Queue<string>(MaxActivities);
        _readCts = new CancellationTokenSource();
        _process.ErrorDataReceived += OnErrorDataReceived;
    }

    /// <summary>记录 stdout 读取任务引用 — 供 DisposeAsync 等待完成</summary>
    public void SetStdoutReadTask(Task task) => _stdoutReadTask = task;

    /// <summary>设置 transcript 流 — 用于对齐 TS 端 transcript 写入</summary>
    public void SetTranscriptStream(StreamWriter stream) => _transcriptStream = stream;

    /// <summary>
    /// 向 stdin 写入数据 — 对齐 TS 端 writeStdin
    /// </summary>
    public async Task WriteStdinAsync(string data, CancellationToken ct = default)
    {
        if (_resilientSubprocess is not null)
        {
            try
            {
                await _resilientSubprocess.WriteStdinAsync(data, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[SubprocessHandle] 写入 stdin 失败（韧性）");
            }
            return;
        }

        using var guard = await _stdinLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_stdinLock.Name}' 等待超时");
        try
        {
            if (_process.StandardInput.BaseStream is null || !_process.StandardInput.BaseStream.CanWrite)
            {
                _logger?.LogWarning("[SubprocessHandle] stdin 不可写");
                return;
            }

            await _process.StandardInput.WriteAsync(data.AsMemory(), ct).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SubprocessHandle] 写入 stdin 失败");
        }
    }

    /// <summary>读取一行 stdout — 韧性模式优先</summary>
    public async Task<string?> ReadStdoutLineAsync(CancellationToken ct)
    {
        return _resilientSubprocess is not null
            ? await _resilientSubprocess.ReadStdoutLineAsync(ct).ConfigureAwait(false)
            : await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
    }

    /// <summary>入队活动行 + 更新 CurrentActivity</summary>
    public void EnqueueActivity(string line)
    {
        EnqueueBounded(_activityQueue, line, MaxActivities);
        CurrentActivity = line;
    }

    /// <summary>写入 transcript（失败不阻塞）</summary>
    public void WriteTranscript(string line)
    {
        if (_transcriptStream is null) return;
        try
        {
            _transcriptStream.WriteLine(line);
            _transcriptStream.Flush();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[BridgeSubprocessManager] transcript 写入失败");
        }
    }

    /// <summary>
    /// 终止进程 — 消除 Kill/ForceKill 重复片段,统一 try-catch + HasExited + Kill 模式
    /// </summary>
    public void TryKillProcess(string action, LogLevel level)
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _logger?.Log(level, "[SubprocessHandle] {Action}: {SessionId}", action, _sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SubprocessHandle] {Action}失败", action);
        }
    }

    /// <summary>等待进程退出</summary>
    public Task WaitForExitAsync(CancellationToken ct) => _process.WaitForExitAsync(ct);

    /// <summary>
    /// 异步释放所有 IO 资源 — 取消读取、等待读取任务、释放进程/锁/transcript
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_ioDisposed) return;
        _ioDisposed = true;

        _readCts.CancelAndDisposeSafe(_logger);

        if (_stdoutReadTask is not null)
        {
            try { await _stdoutReadTask.ConfigureAwait(false); }
            catch (Exception ex) { _logger?.LogWarning(ex, "[BridgeSubprocessHandle] Dispose 时读取任务异常"); }
        }

        if (_resilientSubprocess is not null)
            await _resilientSubprocess.DisposeSafeAsync(_logger).ConfigureAwait(false);
        else
            await _process.DisposeSafeAsync(_logger).ConfigureAwait(false);

        _stdinLock.DisposeSafe(_logger);
        _transcriptStream.DisposeSafe(_logger);
        _transcriptStream = null;
    }

    private void OnErrorDataReceived(object? sender, string line) => EnqueueBounded(_stderrQueue, line, MaxStderrLines);

    private static void EnqueueBounded(Queue<string> queue, string item, int maxCount)
    {
        while (queue.Count >= maxCount)
        {
            queue.Dequeue();
        }
        queue.Enqueue(item);
    }
}
