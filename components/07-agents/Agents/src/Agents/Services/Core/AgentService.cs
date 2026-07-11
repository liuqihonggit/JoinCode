
namespace Core.Agents;

public partial class AgentService : IAgent
{
    private readonly IChatClient _kernel;
    private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<AgentService>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly WorkflowConfig _config;
    private readonly ITelemetryService? _telemetryService;

    public AgentService(
        IChatClient kernel,
        IChatContextManager contextManager,
        WorkflowConfig config,
        ILogger<AgentService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
    {
        _kernel = kernel;
        _contextManager = contextManager;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _config = config;
        _telemetryService = telemetryService;
    }

    public async Task<AgentResponse> ProcessAsync(
        string userInput,
        bool useTools = false,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new AgentResponse();
        var span = _telemetryService?.StartSpan("agent.process", TelemetrySpanKind.Server);
        span?.SetTag("agent.use_tools", useTools);

        try
        {
            _logger?.LogInformation("Agent 处理用户输入: {Input}, 使用工具: {UseTools}", userInput, useTools);

            await _contextManager.AddUserMessageAsync(userInput, cancellationToken).ConfigureAwait(false);

            var chatCompletionService = _kernel.GetChatCompletionService();
            var chatHistory = await _contextManager.GetMessageListAsync(cancellationToken).ConfigureAwait(false);

            var executionSettings = new ChatOptions
            {
                Temperature = (float)_config.LlmExecution.Temperature,
                MaxTokens = _config.LlmExecution.MaxTokens,
                TopP = (float)_config.LlmExecution.TopP,
                FrequencyPenalty = (float)_config.LlmExecution.FrequencyPenalty,
                PresencePenalty = (float)_config.LlmExecution.PresencePenalty,
                ToolChoice = useTools ? ToolChoice.AutoInvoke : ToolChoice.None
            };

            var results = await chatCompletionService.GetApiMessageContentsAsync(
                chatHistory,
                executionSettings,
                _kernel,
                cancellationToken).ConfigureAwait(false);

            var result = results[0];

            stopwatch.Stop();

            response.Content = result.Content ?? "抱歉，我无法生成回复。";
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            if (result.TokenUsage != null)
            {
                response.TokenUsage = result.TokenUsage;
            }

            await _contextManager.AddAssistantMessageAsync(response.Content, cancellationToken).ConfigureAwait(false);
            await _contextManager.SaveContextAsync(cancellationToken).ConfigureAwait(false);

            span?.SetStatus(TelemetryStatusCode.Ok);
            span?.SetTag("agent.duration_ms", stopwatch.ElapsedMilliseconds);
            span?.Dispose();
            RecordAgentMetrics(stopwatch.ElapsedMilliseconds, isSuccess: true);

            _logger?.LogInformation("Agent 响应生成完成，耗时 {ElapsedMs}ms", response.ExecutionTimeMs);

            return response;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Agent 处理已取消");
            span?.SetStatus(TelemetryStatusCode.Error, "Cancelled");
            span?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent 处理时出错");
            response.Content = $"处理时出错: {ex.Message}";
            span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            span?.RecordException(ex);
            span?.Dispose();
            RecordAgentMetrics(stopwatch.ElapsedMilliseconds, isSuccess: false);
            return response;
        }
    }

    private void RecordAgentMetrics(long durationMs, bool isSuccess)
    {
        var tags = new Dictionary<string, string> { ["success"] = isSuccess.ToString() };
        _telemetryService?.RecordCount("agent.process.count", tags, "count", "Agent process count");
        _telemetryService?.RecordHistogram("agent.process.duration", durationMs, tags, "ms", "Agent process duration");
    }

    public async Task ClearContextAsync(CancellationToken cancellationToken = default)
    {
        await _contextManager.ClearMessagesAsync(cancellationToken).ConfigureAwait(false);

        await _contextManager.AddSystemMessageAsync("You are an AI assistant.", cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Agent 上下文已清空");
    }

    public async Task<AgentContext> GetContextAsync(CancellationToken cancellationToken = default)
    {
        var chatHistory = await _contextManager.GetMessageListAsync(cancellationToken).ConfigureAwait(false);

        var messages = chatHistory
            .Select(message => new ContractAgentMessage
            {
                Role = message.Role.ToValue() ?? message.Role.ToString().ToLowerInvariant(),
                Content = message.Content ?? string.Empty,
                Timestamp = _clock.GetUtcNow()
            })
            .ToList();

        return new AgentContext
        {
            Messages = messages,
            StartedAt = _clock.GetUtcNow()
        };
    }
}
