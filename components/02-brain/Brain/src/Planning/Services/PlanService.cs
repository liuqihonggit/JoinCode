
namespace Core.Planning;

[Register]
public partial class PlanService : IPlanService {
    private readonly IChatClient _kernel;
    private readonly IExceptionService _exceptionService;
    [Inject] private readonly ILogger<PlanService>? _logger;
    private readonly IClockService _clock;
    private readonly IToolCategoryProvider _toolCategoryProvider;
    private readonly ITelemetryService? _telemetryService;

    public PlanService(IChatClient kernel, IExceptionService exceptionService, IToolCategoryProvider toolCategoryProvider, ILogger<PlanService>? logger = null, ITelemetryService? telemetryService = null, IClockService? clock = null) {
        _kernel = kernel;
        _exceptionService = exceptionService;
        _logger = logger;
        _toolCategoryProvider = toolCategoryProvider;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task<string> ExecutePlanAsync(string userPrompt, CancellationToken cancellationToken = default) {
        var result = await ExecutePlanWithResultAsync(userPrompt, cancellationToken).ConfigureAwait(false);
        return result.Result;
    }

    public async Task<PlanExecutionResult> ExecutePlanWithResultAsync(string userPrompt, CancellationToken cancellationToken = default) {
        var stopwatch = Stopwatch.StartNew();
        var executionResult = new PlanExecutionResult {
            Prompt = userPrompt,
            Timestamp = _clock.GetUtcNow()
        };
        await using var span = _telemetryService?.StartSpan("plan.execute", TelemetrySpanKind.Server);
        span?.SetTag("plan.prompt_length", userPrompt.Length);

        try {
            _logger?.LogInformation("正在为以下请求创建并执行计划");

            var chatCompletionService = _kernel.GetChatCompletionService();

            var chatHistory = new MessageList();
            var planPrompt = PlanPrompts.BuildPlanExecutionSystemPrompt(_toolCategoryProvider.GetAvailableToolCategories());
            chatHistory.AddSystemMessage(planPrompt);
            chatHistory.AddUserMessage($"请为以下任务创建并执行计划:\n\n{userPrompt}");

            var executionSettings = new ChatOptions {
                Temperature = 0.7f,
                MaxTokens = 4000,
                TopP = 0.95f,
                ToolChoice = ToolChoice.AutoInvoke
            };

            var results = await chatCompletionService.GetApiMessageContentsAsync(
                chatHistory,
                executionSettings,
                _kernel,
                cancellationToken).ConfigureAwait(false);

            var result = results[0];

            stopwatch.Stop();

            executionResult.Success = true;
            executionResult.Result = result.Content ?? "计划执行未生成结果。";
            executionResult.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            if (result.TokenUsage != null) {
                executionResult.TokenUsage = result.TokenUsage;
            }

            span?.SetStatus(TelemetryStatusCode.Ok);
            span?.SetTag("plan.duration_ms", stopwatch.ElapsedMilliseconds);
            RecordPlanMetrics(stopwatch.ElapsedMilliseconds, isSuccess: true);

            _logger?.LogInformation("计划在 {ElapsedMs}ms 内成功执行", executionResult.ExecutionTimeMs);

            return executionResult;
        } catch (OperationCanceledException) {
            stopwatch.Stop();
            executionResult.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            executionResult.Success = false;
            executionResult.Error = "计划执行已取消";
            _logger?.LogWarning("计划执行已取消");
            span?.SetStatus(TelemetryStatusCode.Error, "Cancelled");
            RecordPlanMetrics(stopwatch.ElapsedMilliseconds, isSuccess: false);
            throw;
        } catch (Exception ex) {
            stopwatch.Stop();
            executionResult.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            executionResult.Success = false;
            executionResult.Error = ex.Message;
            _logger?.LogError(ex, "执行计划时出错");
            span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            span?.RecordException(ex);
            RecordPlanMetrics(stopwatch.ElapsedMilliseconds, isSuccess: false);
            throw new WorkflowException("执行计划失败", ex);
        }
    }

    private void RecordPlanMetrics(long durationMs, bool isSuccess)
    {
        _telemetryService?.RecordCount("plan.execute.count", new() { ["success"] = isSuccess.ToString() }, "count", "Plan execution count");
        _telemetryService?.RecordHistogram("plan.execute.duration", durationMs, new() { ["success"] = isSuccess.ToString() }, "ms", "Plan execution duration");
    }
}
