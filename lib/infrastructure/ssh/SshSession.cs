
namespace Core.Ssh;

/// <summary>
/// SSH 会话实现 — 通过 ssh 子进程建立连接，提供命令执行、端口转发、保活与自动重连能力
/// </summary>
public sealed class SshSession : ISshSession
{
    private static readonly FrozenDictionary<SshConnectionState, FrozenSet<SshConnectionState>> SshTransitions = CreateTransitionTable();

    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly SshPortForwardManager _portForwardManager;
    private readonly AsyncLock _stateLock = new();
    private readonly StateMachine<SshConnectionState> _stateMachine;
    private int _isDisposed;
    private Process? _sshProcess;
    private int _reconnectAttempts;
    private CancellationTokenSource? _keepAliveCts;

    /// <summary>获取本次会话的唯一标识符</summary>
    public string SessionId { get; }
    /// <summary>获取 SSH 会话配置</summary>
    public SshSessionConfig Config { get; }
    /// <summary>获取当前连接状态</summary>
    public SshConnectionState ConnectionState => _stateMachine.CurrentState;

    /// <summary>连接状态发生变更时触发，参数携带新旧状态信息</summary>
    public event EventHandler<SshConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// 构造 SSH 会话
    /// </summary>
    /// <param name="config">SSH 会话配置</param>
    /// <param name="fs">文件系统抽象，用于写入私钥等临时文件</param>
    /// <param name="logger">可选日志记录器</param>
    public SshSession(SshSessionConfig config, IFileSystem fs, ILogger? logger = null)
    {
        Config = config;
        _fs = fs;
        _logger = logger;
        SessionId = Guid.NewGuid().ToString("N")[..16];
        _portForwardManager = new SshPortForwardManager(logger);
        _stateMachine = new StateMachine<SshConnectionState>(SshTransitions, SshConnectionState.Disconnected);
        _stateMachine.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// 建立 SSH 连接 — 启动 ssh 子进程并切换状态机到 Connected，启动保活循环
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步连接操作的任务</returns>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        using var guard = await _stateLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时");
        try
        {
            if (_stateMachine.CurrentState == SshConnectionState.Connected)
            {
                return;
            }

            _stateMachine.TransitionTo(SshConnectionState.Connecting);

            var startInfo = BuildSshProcessStartInfo(forwardArgs: null);

            _sshProcess = Process.Start(startInfo);
            if (_sshProcess == null)
            {
                _stateMachine.ForceTransitionTo(SshConnectionState.Error);
                throw new InvalidOperationException("[SSH002] 无法启动 SSH 进程");
            }

            await Task.Delay(1000, ct).ConfigureAwait(false);

            if (_sshProcess.HasExited)
            {
                var error = $"SSH 进程意外退出，退出码: {_sshProcess.ExitCode}";
                _stateMachine.ForceTransitionTo(SshConnectionState.Error);
                throw new InvalidOperationException(error);
            }

            _stateMachine.TransitionTo(SshConnectionState.Connected);
            StartKeepAlive();

            _logger?.LogInformation("SSH 会话已连接: {SessionId} -> {Username}@{Host}:{Port}",
                SessionId, Config.Username, Config.Host, Config.Port);
        }
        catch (OperationCanceledException)
        {
            _stateMachine.ForceTransitionTo(SshConnectionState.Error);
            throw;
        }
        catch (Exception)
        {
            _stateMachine.ForceTransitionTo(SshConnectionState.Error);
            throw;
        }

    }

    /// <summary>
    /// 主动断开 SSH 连接 — 停止保活、停止所有端口转发并终止 ssh 子进程
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步断开操作的任务</returns>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        using var guard = await _stateLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时");

        StopKeepAlive();
        await _portForwardManager.StopAllAsync(ct).ConfigureAwait(false);

        if (_sshProcess != null && !_sshProcess.HasExited)
        {
            _sshProcess.Kill();
            await _sshProcess.WaitForExitAsync(ct).ConfigureAwait(false);
        }

        _sshProcess = null;
        _stateMachine.TransitionTo(SshConnectionState.Disconnected);

