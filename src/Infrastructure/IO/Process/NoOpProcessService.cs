namespace IO.ProcessService;

/// <summary>
/// NoOp 进程服务 — 跳过所有进程操作，用于测试/E2E 环境
/// 通过 JCC_PROCESS_MODE=NoOp 环境变量激活
/// </summary>
public sealed class NoOpProcessService : IProcessService
{
    private readonly ILogger<NoOpProcessService>? _logger;

    public NoOpProcessService(ILogger<NoOpProcessService>? logger = null)
    {
        _logger = logger;
    }

    public Task<ProcessResult> ExecuteAsync(ProcessOptions options, CancellationToken ct = default)
    {
        _logger?.LogDebug("[NoOp] 跳过进程执行: {FileName} {Arguments}", options.FileName, options.Arguments);
        return Task.FromResult(new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = string.Empty,
            StandardError = string.Empty,
            ExecutionTime = TimeSpan.Zero
        });
    }

    public Task<IInteractiveProcess> StartInteractiveAsync(InteractiveProcessOptions options, CancellationToken ct = default)
    {
        _logger?.LogDebug("[NoOp] 跳过交互式进程启动: {FileName}", options.FileName);
        return Task.FromResult<IInteractiveProcess>(new NoOpInteractiveProcess());
    }

    public Task<bool> OpenAsync(string path, CancellationToken ct = default)
    {
        _logger?.LogDebug("[NoOp] 跳过打开: {Path}", path);
        return Task.FromResult(true);
    }

    public Task<string?> FindExecutableAsync(string name, CancellationToken ct = default)
    {
        _logger?.LogDebug("[NoOp] 跳过可执行文件查找: {Name}", name);
        return Task.FromResult<string?>(null);
    }

    public bool IsProcessRunning(string processName)
    {
        _logger?.LogDebug("[NoOp] 跳过进程检查: {ProcessName}", processName);
        return false;
    }

    private sealed class NoOpInteractiveProcess : IInteractiveProcess
    {
        public StreamWriter StandardInput => throw new InvalidOperationException("NoOp 进程不支持标准输入");
        public StreamReader StandardOutput => throw new InvalidOperationException("NoOp 进程不支持标准输出");
        public int Id => -1;
        public bool HasExited => true;
        public int ExitCode => 0;

        public event EventHandler<string>? ErrorDataReceived { add { } remove { } }

        public void Kill() { }
        public Task WaitForExitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
