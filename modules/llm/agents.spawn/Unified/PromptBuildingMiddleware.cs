namespace Core.Agents;

/// <summary>
/// 提示构建中间件 — 构建系统提示词并加载记忆
/// 统一管道版本：主代理 no-op，路径 B（SubOptions 模式）no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware), ServiceLifetime.Singleton)]
public sealed partial class PromptBuildingMiddleware : ServiceEntity, IUnifiedSpawnMiddleware {

    /// <summary>
    /// 构造 PromptBuildingMiddleware 实例，注入提示构建器、可选的记忆服务与日志器
    /// </summary>
    public PromptBuildingMiddleware(IAgentPromptBuilder promptBuilder, IAgentMemoryService? agentMemoryService = null, ILogger<PromptBuildingMiddleware>? logger = null) {
        _promptBuilder = promptBuilder;
        _agentMemoryService = agentMemoryService;
        _logger = logger;
    }
    private readonly IAgentPromptBuilder _promptBuilder;
    private readonly IAgentMemoryService? _agentMemoryService;
    private readonly ILogger<PromptBuildingMiddleware>? _logger;

    /// <summary>中间件错误处理策略：向上传播</summary>
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 执行提示构建：主代理或无 SpawnOptions 跳过；否则构建系统提示词并加载记忆
    /// </summary>
    /// <param name="context">统一 Spawn 上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct) {
        if (context.IsMainAgent || context.SpawnOptions is null) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var agentTypeValue = context.SpawnOptions.Variant?.ToValue() ?? context.SpawnOptions.Role.ToValue();
        var systemPrompt = await _promptBuilder.BuildSystemPromptAsync(
            agentTypeValue, context.SpawnOptions.Description, cancellationToken: ct).ConfigureAwait(false);

        var memoryScope = context.SpawnOptions.MemoryScope ?? context.Definition?.Memory;
        if (memoryScope is not null && _agentMemoryService is not null && (context.SpawnOptions.Variant.HasValue || context.SpawnOptions.Role != default)) {
            try {
                var memoryPrompt = await _agentMemoryService.LoadAgentMemoryPromptAsync(
                    agentTypeValue, memoryScope.Value, ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(memoryPrompt))
                    systemPrompt = $"{systemPrompt}\n\n{memoryPrompt}";

                var snapshotCheck = await _agentMemoryService.CheckSnapshotAsync(
                    agentTypeValue, memoryScope.Value, ct).ConfigureAwait(false);

                if (snapshotCheck.Action == AgentMemorySnapshotAction.Initialize) {
                    await _agentMemoryService.InitializeFromSnapshotAsync(
                        agentTypeValue, memoryScope.Value, snapshotCheck.SnapshotTimestamp ?? throw new InvalidOperationException("SnapshotTimestamp is null"), ct).ConfigureAwait(false);
                }
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "[PromptBuildingMiddleware] 加载 Agent 记忆失败: {Role}", agentTypeValue);
            }
        }

        context.SystemPrompt = systemPrompt;

        await next(context, ct).ConfigureAwait(false);
    }
}