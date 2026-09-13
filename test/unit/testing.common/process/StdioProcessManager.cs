
namespace Testing.Common.Process;

/// <summary>
/// 管理CLI进程的stdin/stdout通信
/// 实现真实的双向对话
/// </summary>
public sealed class StdioProcessManager : IAsyncDisposable
{
    private System.Diagnostics.Process? _process;
    private StreamWriter? _stdinWriter;
    private StreamReader? _stdoutReader;
    private StreamReader? _stderrReader;
    private readonly ILogger<StdioProcessManager> _logger;
    private readonly BufferedChannel _outputChannel = new();
    private readonly BufferedChannel _errorChannel = new();
    private CancellationTokenSource? _readCts;
    private Task? _stdoutReadTask;
    private Task? _stderrReadTask;

    private string _pidTag = "";

    private TaskCompletionSource _outputChangedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _errorChangedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public StdioProcessManager(ILogger<StdioProcessManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 进程是否正在运行
    /// </summary>
    public bool IsRunning => _process?.HasExited == false;

    /// <summary>
    /// 启动CLI进程
    /// </summary>
    public async Task StartAsync(StdioProcessConfig config, CancellationToken ct = default)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = config.ExecutablePath,
            Arguments = config.Arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
            WorkingDirectory = config.WorkingDirectory ?? Path.GetDirectoryName(config.ExecutablePath) ?? Testing.Common.TestConfiguration.FileSystem.GetCurrentDirectory()
        };

        if (config.EnvironmentVariables != null)
        {
            foreach (var (key, value) in config.EnvironmentVariables)
            {
                startInfo.EnvironmentVariables[key] = value;
            }
        }

        _process = new System.Diagnostics.Process { StartInfo = startInfo };
        _process.Start();

        _pidTag = $"P{_process.Id}";
        _logger.LogInformation("[{PidTag}StdioManager] 启动进程: {Path} {Args}", _pidTag, config.ExecutablePath, config.Arguments);

        _stdinWriter = _process.StandardInput;
        _stdoutReader = _process.StandardOutput;
        _stderrReader = _process.StandardError;

        _readCts = new CancellationTokenSource();
        _stdoutReadTask = Task.Run(() => ReadStdoutAsync(_readCts.Token), ct);
        _stderrReadTask = Task.Run(() => ReadStderrAsync(_readCts.Token), ct);

        await WaitForFirstOutputAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(true);