        _logger?.LogInformation("SSH 会话已断开: {SessionId}", SessionId);
    
    }

    /// <summary>
    /// 重新连接 — 先断开再重置重连计数后建立新连接
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步重连操作的任务</returns>
    public async Task ReconnectAsync(CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        await DisconnectAsync(ct).ConfigureAwait(false);
        _reconnectAttempts = 0;
        await ConnectAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查会话保活状态 — 进程已退出时切换状态机到 Disconnected 并返回 false
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>会话存活返回 true，否则返回 false</returns>
    /// <exception cref="InvalidOperationException">会话未处于 Connected 状态时抛出</exception>
    public Task<bool> KeepAliveAsync(CancellationToken ct = default)
    {
        if (_stateMachine.CurrentState != SshConnectionState.Connected)
        {
            throw new InvalidOperationException($"[SSH001] SSH 会话未连接，当前状态: {_stateMachine.CurrentState}");
        }

        if (_sshProcess == null || _sshProcess.HasExited)
        {
            _stateMachine.ForceTransitionTo(SshConnectionState.Disconnected);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 在远端执行指定命令并捕获标准输出、标准错误与退出码
    /// </summary>
    /// <param name="command">待在远端执行的 shell 命令</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>命令执行结果，包含退出码、输出、错误与耗时</returns>
    /// <exception cref="InvalidOperationException">会话未处于 Connected 状态或无法启动 ssh 进程时抛出</exception>
    public async Task<SshCommandResult> ExecuteCommandAsync(
        string command,
        CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);
        ArgumentException.ThrowIfNullOrEmpty(command);

        if (_stateMachine.CurrentState != SshConnectionState.Connected)
        {
            throw new InvalidOperationException($"[SSH001] SSH 会话未连接，当前状态: {_stateMachine.CurrentState}");
        }

        var builder = new IO.ProcessService.ProcessStartInfoBuilder(new IO.ProcessService.ProcessEncodingProvider());
        var startInfo = builder.Build(new ProcessOptions
        {
            FileName = "ssh",
            ArgumentList = [.. BuildSshArgList(), "--", command],
            SkipArgumentValidation = true,
        });

        AddAuthArgs(startInfo);

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("[SSH003] 无法启动 SSH 进程执行命令");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        sw.Stop();

        return new SshCommandResult
        {
            Command = command,
            ExitCode = process.ExitCode,
            Stdout = stdout,
            Stderr = stderr,
            Duration = sw.Elapsed
        };
    }

    /// <summary>
    /// 建立本地端口转发 — 将本地端口映射到远端主机端口
    /// </summary>
    /// <param name="localPort">本地监听端口</param>
    /// <param name="remoteHost">远程目标主机</param>
    /// <param name="remotePort">远程目标端口</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已启动的本地端口转发实例</returns>
    /// <exception cref="InvalidOperationException">会话未处于 Connected 状态时抛出</exception>
    public async Task<ISshForwardedPort> ForwardLocalPortAsync(
        int localPort,
        string remoteHost,
        int remotePort,
        CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        if (_stateMachine.CurrentState != SshConnectionState.Connected)
        {
            throw new InvalidOperationException($"[SSH001] SSH 会话未连接，当前状态: {_stateMachine.CurrentState}");
        }

        return await _portForwardManager.AddLocalForwardAsync(
            SessionId, Config, localPort, remoteHost, remotePort, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 建立远程端口转发 — 将远端端口映射回本地端口
    /// </summary>
    /// <param name="remotePort">远程监听端口</param>
    /// <param name="localHost">本地绑定主机</param>
    /// <param name="localPort">本地目标端口</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已启动的远程端口转发实例</returns>
    /// <exception cref="InvalidOperationException">会话未处于 Connected 状态时抛出</exception>
    public async Task<ISshForwardedPort> ForwardRemotePortAsync(
        int remotePort,
        string localHost,
        int localPort,
        CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        if (_stateMachine.CurrentState != SshConnectionState.Connected)
        {
            throw new InvalidOperationException($"[SSH001] SSH 会话未连接，当前状态: {_stateMachine.CurrentState}");
        }

        return await _portForwardManager.AddRemoteForwardAsync(
            SessionId, Config, remotePort, localHost, localPort, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取本会话所有处于活动状态的端口转发
    /// </summary>
    /// <returns>活动端口转发的集合</returns>
    public IEnumerable<ISshForwardedPort> GetActiveForwards()
    {
        return _portForwardManager.GetActiveForwards();
    }

    /// <summary>
    /// 异步释放资源 — 停止保活、释放端口转发管理器并终止 ssh 子进程
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed))
        {
            return ValueTask.CompletedTask;
        }

        StopKeepAlive();
        var portTask = _portForwardManager.DisposeAsync();

        if (_sshProcess != null && !_sshProcess.HasExited)
        {
            try
            {
                _sshProcess.Kill();
            }
            catch (InvalidOperationException ex) { _logger?.LogWarning(ex, "SshSession: 终止 SSH 进程失败"); }

            _sshProcess.Dispose();
        }

        _stateLock.Dispose();
        return portTask;
    }

    private void OnStateChanged(object? sender, StateChangedEventArgs<SshConnectionState> e)
    {
        ConnectionStateChanged?.Invoke(this, new SshConnectionStateChangedEventArgs
        {
            SessionId = SessionId,
            NewState = e.NewState,
            PreviousState = e.OldState
        });
    }

    private void StartKeepAlive()
    {
        _keepAliveCts = new CancellationTokenSource();
        var ct = _keepAliveCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(Config.KeepAliveIntervalMs, ct).ConfigureAwait(false);
                if (!await KeepAliveAsync(ct).ConfigureAwait(false) && Config.AutoReconnect)
                {
                    await TryAutoReconnectAsync(ct).ConfigureAwait(false);
                }
            }
        }, ct);
    }

    private void StopKeepAlive()
    {
        _keepAliveCts?.Cancel();
        _keepAliveCts?.Dispose();
        _keepAliveCts = null;
    }

    private async Task TryAutoReconnectAsync(CancellationToken ct)
    {
        _stateMachine.ForceTransitionTo(SshConnectionState.Reconnecting);

        while (_reconnectAttempts < Config.MaxReconnectAttempts && !ct.IsCancellationRequested)
        {
            _reconnectAttempts++;
            var backoff = new ExponentialBackoff(
                TimeSpan.FromMilliseconds(Config.ReconnectDelayMs),
                TimeSpan.FromMilliseconds(Config.MaxReconnectDelayMs));
            var delay = (int)backoff.CalculateDelay(_reconnectAttempts - 1).TotalMilliseconds;

            _logger?.LogWarning("SSH 自动重连尝试 {Attempt}/{Max}，等待 {Delay}ms",
                _reconnectAttempts, Config.MaxReconnectAttempts, delay);

            await Task.Delay(delay, ct).ConfigureAwait(false);

            try
            {
                StopKeepAlive();

                if (_sshProcess != null && !_sshProcess.HasExited)
                {
                    _sshProcess.Kill();
                    _sshProcess.Dispose();
                    _sshProcess = null;
                }

                var startInfo = BuildSshProcessStartInfo(forwardArgs: null);
                _sshProcess = Process.Start(startInfo);

                if (_sshProcess != null && !_sshProcess.HasExited)
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);

                    if (!_sshProcess.HasExited)
                    {
                        _stateMachine.TransitionTo(SshConnectionState.Connected);
                        _reconnectAttempts = 0;
                        StartKeepAlive();
                        _logger?.LogInformation("SSH 自动重连成功: {SessionId}", SessionId);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "SSH 自动重连失败: {SessionId}", SessionId);
            }
        }

        _stateMachine.ForceTransitionTo(SshConnectionState.Error);
    }

    private ProcessStartInfo BuildSshProcessStartInfo(string? forwardArgs)
    {
        var argList = new List<string>(BuildSshArgList());
        if (forwardArgs != null)
        {
            argList.Add(forwardArgs);
        }

        var builder = new IO.ProcessService.ProcessStartInfoBuilder(new IO.ProcessService.ProcessEncodingProvider());
        var startInfo = builder.Build(new ProcessOptions
        {
            FileName = "ssh",
            ArgumentList = argList,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            SkipArgumentValidation = true,
        });

        AddAuthArgs(startInfo);
        return startInfo;
    }

    /// <summary>
    /// 构建 SSH 连接参数列表 — 参数化启动，消除字符串拼接注入风险
    /// </summary>
    private IReadOnlyList<string> BuildSshArgList()
    {
        var args = new List<string>();

        args.Add("-o");
        args.Add($"ConnectTimeout={Config.ConnectionTimeoutMs / 1000}");
        args.Add("-o");
        args.Add($"ServerAliveInterval={Config.KeepAliveIntervalMs / 1000}");
        args.Add("-o");
        args.Add("ServerAliveCountMax=3");

        args.Add("-o");
        args.Add(Config.KnownHostsPolicy switch
        {
            SshKnownHostsPolicy.Strict => "StrictHostKeyChecking=yes",
            SshKnownHostsPolicy.AcceptNew => "StrictHostKeyChecking=accept-new",
            SshKnownHostsPolicy.Ignore => "StrictHostKeyChecking=no",
            _ => "StrictHostKeyChecking=accept-new"
        });

        args.Add("-p");
        args.Add(Config.Port.ToString());
        args.Add($"{Config.Username}@{Config.Host}");

        return args;
    }

    private void AddAuthArgs(ProcessStartInfo startInfo)
    {
        switch (Config.AuthMethod)
        {
            case SshAuthMethod.PrivateKey when Config.PrivateKey != null:
                var keyFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    AppDataConstants.AppDataFolder, "ssh", $"key_{SessionId}");

                _fs.CreateDirectory(Path.GetDirectoryName(keyFile)!);
                _fs.WriteAllText(keyFile, Config.PrivateKey);
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(keyFile);
                break;

            case SshAuthMethod.Password when Config.Password != null:
                startInfo.Environment["SSHPASS"] = Config.Password;
                break;
        }
    }

    private static FrozenDictionary<SshConnectionState, FrozenSet<SshConnectionState>> CreateTransitionTable()
    {
        return new Dictionary<SshConnectionState, FrozenSet<SshConnectionState>>
        {
            [SshConnectionState.Disconnected] = new HashSet<SshConnectionState>
            {
                SshConnectionState.Connecting
            }.ToFrozenSet(),
            [SshConnectionState.Connecting] = new HashSet<SshConnectionState>
            {
                SshConnectionState.Connected,
                SshConnectionState.Error
            }.ToFrozenSet(),
            [SshConnectionState.Connected] = new HashSet<SshConnectionState>
            {
                SshConnectionState.Disconnected,
                SshConnectionState.Reconnecting
            }.ToFrozenSet(),
            [SshConnectionState.Reconnecting] = new HashSet<SshConnectionState>
            {
                SshConnectionState.Connected,
                SshConnectionState.Error
            }.ToFrozenSet(),
            [SshConnectionState.Error] = new HashSet<SshConnectionState>
            {
                SshConnectionState.Disconnected,
                SshConnectionState.Connecting
            }.ToFrozenSet()
        }.ToFrozenDictionary();
    }
}
