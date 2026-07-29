
namespace Core.Ssh;

public sealed class SshSession : ISshSession
{
    private static readonly FrozenDictionary<SshConnectionState, FrozenSet<SshConnectionState>> SshTransitions = CreateTransitionTable();

    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly SshPortForwardManager _portForwardManager;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly StateMachine<SshConnectionState> _stateMachine;
    private int _isDisposed;
    private Process? _sshProcess;
    private int _reconnectAttempts;
    private CancellationTokenSource? _keepAliveCts;

    public string SessionId { get; }
    public SshSessionConfig Config { get; }
    public SshConnectionState ConnectionState => _stateMachine.CurrentState;

    public event EventHandler<SshConnectionStateChangedEventArgs>? ConnectionStateChanged;

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

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
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
                throw new InvalidOperationException("无法启动 SSH 进程");
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
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
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
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task ReconnectAsync(CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        await DisconnectAsync(ct).ConfigureAwait(false);
        _reconnectAttempts = 0;
        await ConnectAsync(ct).ConfigureAwait(false);
    }

    public Task<bool> KeepAliveAsync(CancellationToken ct = default)
    {
        if (_stateMachine.CurrentState != SshConnectionState.Connected)
        {
            throw new InvalidOperationException($"SSH 会话未连接，当前状态: {_stateMachine.CurrentState}");
        }

        if (_sshProcess == null || _sshProcess.HasExited)
        {
            _stateMachine.ForceTransitionTo(SshConnectionState.Disconnected);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public async Task<SshCommandResult> ExecuteCommandAsync(
        string command,
        CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);
        ArgumentException.ThrowIfNullOrEmpty(command);

        if (_stateMachine.CurrentState != SshConnectionState.Connected)
        {
            throw new InvalidOperationException($"SSH 会话未连接，当前状态: {_stateMachine.CurrentState}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "ssh",
            Arguments = BuildSshArgs() + $" -- {command}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        AddAuthArgs(startInfo);

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("无法启动 SSH 进程执行命令");
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

    public async Task<ISshForwardedPort> ForwardLocalPortAsync(
        int localPort,
        string remoteHost,
        int remotePort,
        CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        if (_stateMachine.CurrentState != SshConnectionState.Connected)
        {
            throw new InvalidOperationException($"SSH 会话未连接，当前状态: {_stateMachine.CurrentState}");
        }

        return await _portForwardManager.AddLocalForwardAsync(
            SessionId, Config, localPort, remoteHost, remotePort, ct).ConfigureAwait(false);
    }

    public async Task<ISshForwardedPort> ForwardRemotePortAsync(
        int remotePort,
        string localHost,
        int localPort,
        CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        if (_stateMachine.CurrentState != SshConnectionState.Connected)
        {
            throw new InvalidOperationException($"SSH 会话未连接，当前状态: {_stateMachine.CurrentState}");
        }

        return await _portForwardManager.AddRemoteForwardAsync(
            SessionId, Config, remotePort, localHost, localPort, ct).ConfigureAwait(false);
    }

    public IReadOnlyList<ISshForwardedPort> GetActiveForwards()
    {
        return _portForwardManager.GetActiveForwards();
    }

    public async ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed))
        {
            return;
        }

        StopKeepAlive();
        await _portForwardManager.DisposeAsync().ConfigureAwait(false);

        if (_sshProcess != null && !_sshProcess.HasExited)
        {
            try
            {
                _sshProcess.Kill();
            }
            catch (InvalidOperationException ex) { System.Diagnostics.Trace.WriteLine($"SshSession: failed to kill SSH process: {ex.Message}"); }

            _sshProcess.Dispose();
        }

        _stateLock.Dispose();
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
        var args = BuildSshArgs();
        if (forwardArgs != null)
        {
            args += " " + forwardArgs;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "ssh",
            Arguments = args,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        AddAuthArgs(startInfo);
        return startInfo;
    }

    private string BuildSshArgs()
    {
        var args = new StringBuilder();

        args.Append($" -o ConnectTimeout={Config.ConnectionTimeoutMs / 1000}");
        args.Append($" -o ServerAliveInterval={Config.KeepAliveIntervalMs / 1000}");
        args.Append(" -o ServerAliveCountMax=3");

        args.Append(Config.KnownHostsPolicy switch
        {
            SshKnownHostsPolicy.Strict => " -o StrictHostKeyChecking=yes",
            SshKnownHostsPolicy.AcceptNew => " -o StrictHostKeyChecking=accept-new",
            SshKnownHostsPolicy.Ignore => " -o StrictHostKeyChecking=no",
            _ => " -o StrictHostKeyChecking=accept-new"
        });

        args.Append($" -p {Config.Port}");
        args.Append($" {Config.Username}@{Config.Host}");

        return args.ToString();
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
                startInfo.Arguments += $" -i \"{keyFile}\"";
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
