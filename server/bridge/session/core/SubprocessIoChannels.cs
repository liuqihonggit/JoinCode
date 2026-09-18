namespace Core.Bridge;

/// <summary>
/// 子进程 IO 通道 — 封装进程读写、队列缓冲、transcript 写入、stdin Actor、读取任务生命周期
/// 从 BridgeSubprocessHandle 提取,所有进程交互和 IO 资源释放集中于此
/// ADR 0115: stdin 写入迁移到 Actor 邮箱管道，消除 AsyncLock
/// </summary>
internal sealed class SubprocessIoChannels : IAsyncDisposable
{
    private const int MaxStderrLines = 10;
    private const int MaxActivities = 10;

    private readonly IInteractiveProcess _process;
    private readonly ResilientSubprocess? _resilientSubprocess;
    private readonly StdinActor _actor;
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
        _actor = new StdinActor(this, logger);
        _process.ErrorDataReceived += OnErrorDataReceived;
    }

    /// <summary>记录 stdout 读取任务引用 — 供 DisposeAsync 等待完成</summary>
    public void SetStdoutReadTask(Task task) => _stdoutReadTask = task;

    /// <summary>设置 transcript 流 — 用于对齐 TS 端 transcript 写入</summary>
    public void SetTranscriptStream(StreamWriter stream) => _transcriptStream = stream;

    /// <summary>
    /// 向 stdin 写入数据 — 对齐 TS 端 writeStdin
    /// 韧性模式直接走 _resilientSubprocess,非韧性模式经 Actor 邮箱串行化 — ADR 0115
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

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new WriteStdinCmd(data, reply), ct).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// stdin 写入实际执行 — 由 StdinActor Consumer 串行调用,无需锁
    /// </summary>
    private async Task WriteStdinInternalAsync(string data, CancellationToken ct)
    {
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

        await _actor.DisposeAsync().ConfigureAwait(false);
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

    /// <summary>
    /// stdin 写入 Actor — 串行化 WriteStdinAsync 调用,消除 AsyncLock — ADR 0115
    /// <para>命令通过 Channel 投递,Consumer 单线程串行处理,天然无竞态。</para>
    /// </summary>
    private sealed class StdinActor : ActorBase<WriteStdinCmd, Unit>
    {
        private readonly SubprocessIoChannels _owner;
        private readonly ILogger? _logger;

        public StdinActor(SubprocessIoChannels owner, ILogger? logger) : base()
        {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 SubprocessIoChannels 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(WriteStdinCmd cmd, CancellationToken ct)
        {
            try
            {
                await _owner.WriteStdinInternalAsync(cmd.Data, ct).ConfigureAwait(false);
                cmd.Reply.SetResult();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[SubprocessHandle] StdinActor 命令处理异常");
                cmd.Reply.SetResult();
            }
        }
    }
}

/// <summary>
/// stdin 写入 Actor 命令 — WriteStdinAsync 的 Actor 化封装 — ADR 0115
/// </summary>
internal sealed record WriteStdinCmd(string Data, TaskCompletionSource Reply);
