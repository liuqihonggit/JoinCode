namespace Core.Context;

/// <summary>
/// 系统提示构建中间件 — 构建分区系统提示（静态前缀 + 动态后缀）
/// </summary>
[Register(typeof(IPreparePreprocessMiddleware), ServiceLifetime.Singleton)]
public sealed partial class SystemPromptMiddleware : ServiceEntity, IPreparePreprocessMiddleware {

    /// <summary>
    /// 初始化系统提示构建中间件
    /// </summary>
    /// <param name="contextManager">聊天上下文管理器</param>
    /// <param name="nodeRegistry">节点注册表</param>
    /// <param name="profileManager">Profile 管理器</param>
    public SystemPromptMiddleware(
        IChatContextManager contextManager,
        INodeRegistry nodeRegistry,
        IProfileManager profileManager) {
        _contextManager = contextManager;
        _nodeRegistry = nodeRegistry;
        _profileManager = profileManager;
    }
    private readonly IChatContextManager _contextManager;
    private readonly INodeRegistry _nodeRegistry;
    private readonly IProfileManager _profileManager;


    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct) {
        var categoryNodes = await _nodeRegistry.GetServicesForConsumerAsync<PromptSectionCategoryNode>(
            "prompt-assembly", _profileManager.Current, ct).ConfigureAwait(false);
        var allSections = categoryNodes.SelectMany(n => n.Sections).ToList();
        await using var builder = new SystemPromptBuilder();
        builder.AddSections(allSections);
        var (staticPrefix, dynamicSuffix) = await builder.BuildPartitionedAsync().ConfigureAwait(false);

        context.StaticPrefix = staticPrefix;
        context.DynamicSuffix = dynamicSuffix;

        await _contextManager.ClearDynamicSystemMessagesAsync(ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(staticPrefix)) {
            await _contextManager.UpdateSystemPromptAsync(staticPrefix, ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(dynamicSuffix)) {
            await _contextManager.AddDynamicSystemMessageAsync(dynamicSuffix, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}