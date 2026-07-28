
namespace Core.Scheduling.Tasks;

public interface IRemoteAgentTaskExecutor
{
    Task<AgentTaskResult> ExecuteRemoteAsync(RemoteAgentTaskDefinition definition, CancellationToken ct = default);
    Task<bool> IsRemoteAvailableAsync(string endpoint, CancellationToken ct = default);
    Task CancelRemoteAsync(string taskId, CancellationToken ct = default);
}

public sealed partial class RemoteAgentTaskDefinition
{
    public required string TaskId { get; init; }
    public required string Endpoint { get; init; }
    public required string TaskDescription { get; init; }
    public string? SystemPrompt { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public TimeSpan? Timeout { get; init; }
    public int MaxRetries { get; init; } = 3;
}

[Register]
public sealed partial class RemoteAgentTaskExecutor : IRemoteAgentTaskExecutor
{
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<RemoteAgentTaskExecutor>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;

    public RemoteAgentTaskExecutor(HttpClient httpClient, ILogger<RemoteAgentTaskExecutor>? logger = null, ITelemetryService? telemetryService = null, IClockService? clock = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task<AgentTaskResult> ExecuteRemoteAsync(RemoteAgentTaskDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();
        var remainingRetries = definition.MaxRetries;

        while (remainingRetries >= 0)
        {
            try
            {
                var request = BuildExecuteRequest(definition);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var result = JsonSerializer.Deserialize(json, SchedulingTasksJsonContext.Default.RemoteAgentExecuteResponse);

                if (result is null)
                {
                    return AgentTaskResult.Failure(definition.TaskId, "remote", "Failed to deserialize remote response");
                }

                var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
                RecordRemoteMetrics("execute", result.Success);
                return result.Success
                    ? AgentTaskResult.Success(definition.TaskId, "remote", result.Output ?? string.Empty, elapsed)
                    : AgentTaskResult.Failure(definition.TaskId, "remote", result.Error ?? "Remote execution failed", elapsed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (remainingRetries > 0)
            {
                _logger?.LogWarning(ex, "Remote agent task {TaskId} failed, retrying ({RetriesLeft} left)", definition.TaskId, remainingRetries);
                remainingRetries--;
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (definition.MaxRetries - remainingRetries)), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
                RecordRemoteMetrics("execute", false);
                return AgentTaskResult.Failure(definition.TaskId, "remote", ex.Message, elapsed);
            }
        }

        RecordRemoteMetrics("execute", false);
        return AgentTaskResult.Failure(definition.TaskId, "remote", "Max retries exceeded");
    }

    public async Task<bool> IsRemoteAvailableAsync(string endpoint, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{endpoint.TrimEnd('/')}/api/agent/health", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task CancelRemoteAsync(string taskId, CancellationToken ct = default)
    {
        try
        {
            await _httpClient.DeleteAsync($"/api/agent/cancel/{taskId}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to cancel remote task {TaskId}", taskId);
        }
    }

    private HttpRequestMessage BuildExecuteRequest(RemoteAgentTaskDefinition definition)
    {
        var payload = new RemoteAgentExecuteRequest
        {
            TaskId = definition.TaskId,
            TaskDescription = definition.TaskDescription,
            SystemPrompt = definition.SystemPrompt,
            Timeout = definition.Timeout
        };

        var json = JsonSerializer.Serialize(payload, SchedulingTasksJsonContext.Default.RemoteAgentExecuteRequest);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{definition.Endpoint.TrimEnd('/')}/api/agent/execute")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (definition.Headers is not null)
        {
            foreach (var (key, value) in definition.Headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        return request;
    }

    private void RecordRemoteMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("scheduling.remote.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Remote agent task execution count");
}

public sealed partial class RemoteAgentExecuteRequest
{
    public required string TaskId { get; init; }
    public required string TaskDescription { get; init; }
    public string? SystemPrompt { get; init; }
    public TimeSpan? Timeout { get; init; }
}

public sealed partial class RemoteAgentExecuteResponse
{
    public bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
}
