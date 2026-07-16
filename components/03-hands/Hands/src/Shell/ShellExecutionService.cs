
using Services.Shell.Providers;

namespace Services.Shell;

/// <summary>
/// Shell 执行服务实现 — 使用 IShellProvider 替代硬编码的 cmd.exe/powershell.exe
/// </summary>
[Register]
public sealed partial class ShellExecutionService : IShellExecutionService
{
    [Inject] private readonly ILogger<ShellExecutionService>? _logger;
    private readonly ShellExecutionConfig _config;
    private readonly IFileSystem _fs;
    private readonly ISandboxModeService? _sandboxModeService;
    private readonly IPreventSleepService? _preventSleepService;
    private readonly IShellProvider _bashProvider;
    private readonly IShellProvider _powerShellProvider;

    public ShellExecutionService(
        ShellExecutionConfig config,
        IFileSystem fs,
        BashShellProvider bashProvider,
        PowerShellShellProvider powerShellProvider,
        ILogger<ShellExecutionService>? logger = null,
        ISandboxModeService? sandboxModeService = null,
        IPreventSleepService? preventSleepService = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _bashProvider = bashProvider ?? throw new ArgumentNullException(nameof(bashProvider));
        _powerShellProvider = powerShellProvider ?? throw new ArgumentNullException(nameof(powerShellProvider));
        _logger = logger;
        _sandboxModeService = sandboxModeService;
        _preventSleepService = preventSleepService;
    }

    /// <inheritdoc />
    public Task<ShellExecutionResult> ExecuteAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(command, timeout, workingDirectory, disableSandbox, _bashProvider,
            "Bash", cancellationToken);

    /// <inheritdoc />
    public Task<ShellExecutionResult> ExecutePowerShellAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(command, timeout, workingDirectory, disableSandbox, _powerShellProvider,
            "PowerShell", cancellationToken);

    /// <inheritdoc />
    public async Task<IShellCommandContext> StartWithBackgroundSupportAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool isPowerShell = false,
        bool shouldAutoBackground = true,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command cannot be empty", nameof(command));
        }

        var cwd = ResolveWorkingDirectory(workingDirectory, disableSandbox);

        // 对齐 TS Shell.ts: CWD 不存在时回退到项目根目录而非抛异常
        if (!_fs.DirectoryExists(cwd))
        {
            _logger?.LogWarning("工作目录不存在: {Cwd}，回退到项目根目录", cwd);
            cwd = _fs.GetCurrentDirectory();
            if (!_fs.DirectoryExists(cwd))
            {
                throw new DirectoryNotFoundException($"Working directory does not exist: {cwd}");
            }
        }

        var provider = isPowerShell ? _powerShellProvider : _bashProvider;

        _logger?.LogInformation("Starting backgroundable command with {ProviderType}: {Command}", provider.Type, command);

        if (_preventSleepService is not null) await _preventSleepService.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(false);

        var useSandbox = !disableSandbox && _sandboxModeService is not null && _sandboxModeService.IsInSandbox;
        var sandboxTmpDir = useSandbox ? (_sandboxModeService ?? throw new InvalidOperationException("SandboxModeService not available.")).CurrentSandbox?.RootPath : null;

        var context = await ShellCommandContext.StartAsync(
            command,
            cwd,
            _fs,
            provider,
            timeout,
            shouldAutoBackground,
            useSandbox,
            sandboxTmpDir,
            _logger).ConfigureAwait(false);

        _ = context.ResultTask.ContinueWith(async _ =>
        {
            if (_preventSleepService is not null) await _preventSleepService.AllowSleepAsync().ConfigureAwait(false);
        }, TaskScheduler.Default);

        return context;
    }

    #region Core Execution

    /// <summary>
    /// 统一执行核心 — 对齐 TS Shell.ts，所有路径走 ShellCommandContext
    /// 消除双路径（IProcessService vs ShellCommandContext），统一环境变量清理、大输出持久化、CWD 追踪
    /// </summary>
    private async Task<ShellExecutionResult> ExecuteCoreAsync(
        string command,
        int? timeout,
        string? workingDirectory,
        bool disableSandbox,
        IShellProvider provider,
        string shellLabel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return ShellExecutionResult.FailureResult("Command cannot be empty");
        }

        var cwd = ResolveWorkingDirectory(workingDirectory, disableSandbox);

        if (!_fs.DirectoryExists(cwd))
        {
            _logger?.LogWarning("工作目录不存在: {Cwd}，回退到项目根目录", cwd);
            cwd = _fs.GetCurrentDirectory();
            if (!_fs.DirectoryExists(cwd))
            {
                return ShellExecutionResult.FailureResult($"Working directory does not exist: {cwd}");
            }
        }

        _logger?.LogInformation("Executing {Shell} command: {Command}", shellLabel, command);

        if (_preventSleepService is not null) await _preventSleepService.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(false);
        try
        {
            var useSandbox = !disableSandbox && _sandboxModeService is not null && _sandboxModeService.IsInSandbox;
            var sandboxTmpDir = useSandbox ? (_sandboxModeService ?? throw new InvalidOperationException("SandboxModeService not available.")).CurrentSandbox?.RootPath : null;

            await using var context = await ShellCommandContext.StartAsync(
                command, cwd, _fs, provider, timeout,
                shouldAutoBackground: false, useSandbox, sandboxTmpDir, _logger).ConfigureAwait(false);

            var result = await context.ResultTask.ConfigureAwait(false);

            _logger?.LogInformation(
                "{LogLabel} completed: ExitCode={ExitCode}, StdoutLength={StdoutLength}, StderrLength={StderrLength}",
                shellLabel, result.ExitCode, result.Stdout?.Length ?? 0, result.Stderr?.Length ?? 0);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{LogLabel} execution failed: {Command}", shellLabel, command);
            return ShellExecutionResult.FailureResult(ex.Message);
        }
        finally
        {
            if (_preventSleepService is not null) await _preventSleepService.AllowSleepAsync().ConfigureAwait(false);
        }
    }

    private string ResolveWorkingDirectory(string? workingDirectory, bool disableSandbox)
    {
        var cwd = string.IsNullOrEmpty(workingDirectory)
            ? _fs.GetCurrentDirectory()
            : Path.GetFullPath(workingDirectory);

        if (!disableSandbox && _sandboxModeService != null && _sandboxModeService.IsInSandbox)
        {
            cwd = _sandboxModeService.ResolvePath(cwd);
        }

        return cwd;
    }

    #endregion
}
