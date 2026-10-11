
namespace Core.Scheduling.Tasks;

/// <summary>
/// 本地 Shell 任务执行器接口 — 抽象 Bash 与 PowerShell 命令的本地执行能力
/// </summary>
public interface ILocalShellTaskExecutor {
    /// <summary>
    /// 异步执行本地 Bash Shell 任务
    /// </summary>
    /// <param name="definition">任务定义</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>智能体任务执行结果</returns>
    Task<AgentTaskResult> ExecuteShellAsync(LocalShellTaskDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// 异步执行本地 PowerShell 任务
    /// </summary>
    /// <param name="definition">任务定义</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>智能体任务执行结果</returns>
    Task<AgentTaskResult> ExecutePowerShellAsync(LocalShellTaskDefinition definition, CancellationToken ct = default);
}

/// <summary>
/// 本地 Shell 任务定义 — 描述一次本地命令执行所需的全部参数
/// </summary>
public sealed partial class LocalShellTaskDefinition {
    /// <summary>
    /// 任务 ID — 唯一标识本次任务执行
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// 要执行的命令文本
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// 工作目录路径,为 null 时使用当前目录
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 执行超时(毫秒),为 null 时使用默认超时
    /// </summary>
    public int? TimeoutMs { get; init; }

    /// <summary>
    /// 是否使用 PowerShell,为 false 时使用 Bash
    /// </summary>
    public bool UsePowerShell { get; init; }

    /// <summary>
    /// 环境变量表 — 执行前注入到进程环境
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; init; } = [];
}

/// <summary>
/// 本地 Shell 任务执行器 — 通过系统执行器注册表执行 Bash 或 PowerShell 命令,并记录遥测指标
/// </summary>
[Register(typeof(ILocalShellTaskExecutor), ServiceLifetime.Singleton)]
public sealed partial class LocalShellTaskExecutor : ServiceEntity, ILocalShellTaskExecutor {
    private readonly ISystemActuatorRegistry _actuatorRegistry;
    private readonly ILogger<LocalShellTaskExecutor>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;

    /// <summary>
    /// 初始化本地 Shell 任务执行器实例
    /// </summary>
    /// <param name="actuatorRegistry">系统执行器注册表</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="telemetryService">遥测服务</param>
    /// <param name="clock">时钟服务,用于计时</param>
    public LocalShellTaskExecutor(ISystemActuatorRegistry actuatorRegistry, ILogger<LocalShellTaskExecutor>? logger = null, ITelemetryService? telemetryService = null, IClockService? clock = null) {
        _actuatorRegistry = actuatorRegistry;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <inheritdoc/>
    public async Task<AgentTaskResult> ExecuteShellAsync(LocalShellTaskDefinition definition, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();

        try {
            _logger?.LogInformation("执行本地 Shell 任务: {TaskId}, 命令: {Command}", definition.TaskId, definition.Command);

            SetEnvironmentVariables(definition);
            var result = await _actuatorRegistry.Get(SystemActuatorKind.Bash).ExecuteAsync(definition.Command, definition.TimeoutMs, definition.WorkingDirectory, cancellationToken: ct).ConfigureAwait(false);

            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;

            if (result.Success) {
                var output = string.IsNullOrEmpty(result.Stderr)
                    ? result.Stdout
                    : $"{result.Stdout}\n[stderr] {result.Stderr}";

                RecordShellMetrics("shell", true);
                return AgentTaskResult.Success(definition.TaskId, "local-shell", output, elapsed);
            }

            var error = result.ErrorMessage ?? (result.Interrupted ? L.T(StringKey.CommandTimeout) : $"Exit code: {result.ExitCode}");
            RecordShellMetrics("shell", false);
            return AgentTaskResult.Failure(definition.TaskId, "local-shell", error, elapsed);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.LocalShellTaskFailedLog, definition.TaskId));
            return AgentTaskResult.Failure(definition.TaskId, "local-shell", ex.Message, elapsed);
        }
    }

    /// <inheritdoc/>
    public async Task<AgentTaskResult> ExecutePowerShellAsync(LocalShellTaskDefinition definition, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();

        try {
            _logger?.LogInformation(L.T(StringKey.LocalPowershellTaskStartLog, definition.TaskId, definition.Command));

            SetEnvironmentVariables(definition);
            var result = await _actuatorRegistry.Get(SystemActuatorKind.PowerShell).ExecuteAsync(definition.Command, definition.TimeoutMs, definition.WorkingDirectory, cancellationToken: ct).ConfigureAwait(false);

            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;

            if (result.Success) {
                var output = string.IsNullOrEmpty(result.Stderr)
                    ? result.Stdout
                    : $"{result.Stdout}\n[stderr] {result.Stderr}";

                RecordShellMetrics("powershell", true);
                return AgentTaskResult.Success(definition.TaskId, "local-powershell", output, elapsed);
            }

            var error = result.ErrorMessage ?? (result.Interrupted ? L.T(StringKey.CommandTimeout) : $"Exit code: {result.ExitCode}");
            RecordShellMetrics("powershell", false);
            return AgentTaskResult.Failure(definition.TaskId, "local-powershell", error, elapsed);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.LocalPowershellTaskFailedLog, definition.TaskId));
            return AgentTaskResult.Failure(definition.TaskId, "local-powershell", ex.Message, elapsed);
        }
    }

    private static void SetEnvironmentVariables(LocalShellTaskDefinition definition) {
        if (definition.EnvironmentVariables is null || definition.EnvironmentVariables.Count == 0) return;

        foreach (var (key, value) in definition.EnvironmentVariables) {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private void RecordShellMetrics(string shellType, bool isSuccess)
        => _telemetryService?.RecordCount("scheduling.shell.count", new Dictionary<string, string> { ["shell"] = shellType, ["success"] = isSuccess.ToString() }, "count", "Shell task execution count");
}