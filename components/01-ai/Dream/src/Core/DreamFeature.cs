
namespace JoinCode.Dream;

using JoinCode.Dream.Pipeline;

/// <summary>
/// 做梦功能实现 - 记忆整合功能
/// </summary>
[Register]
public sealed partial class DreamFeature : IDreamFeature
{
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly ISessionScanner _sessionScanner;
    private readonly IDreamTaskRegistry _taskRegistry;
    private readonly AutoDreamConfig _config;
    private readonly MiddlewarePipeline<DreamContext>? _pipeline;
    [Inject] private readonly ILogger<DreamFeature>? _logger;

    public DreamFeature(
        IChatCompletionClient chatCompletionClient,
        ISessionScanner sessionScanner,
        IDreamTaskRegistry taskRegistry,
        AutoDreamConfig? config = null,
        MiddlewarePipeline<DreamContext>? pipeline = null,
        ILogger<DreamFeature>? logger = null)
    {
        _chatCompletionClient = chatCompletionClient ?? throw new ArgumentNullException(nameof(chatCompletionClient));
        _sessionScanner = sessionScanner ?? throw new ArgumentNullException(nameof(sessionScanner));
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _config = config ?? new AutoDreamConfig();
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DreamResult> ExecuteAsync(DreamRequest request, CancellationToken cancellationToken = default)
    {
        if (_pipeline is not null)
        {
            return await ExecuteViaPipelineAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteDirectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DreamResult> ExecuteViaPipelineAsync(DreamRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var pipeline = _pipeline ?? throw new InvalidOperationException("Pipeline not available.");
        _logger?.LogInformation("[DreamFeature] 开始梦境整合(管道)");

        try
        {
            var ctx = new DreamContext { Request = request, CancellationToken = cancellationToken };
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            if (ctx.Result is not null)
            {
                return ctx.Result;
            }

            return DreamResult.Failure("管道执行完成但未产生结果");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "[DreamFeature] 梦境整合失败(管道)");
            return DreamResult.Failure($"梦境整合失败: {ex.Message}");
        }
    }

    private async Task<DreamResult> ExecuteDirectAsync(DreamRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogInformation("[DreamFeature] 开始梦境整合");

        try
        {
            // 1. 获取会话列表
            IReadOnlyList<string> sessionIds;
            if (request.SessionIds?.Count > 0)
            {
                // 用户指定了会话，直接使用
                sessionIds = request.SessionIds;
            }
            else
            {
                // 2. 检查门控条件（除非强制触发）
                if (!request.Force)
                {
                    var gateResult = await CheckGatesAsync(cancellationToken).ConfigureAwait(false);
                    if (!gateResult.IsPassed)
                    {
                        _logger?.LogDebug("[DreamFeature] 门控检查未通过: {Reason}", gateResult.Reason);
                        return DreamResult.Skipped($"门控未通过: {gateResult.Reason}");
                    }
                }

                // 自动扫描会话
                var lastConsolidationTime = DateTime.UtcNow.AddHours(-_config.MinHours).Ticks / TimeSpan.TicksPerMillisecond;
                sessionIds = await _sessionScanner.ListSessionsTouchedSinceAsync(
                    lastConsolidationTime,
                    cancellationToken).ConfigureAwait(false);
            }

            if (sessionIds.Count == 0)
            {
                _logger?.LogDebug("[DreamFeature] 没有找到需要处理的会话");
                return DreamResult.Skipped("没有需要处理的会话");
            }

            if (!request.Force && sessionIds.Count < _config.MinSessions)
            {
                _logger?.LogDebug("[DreamFeature] 会话数量不足: {Count} < {Min}", sessionIds.Count, _config.MinSessions);
                return DreamResult.Skipped($"会话数量不足: {sessionIds.Count} < {_config.MinSessions}");
            }

            // 3. 注册任务
            var taskId = await _taskRegistry.RegisterDreamTaskAsync(
                new DreamTaskRegistrationRequest(
                    sessionIds.Count,
                    DateTime.UtcNow.AddHours(-_config.MinHours).Ticks / TimeSpan.TicksPerMillisecond,
                    new CancellationTokenSource()),
                cancellationToken).ConfigureAwait(false);

            // 4. 构建提示词
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildConsolidationPrompt(sessionIds);

            // 5. 调用LLM进行整合
            var chatHistory = new MessageList();
            chatHistory.AddSystemMessage(systemPrompt);
            chatHistory.AddUserMessage(userPrompt);

            var consolidationResult = await _chatCompletionClient.GetCompletionAsync(
                chatHistory,
                cancellationToken).ConfigureAwait(false);

            // 6. 记录回合
            await _taskRegistry.AddDreamTurnAsync(
                taskId,
                new DreamTurn { Text = consolidationResult, ToolUseCount = 0 },
                Array.Empty<string>(),
                cancellationToken).ConfigureAwait(false);

            // 7. 完成任务
            await _taskRegistry.CompleteDreamTaskAsync(taskId, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            _logger?.LogInformation(
                "[DreamFeature] 梦境整合完成，处理了 {SessionCount} 个会话，耗时 {ElapsedMs}ms",
                sessionIds.Count,
                stopwatch.ElapsedMilliseconds);

            return DreamResult.Success(
                consolidationResult,
                taskId,
                sessionIds.Count,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("[DreamFeature] 梦境整合已取消");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "[DreamFeature] 梦境整合失败");
            return DreamResult.Failure($"梦境整合失败: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<DreamTaskState?> GetTaskStatusAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return _taskRegistry.GetTaskStateAsync(taskId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, DreamTaskState>> ListTasksAsync(CancellationToken cancellationToken = default)
    {
        return _taskRegistry.GetAllTasksAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task KillTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        return _taskRegistry.KillDreamTaskAsync(taskId, cancellationToken);
    }

    /// <summary>
    /// 检查门控条件
    /// </summary>
    private async Task<GateResult> CheckGatesAsync(CancellationToken cancellationToken)
    {
        // 检查是否启用
        if (!_config.Enabled)
        {
            return GateResult.Failure("自动做梦已禁用");
        }

        // 检查时间间隔（通过扫描器间接检查）
        var lastConsolidationTime = DateTime.UtcNow.AddHours(-_config.MinHours).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await _sessionScanner.ListSessionsTouchedSinceAsync(lastConsolidationTime, cancellationToken).ConfigureAwait(false);

        if (sessions.Count < _config.MinSessions)
        {
            return GateResult.Failure($"会话数不足: {sessions.Count} < {_config.MinSessions}");
        }

        return GateResult.Success();
    }

    /// <summary>
    /// 构建系统提示词
    /// </summary>
    private static string BuildSystemPrompt()
    {
        return ConsolidationPrompt.BuildPrompt(
            "memory/",
            "sessions/",
            ConsolidationPrompt.ToolConstraints);
    }

    /// <summary>
    /// 构建整合提示词
    /// </summary>
    private static string BuildConsolidationPrompt(IReadOnlyList<string> sessionIds)
    {
        return ConsolidationPrompt.BuildExtraContext(
            sessionIds,
            ConsolidationPrompt.ToolConstraints);
    }

    /// <summary>
    /// 门控结果
    /// </summary>
    private readonly record struct GateResult(bool IsPassed, string Reason)
    {
        public static GateResult Success() => new(true, string.Empty);
        public static GateResult Failure(string reason) => new(false, reason);
    }
}
