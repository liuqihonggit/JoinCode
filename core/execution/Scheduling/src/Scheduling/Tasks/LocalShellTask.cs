
namespace Core.Scheduling.Tasks;

public interface ILocalShellTaskExecutor
{
    Task<AgentTaskResult> ExecuteShellAsync(LocalShellTaskDefinition definition, CancellationToken ct = default);
    Task<AgentTaskResult> ExecutePowerShellAsync(LocalShellTaskDefinition definition, CancellationToken ct = default);
}

public sealed partial class LocalShellTaskDefinition
{
    public required string TaskId { get; init; }
    public required string Command { get; init; }
    public string? WorkingDirectory { get; init; }
    public int? TimeoutMs { get; init; }
    public bool UsePowerShell { get; init; }
    public Dictionary<string, string>? EnvironmentVariables { get; init; }
}

[Register]
public sealed partial class LocalShellTaskExecutor : ILocalShellTaskExecutor
{
    private readonly IShellExecutionService _shellExecutionService;
    [Inject] private readonly ILogger<LocalShellTaskExecutor>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;

    public LocalShellTaskExecutor(IShellExecutionService shellExecutionService, ILogger<LocalShellTaskExecutor>? logger = null, ITelemetryService? telemetryService = null, IClockService? clock = null)
    {
        _shellExecutionService = shellExecutionService;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task<AgentTaskResult> ExecuteShellAsync(LocalShellTaskDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();

        try
        {
            _logger?.LogInformation("执行本地 Shell 任务: {TaskId}, 命令: {Command}", definition.TaskId, definition.Command);

            SetEnvironmentVariables(definition);
            var result = await _shellExecutionService.ExecuteAsync(definition.Command, definition.TimeoutMs, definition.WorkingDirectory, cancellationToken: ct).ConfigureAwait(false);

            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;

            if (result.Success)
            {
                var output = string.IsNullOrEmpty(result.Stderr)
                    ? result.Stdout
                    : $"{result.Stdout}\n[stderr] {result.Stderr}";

                RecordShellMetrics("shell", true);
                return AgentTaskResult.Success(definition.TaskId, "local-shell", output, elapsed);
            }

            var error = result.ErrorMessage ?? (result.Interrupted ? L.T(StringKey.CommandTimeout) : $"Exit code: {result.ExitCode}");
            RecordShellMetrics("shell", false);
            return AgentTaskResult.Failure(definition.TaskId, "local-shell", error, elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.LocalShellTaskFailedLog, definition.TaskId));
            return AgentTaskResult.Failure(definition.TaskId, "local-shell", ex.Message, elapsed);
        }
    }

    public async Task<AgentTaskResult> ExecutePowerShellAsync(LocalShellTaskDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();

        try
        {
            _logger?.LogInformation(L.T(StringKey.LocalPowershellTaskStartLog, definition.TaskId, definition.Command));

            SetEnvironmentVariables(definition);
            var result = await _shellExecutionService.ExecutePowerShellAsync(definition.Command, definition.TimeoutMs, definition.WorkingDirectory, cancellationToken: ct).ConfigureAwait(false);

            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;

            if (result.Success)
            {
                var output = string.IsNullOrEmpty(result.Stderr)
                    ? result.Stdout
                    : $"{result.Stdout}\n[stderr] {result.Stderr}";

                RecordShellMetrics("powershell", true);
                return AgentTaskResult.Success(definition.TaskId, "local-powershell", output, elapsed);
            }

            var error = result.ErrorMessage ?? (result.Interrupted ? L.T(StringKey.CommandTimeout) : $"Exit code: {result.ExitCode}");
            RecordShellMetrics("powershell", false);
            return AgentTaskResult.Failure(definition.TaskId, "local-powershell", error, elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.LocalPowershellTaskFailedLog, definition.TaskId));
            return AgentTaskResult.Failure(definition.TaskId, "local-powershell", ex.Message, elapsed);
        }
    }

    private static void SetEnvironmentVariables(LocalShellTaskDefinition definition)
    {
        if (definition.EnvironmentVariables is null || definition.EnvironmentVariables.Count == 0) return;

        foreach (var (key, value) in definition.EnvironmentVariables)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private void RecordShellMetrics(string shellType, bool isSuccess)
        => _telemetryService?.RecordCount("scheduling.shell.count", new Dictionary<string, string> { ["shell"] = shellType, ["success"] = isSuccess.ToString() }, "count", "Shell task execution count");
}
