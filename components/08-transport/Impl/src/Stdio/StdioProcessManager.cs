using System.Text;

namespace JoinCode.Transport;

/// <summary>
/// 管理CLI进程的stdin/stdout通信
/// 实现真实的双向对话
/// </summary>
public sealed partial class StdioProcessManager : IAsyncDisposable
{
    private System.Diagnostics.Process? _process;
    private StreamWriter? _stdinWriter;
    private StreamReader? _stdoutReader;
    private StreamReader? _stderrReader;
    [Inject] private readonly ILogger<StdioProcessManager>? _logger;
    private readonly List<string> _outputBuffer = new();
    private readonly List<string> _errorBuffer = new();
    private readonly SemaphoreSlim _outputLock = new(1, 1);
    private readonly SemaphoreSlim _errorLock = new(1, 1);
    private int _outputConsumedIndex;
    private int _errorConsumedIndex;
    private CancellationTokenSource? _readCts;
    private Task? _stdoutReadTask;
    private Task? _stderrReadTask;

    private readonly IClockService _clock;

    public StdioProcessManager(ILogger<StdioProcessManager>? logger = null, IClockService? clock = null)
    {
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
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
        _logger?.LogInformation("[StdioManager] 启动进程: {Path} {Args}", config.ExecutablePath, config.Arguments);

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
            WorkingDirectory = config.WorkingDirectory ?? Path.GetDirectoryName(config.ExecutablePath) ?? Environment.CurrentDirectory
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

        _stdinWriter = _process.StandardInput;
        _stdoutReader = _process.StandardOutput;
        _stderrReader = _process.StandardError;

        _readCts = new CancellationTokenSource();
        _stdoutReadTask = Task.Run(() => ReadStdoutAsync(_readCts.Token), ct);
        _stderrReadTask = Task.Run(() => ReadStderrAsync(_readCts.Token), ct);

        await Task.Delay(500, ct).ConfigureAwait(false);

        _logger?.LogInformation("[StdioManager] 进程已启动, PID: {Pid}", _process.Id);
    }

    /// <summary>
    /// 向进程发送消息
    /// </summary>
    public async Task SendAsync(string message, CancellationToken ct = default)
    {
        if (_stdinWriter == null)
            throw new InvalidOperationException("进程未启动");

        _logger?.LogDebug("[StdioManager] 发送: {Message}", message.Length > 100 ? message[..100] + "..." : message);

        await _stdinWriter.WriteLineAsync(message.AsMemory(), ct).ConfigureAwait(false);
        await _stdinWriter.FlushAsync(ct).ConfigureAwait(false);
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
        var startTime = _clock.GetUtcNow();

        while (_clock.GetUtcNow() - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            await _outputLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var currentOutput = string.Join("\n", _outputBuffer);
                if (predicate(currentOutput))
                {
                    return currentOutput;
                }
            }
            finally
            {
                _outputLock.Release();
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待输出超时 (>{timeout.Value.TotalSeconds}s)");
    }

    /// <summary>
    /// 清空输出缓冲区
    /// </summary>
    public async Task ClearOutputAsync()
    {
        await _outputLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        try
        {
            _outputBuffer.Clear();
            _outputConsumedIndex = 0;
        }
        finally
        {
            _outputLock.Release();
        }
    }

    /// <summary>
    /// 获取当前所有输出
    /// </summary>
    public async Task<string> GetOutputAsync()
    {
        await _outputLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        try
        {
            return string.Join("\n", _outputBuffer);
        }
        finally
        {
            _outputLock.Release();
        }
    }

    /// <summary>
    /// 获取自上次调用以来的增量输出 — 避免全量拼接导致大日志超时
    /// </summary>
    public async Task<string> GetOutputIncrementalAsync()
    {
        await _outputLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        try
        {
            if (_outputConsumedIndex >= _outputBuffer.Count)
                return string.Empty;

            var result = string.Join("\n", _outputBuffer[_outputConsumedIndex..]);
            _outputConsumedIndex = _outputBuffer.Count;
            return result;
        }
        finally
        {
            _outputLock.Release();
        }
    }

    /// <summary>
    /// 获取当前所有stderr输出
    /// </summary>
    public async Task<string> GetErrorAsync()
    {
        await _errorLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        try
        {
            return string.Join("\n", _errorBuffer);
        }
        finally
        {
            _errorLock.Release();
        }
    }

    /// <summary>
    /// 获取自上次调用以来的增量stderr输出 — 避免全量拼接导致大日志超时
    /// </summary>
    public async Task<string> GetErrorIncrementalAsync()
    {
        await _errorLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        try
        {
            if (_errorConsumedIndex >= _errorBuffer.Count)
                return string.Empty;

            var result = string.Join("\n", _errorBuffer[_errorConsumedIndex..]);
            _errorConsumedIndex = _errorBuffer.Count;
            return result;
        }
        finally
        {
            _errorLock.Release();
        }
    }

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
        var startTime = _clock.GetUtcNow();

        while (_clock.GetUtcNow() - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            await _errorLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var currentError = string.Join("\n", _errorBuffer);
                if (predicate(currentError))
                {
                    return currentError;
                }
            }
            finally
            {
                _errorLock.Release();
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待stderr输出超时 (>{timeout.Value.TotalSeconds}s)");
    }

    /// <summary>
    /// 停止进程
    /// </summary>
    public async Task StopAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        _logger?.LogInformation("[StdioManager] 停止进程");

        _readCts?.Cancel();

        _stdinWriter?.Close();

        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                using var killCts = new CancellationTokenSource(timeout.Value);
                await _process.WaitForExitAsync(killCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"停止进程时Kill失败: {ex.Message}");
            }
        }

        try
        {
            var stdoutTask = _stdoutReadTask;
            var stderrTask = _stderrReadTask;
            if (stdoutTask != null)
                await Task.WhenAny(stdoutTask, Task.Delay(timeout.Value)).ConfigureAwait(false);
            if (stderrTask != null)
                await Task.WhenAny(stderrTask, Task.Delay(timeout.Value)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"等待读取任务退出时异常: {ex.Message}");
        }

        _process?.Dispose();
        _readCts?.Dispose();
        _outputLock.Dispose();
        _errorLock.Dispose();

        _logger?.LogInformation("[StdioManager] 进程已停止");
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
                    .ConfigureAwait(false);
                if (line == null) break;

                await _outputLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    _outputBuffer.Add(line);
                    _logger?.LogTrace("[StdioManager] stdout: {Line}", line.Length > 200 ? line[..200] + "..." : line);
                }
                finally
                {
                    _outputLock.Release();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException)
        {
            _logger?.LogDebug("[StdioManager] stdout 读取超时，退出读取循环");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[StdioManager] 读取stdout时出错");
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
                    .ConfigureAwait(false);
                if (line == null) break;

                await _errorLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    _errorBuffer.Add(line);
                    _logger?.LogTrace("[StdioManager] stderr: {Line}", line);
                }
                finally
                {
                    _errorLock.Release();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Debug.WriteLine($"stderr读取超时，视为管道关闭: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[StdioManager] 读取stderr时出错");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// STDIO进程配置
/// </summary>
public sealed record StdioProcessConfig
{
    public required string ExecutablePath { get; init; }
    public string Arguments { get; init; } = "";
    public string? WorkingDirectory { get; init; }
    public Dictionary<string, string>? EnvironmentVariables { get; init; }
}
