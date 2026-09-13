namespace Core.Context;

/// <summary>
/// 系统提示构建中间件 — 构建分区系统提示（静态前缀 + 动态后缀）
/// </summary>
[Register(typeof(IPreparePreprocessMiddleware), ServiceLifetime.Singleton)]
public sealed partial class SystemPromptMiddleware : ServiceEntity, IPreparePreprocessMiddleware
{

    /// <summary>
    /// 初始化系统提示构建中间件
    /// </summary>
    /// <param name="systemPromptBuilder">系统提示构建器</param>
    /// <param name="contextManager">聊天上下文管理器</param>
    public SystemPromptMiddleware(SystemPromptBuilder systemPromptBuilder, IChatContextManager contextManager)
    {
        _systemPromptBuilder = systemPromptBuilder;
        _contextManager = contextManager;
    }
    private readonly SystemPromptBuilder _systemPromptBuilder;
    private readonly IChatContextManager _contextManager;


    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct)
    {
        var (staticPrefix, dynamicSuffix) = await _systemPromptBuilder.BuildPartitionedAsync().ConfigureAwait(false);
        context.StaticPrefix = staticPrefix;
        context.DynamicSuffix = dynamicSuffix;

        await _contextManager.ClearDynamicSystemMessagesAsync(ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(staticPrefix))
        {
            await _contextManager.UpdateSystemPromptAsync(staticPrefix, ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(dynamicSuffix))
        {
            await _contextManager.AddDynamicSystemMessageAsync(dynamicSuffix, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
