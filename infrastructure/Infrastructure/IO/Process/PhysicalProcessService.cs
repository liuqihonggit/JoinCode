namespace IO.ProcessService;

/// <summary>
/// 物理进程服务 — 委托给 System.Diagnostics.Process
/// 内部强制先读 stdout/stderr 再 WaitForExit，消除 JCC3003 死锁风险
/// </summary>
public sealed class PhysicalProcessService : IProcessService
{
    private readonly ILogger<PhysicalProcessService>? _logger;

    public PhysicalProcessService(ILogger<PhysicalProcessService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ProcessResult> ExecuteAsync(ProcessOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var psi = new ProcessStartInfo
        {
            FileName = options.FileName,
            Arguments = options.Arguments,
            WorkingDirectory = options.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = options.RedirectStandardOutput,
            RedirectStandardError = options.RedirectStandardError
        };

        if (options.StandardOutputEncoding != null) psi.StandardOutputEncoding = options.StandardOutputEncoding;
        if (options.StandardErrorEncoding != null) psi.StandardErrorEncoding = options.StandardErrorEncoding;

        if (options.EnvironmentVariables != null)
        {
            foreach (var (key, value) in options.EnvironmentVariables)
                psi.EnvironmentVariables[key] = value;
        }

        _logger?.LogDebug("[Process] 执行: {FileName} {Arguments}", options.FileName, options.Arguments);

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动进程: {options.FileName}");

        var sw = Stopwatch.StartNew();

        var stdoutTask = options.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync(ct)
            : Task.FromResult(string.Empty);
        var stderrTask = options.RedirectStandardError
            ? process.StandardError.ReadToEndAsync(ct)
            : Task.FromResult(string.Empty);

        if (options.TimeoutMs is > 0)
        {
            using var cts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromMilliseconds(options.TimeoutMs.Value));
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                process.Kill();
                return new ProcessResult
                {
                    ExitCode = -1,
                    StandardOutput = string.Empty,
                    StandardError = "进程执行超时",
                    ExecutionTime = sw.Elapsed
                };
            }
        }
        else
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await stdoutTask.ConfigureAwait(false),
            StandardError = await stderrTask.ConfigureAwait(false),
            ExecutionTime = sw.Elapsed
        };
    }

    public Task<IInteractiveProcess> StartInteractiveAsync(InteractiveProcessOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var psi = new ProcessStartInfo
        {
            FileName = options.FileName,
            Arguments = options.Arguments,
            WorkingDirectory = options.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = options.RedirectStandardError
        };

        if (options.StandardOutputEncoding != null) psi.StandardOutputEncoding = options.StandardOutputEncoding;
        if (options.StandardErrorEncoding != null) psi.StandardErrorEncoding = options.StandardErrorEncoding;
        if (options.StandardInputEncoding != null) psi.StandardInputEncoding = options.StandardInputEncoding;

        if (options.EnvironmentVariables != null)
        {
            foreach (var (key, value) in options.EnvironmentVariables)
                psi.EnvironmentVariables[key] = value;
        }

        _logger?.LogDebug("[Process] 启动交互式进程: {FileName} {Arguments}", options.FileName, options.Arguments);

        var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动交互式进程: {options.FileName}");

        if (options.RedirectStandardError)
            process.BeginErrorReadLine();

        var interactive = new PhysicalInteractiveProcess(process, _logger);
        return Task.FromResult<IInteractiveProcess>(interactive);
    }

    public async Task<bool> OpenAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var process = System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return process != null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Process] 打开失败: {Path}", path);
            return false;
        }
    }

    public async Task<string?> FindExecutableAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var cmd = OperatingSystem.IsWindows() ? "where" : "which";
        try
        {
            var result = await ExecuteAsync(new ProcessOptions
            {
                FileName = cmd,
                Arguments = name,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                TimeoutMs = 5000
            }, ct).ConfigureAwait(false);

            if (!result.Success) return null;

            var output = result.StandardOutput.Trim();

            if (OperatingSystem.IsWindows())
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!trimmed.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                        return trimmed;
                }
                return lines.Length > 0 ? lines[0].Trim() : null;
            }

            return string.IsNullOrWhiteSpace(output) ? null : output.Split('\n')[0].Trim();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[Process] 查找可执行文件失败: {Name}", name);
            return null;
        }
    }

    public bool IsProcessRunning(string processName)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName(processName).Length > 0;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[Process] 检查进程运行状态失败: {ProcessName}", processName);
            return false;
        }
    }

    private sealed class PhysicalInteractiveProcess : IInteractiveProcess
    {
        private readonly System.Diagnostics.Process _process;
        private readonly ILogger? _logger;

        public PhysicalInteractiveProcess(System.Diagnostics.Process process, ILogger? logger)
        {
            _process = process;
            _logger = logger;
            StandardInput = new StreamWriter(_process.StandardInput.BaseStream) { AutoFlush = true };
            StandardOutput = new StreamReader(_process.StandardOutput.BaseStream);
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) ErrorDataReceived?.Invoke(this, e.Data);
            };
        }

        public StreamWriter StandardInput { get; }
        public StreamReader StandardOutput { get; }
        public int Id => _process.Id;

        public bool HasExited
        {
            get
            {
                try { return _process.HasExited; }
                catch { return true; }
            }
        }

        public int ExitCode => _process.HasExited ? _process.ExitCode : -1;

        public event EventHandler<string>? ErrorDataReceived;

        public Task WaitForExitAsync(CancellationToken ct = default)
            => _process.WaitForExitAsync(ct);

        public void Kill()
        {
            try { _process.Kill(); }
            catch (Exception ex) { _logger?.LogDebug(ex, "[Process] 终止进程失败: PID={Id}", _process.Id); }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill();
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "[Process] Dispose时终止进程失败: PID={Id}", _process.Id); }

            StandardInput.Dispose();
            StandardOutput.Dispose();
            _process.Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