        _logger.LogInformation("[{PidTag}StdioManager] 进程已启动", _pidTag);
    }

    /// <summary>
    /// 等待进程首行输出 — 替代固定500ms延迟，确保进程真正开始运行
    /// 同时检查 stdout 和 stderr，因为 jcc.exe 初始化时先输出到 stderr
    /// </summary>
    private async Task WaitForFirstOutputAsync(TimeSpan timeout, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();
            if (_process?.HasExited == true) return;

            bool hasOutput;
            hasOutput = await _outputChannel.TryPredicateAsync(s => s.Length > 0, ct).ConfigureAwait(true);
            if (hasOutput) return;

            hasOutput = await _errorChannel.TryPredicateAsync(s => s.Length > 0, ct).ConfigureAwait(true);
            if (hasOutput) return;

            await WaitForOutputChangeAsync(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 向进程发送消息
    /// </summary>
    public async Task SendAsync(string message, CancellationToken ct = default)
    {
        if (_stdinWriter == null)
            throw new InvalidOperationException("[TCU001] 进程未启动");

        _logger.LogDebug("[{PidTag}StdioManager] 发送: {Message}", _pidTag, message.Length > 100 ? message[..100] + "..." : message);

        await _stdinWriter.WriteLineAsync(message.AsMemory(), ct).ConfigureAwait(true);
        await _stdinWriter.FlushAsync(ct).ConfigureAwait(true);
    }

    /// <summary>
    /// 等待并读取输出，直到满足条件或超时
    /// </summary>
    public async Task<string> WaitForOutputAsync(
        Func<string, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        timeout ??= TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            if (await _outputChannel.TryPredicateAsync(predicate, ct).ConfigureAwait(true))
            {
                return await _outputChannel.GetAllAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(true);
            }

            await WaitForOutputChangeAsync(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(true);
        }

        throw new TimeoutException($"[GEN050] 等待输出超时 (>{timeout.Value.TotalSeconds}s), process={_pidTag}, running={IsRunning}");
    }

    /// <summary>
    /// 等待特定JSON-RPC响应
    public async Task<string> WaitForJsonRpcResponseAsync(
        string? method = null,
        object? id = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        return await WaitForOutputAsync(output =>
        {
            if (!output.Contains('{')) return false;

            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith('{')) continue;

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                    var root = doc.RootElement;

                    if (method != null)
                    {
                        if (root.TryGetProperty("method", out var methodProp))
                        {
                            if (methodProp.GetString() == method) return true;
                        }
                    }

                    if (id != null)
                    {
                        if (root.TryGetProperty("id", out var idProp))
                        {
                            var idMatch = idProp.ValueKind == System.Text.Json.JsonValueKind.Number
                                ? idProp.GetInt64().Equals(id)
                                : idProp.GetString()?.Equals(id.ToString()) == true;
                            if (idMatch) return true;
                        }
                    }

                    if (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _))
                        return true;
                }
                catch (Exception ex)
                {
                    // 忽略解析错误
                    System.Diagnostics.Trace.WriteLine($"JSON-RPC响应解析失败: {ex.Message}");
                }
            }
            return false;
        }, timeout, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// 清空输出缓冲区
    /// </summary>
    public Task ClearOutputAsync() =>
        _outputChannel.ClearAsync(TimeSpan.FromSeconds(5));

    public Task<string> GetOutputAsync() =>
        _outputChannel.GetAllAsync(TimeSpan.FromSeconds(5));

    public Task<string> GetOutputIncrementalAsync() =>
        _outputChannel.GetIncrementalAsync(TimeSpan.FromSeconds(5));

    public Task<string> GetErrorAsync() =>
        _errorChannel.GetAllAsync(TimeSpan.FromSeconds(5));

    public Task<string> GetErrorIncrementalAsync() =>
        _errorChannel.GetIncrementalAsync(TimeSpan.FromSeconds(5));

    /// <summary>
    /// 等待stderr输出，直到满足条件或超时
    /// </summary>
    public async Task<string> WaitForErrorAsync(
        Func<string, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        timeout ??= TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            if (await _errorChannel.TryPredicateAsync(predicate, ct).ConfigureAwait(true))
            {
                return await _errorChannel.GetAllAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(true);
            }

            await WaitForErrorChangeAsync(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(true);
        }

        throw new TimeoutException($"[GEN051] 等待stderr输出超时 (>{timeout.Value.TotalSeconds}s), process={_pidTag}, running={IsRunning}");
    }

    /// <summary>
    /// 停止进程
    /// </summary>
    public async Task StopAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        _logger.LogInformation("[{PidTag}StdioManager] 停止进程", _pidTag);

        _readCts?.Cancel();

        // 先关闭 stdin，让子进程感知到输入结束
        _stdinWriter?.Close();

        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                using var killCts = new CancellationTokenSource(timeout.Value);
                await _process.WaitForExitAsync(killCts.Token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"停止进程时Kill失败: {ex.Message}");
            }
        }

        // 等待读取任务退出（进程已终止，管道会关闭）
        try
        {
            var stdoutTask = _stdoutReadTask;
            var stderrTask = _stderrReadTask;
            if (stdoutTask != null)
                await Task.WhenAny(stdoutTask, Task.Delay(timeout.Value)).ConfigureAwait(true);
            if (stderrTask != null)
                await Task.WhenAny(stderrTask, Task.Delay(timeout.Value)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"等待读取任务退出时异常: {ex.Message}");
        }

        _process?.Dispose();
        _readCts?.Dispose();
        _outputChannel.Dispose();
        _errorChannel.Dispose();

        _logger.LogInformation("[{PidTag}StdioManager] 进程已停止", _pidTag);
    }

    private async Task ReadStdoutAsync(CancellationToken ct)
    {
        if (_stdoutReader == null) return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _stdoutReader.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(30), ct)
                    .ConfigureAwait(true);
                if (line == null) break;

                await _outputChannel.AddAsync(line, ct).ConfigureAwait(true);
                _logger.LogTrace("[{PidTag}StdioManager] stdout: {Line}", _pidTag, line.Length > 200 ? line[..200] + "..." : line);
                SignalOutputChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (TimeoutException)
        {
            // 读取超时，视为管道关闭
            _logger.LogDebug("[{PidTag}StdioManager] stdout 读取超时，退出读取循环", _pidTag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{PidTag}StdioManager] 读取stdout时出错", _pidTag);
        }
    }

    private async Task ReadStderrAsync(CancellationToken ct)
    {
        if (_stderrReader == null) return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _stderrReader.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(30), ct)
                    .ConfigureAwait(true);
                if (line == null) break;

                await _errorChannel.AddAsync(line, ct).ConfigureAwait(true);
                _logger.LogTrace("[{PidTag}StdioManager] stderr: {Line}", _pidTag, line);
                SignalErrorChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (TimeoutException ex)
        {
            // 读取超时，视为管道关闭
            System.Diagnostics.Trace.WriteLine($"stderr读取超时，视为管道关闭: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{PidTag}StdioManager] 读取stderr时出错", _pidTag);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(true);
    }
    private void SignalOutputChanged()
    {
        var oldTcs = _outputChangedTcs;
        if (oldTcs.Task.IsCompleted) return;
        oldTcs.TrySetResult();
    }

    private void SignalErrorChanged()
    {
        var oldTcs = _errorChangedTcs;
        if (oldTcs.Task.IsCompleted) return;
        oldTcs.TrySetResult();
    }

    /// <summary>
    /// 等待输出变化 — 事件驱动替代轮询
    /// </summary>
    public async Task WaitForOutputChangeAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var currentTcs = _outputChangedTcs;
        if (currentTcs.Task.IsCompleted)
        {
            _outputChangedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return;
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await currentTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        _outputChangedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// 等待stderr变化 — 事件驱动替代轮询
    /// </summary>
    public async Task WaitForErrorChangeAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var currentTcs = _errorChangedTcs;
        if (currentTcs.Task.IsCompleted)
        {
            _errorChangedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return;
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await currentTcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        _errorChangedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>
/// STDIO进程配置
/// </summary>
public sealed record StdioProcessConfig
{
    public required string ExecutablePath { get; init; }
    public string Arguments { get; init; } = "";
    public Dictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>
    /// 进程工作目录 — null 时默认使用 exe 文件所在目录
    /// 用于 E2E 测试让 jcc 在有 .cs 文件的目录启动(如仓库根目录),使 CodeIndex 能扫描到真实代码
    /// </summary>
    public string? WorkingDirectory { get; init; }
}
