
namespace Core.Scheduling.Tasks;

/// <summary>
/// 远程智能体任务执行器接口 — 定义远程任务执行、可用性检查与取消能力
/// </summary>
public interface IRemoteAgentTaskExecutor
{
    /// <summary>
    /// 异步执行远程智能体任务
    /// </summary>
    /// <param name="definition">远程任务定义</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务执行结果</returns>
    Task<AgentTaskResult> ExecuteRemoteAsync(RemoteAgentTaskDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// 异步检查远程端点是否可用
    /// </summary>
    /// <param name="endpoint">远程端点地址</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>可用返回 true,否则返回 false</returns>
    Task<bool> IsRemoteAvailableAsync(string endpoint, CancellationToken ct = default);

    /// <summary>
    /// 异步取消远程任务
    /// </summary>
    /// <param name="taskId">要取消的任务Id</param>
    /// <param name="ct">取消令牌</param>
    Task CancelRemoteAsync(string taskId, CancellationToken ct = default);
}

/// <summary>
/// 远程智能体任务定义 — 描述一次远程执行的请求参数
/// </summary>
public sealed partial class RemoteAgentTaskDefinition
{
    /// <summary>任务唯一标识</summary>
    public required string TaskId { get; init; }
    /// <summary>远程端点地址</summary>
    public required string Endpoint { get; init; }
    /// <summary>任务描述</summary>
    public required string TaskDescription { get; init; }
    /// <summary>系统提示词,为空时使用远程端默认值</summary>
    public string? SystemPrompt { get; init; }
    /// <summary>附加请求头字典</summary>
    public Dictionary<string, string> Headers { get; init; } = [];
    /// <summary>执行超时时长,为空时使用远程端默认值</summary>
    public TimeSpan? Timeout { get; init; }
    /// <summary>最大重试次数,默认 3 次</summary>
    public int MaxRetries { get; init; } = 3;
}

/// <summary>
/// 远程智能体任务执行器 — 通过 HTTP 调用远程端点执行任务,支持重试与遥测
/// </summary>
[Register(typeof(IRemoteAgentTaskExecutor), ServiceLifetime.Singleton)]
public sealed partial class RemoteAgentTaskExecutor : ServiceEntity, IRemoteAgentTaskExecutor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RemoteAgentTaskExecutor>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;

    /// <summary>
    /// 初始化远程智能体任务执行器
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="logger">日志记录器,为空时不记录日志</param>
    /// <param name="telemetryService">遥测服务,为空时不记录遥测指标</param>
    /// <param name="clock">时钟服务,为空时使用系统默认时钟</param>
    public RemoteAgentTaskExecutor(HttpClient httpClient, ILogger<RemoteAgentTaskExecutor>? logger = null, ITelemetryService? telemetryService = null, IClockService? clock = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <inheritdoc/>
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
                var result = RelaxedJsonSerializer.Deserialize(json, SchedulingTasksJsonContext.Default.RemoteAgentExecuteResponse);

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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

/// <summary>
/// 远程智能体执行请求体 — 序列化后发送到远程端点
/// </summary>
public sealed partial class RemoteAgentExecuteRequest
{
    /// <summary>任务唯一标识</summary>
    public required string TaskId { get; init; }
    /// <summary>任务描述</summary>
    public required string TaskDescription { get; init; }
    /// <summary>系统提示词,为空时使用远程端默认值</summary>
    public string? SystemPrompt { get; init; }
    /// <summary>执行超时时长,为空时使用远程端默认值</summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// 远程智能体执行响应体 — 从远程端点反序列化的执行结果
/// </summary>
public sealed partial class RemoteAgentExecuteResponse
{
    /// <summary>执行是否成功</summary>
    public bool Success { get; init; }
    /// <summary>执行输出内容,失败时为空</summary>
    public string? Output { get; init; }
    /// <summary>错误信息,成功时为空</summary>
    public string? Error { get; init; }
}
