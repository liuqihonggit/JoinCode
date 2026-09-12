namespace Infrastructure.Pipeline;

/// <summary>
/// 管道构建器扩展方法 — 统一 Task/Stream 两种构建器的 Hook 解析逻辑
/// C# 无法从 CRTP 约束链推断 TContext，因此用两个显式重载 + 共享核心方法
/// </summary>
public static class PipelineBuilderExtensions
{
    /// <summary>
    /// 为 Task 管道构建器附加 DI 解析的 Pre/Post Hook + 默认 onError 日志回调
    /// 当 builder 尚未设置 OnError 时，自动注入 ILogger 记录 Continue 中间件的异常
    /// </summary>
    public static PipelineBuilder<TContext> WithHooks<TContext>(
        this PipelineBuilder<TContext> builder, IServiceProvider sp)
    {
        ApplyHooks<TContext>(sp,
            hook => builder.WithPreHook(hook),
            hook => builder.WithPostHook(hook));
        ApplyDefaultOnError(builder, sp);
        return builder;
    }

    /// <summary>
    /// 为 Stream 管道构建器附加 DI 解析的 Pre/Post Hook + 默认 onError 日志回调
    /// </summary>
    public static StreamPipelineBuilder<TContext, TEvent> WithHooks<TContext, TEvent>(
        this StreamPipelineBuilder<TContext, TEvent> builder, IServiceProvider sp)
    {
        ApplyHooks<TContext>(sp,
            hook => builder.WithPreHook(hook),
            hook => builder.WithPostHook(hook));
        ApplyDefaultOnError(builder, sp);
        return builder;
    }

    /// <summary>
    /// 当 builder 尚未显式设置 OnError 时，自动注入默认的 ILogger 日志回调
    /// 确保 ErrorBehavior.Continue 的中间件异常被正确捕获和记录，而非静默穿透
    /// </summary>
    private static void ApplyDefaultOnError<TContext>(
        PipelineBuilderBase<TContext, PipelineBuilder<TContext>> builder, IServiceProvider sp)
    {
        if (builder.OnErrorHandler is not null)
            return;

        var logger = sp.GetService<ILogger<TContext>>();
        if (logger is null)
            return;

        builder.OnError((ctx, ex) =>
        {
            logger.LogError(ex, "[Pipeline<{PipeName}>] Continue 中间件异常被捕获", typeof(TContext).Name);
        });
    }

    /// <summary>
    /// Stream 管道构建器的默认 onError 注入
    /// </summary>
    private static void ApplyDefaultOnError<TContext, TEvent>(
        PipelineBuilderBase<TContext, StreamPipelineBuilder<TContext, TEvent>> builder, IServiceProvider sp)
    {
        if (builder.OnErrorHandler is not null)
            return;

        var logger = sp.GetService<ILogger<TContext>>();
        if (logger is null)
            return;

        builder.OnError((ctx, ex) =>
        {
            logger.LogError(ex, "[StreamPipeline<{PipeName}>] Continue 中间件异常被捕获", typeof(TContext).Name);
        });
    }

    private static void ApplyHooks<TContext>(
        IServiceProvider sp,
        Action<PipelinePreHookDelegate<TContext>> setPreHook,
        Action<PipelinePostHookDelegate<TContext>> setPostHook)
    {
        var preHooks = sp.GetServices<IPipelinePreHook<TContext>>().ToArray();
        if (preHooks.Length > 0)
        {
            setPreHook(async (ctx, ct) =>
            {
                foreach (var hook in preHooks)
                    if (!await hook.InvokeAsync(ctx, ct).ConfigureAwait(false))
                        return false;
                return true;
            });
        }

        var postHooks = sp.GetServices<IPipelinePostHook<TContext>>().ToArray();
        if (postHooks.Length > 0)
        {
            setPostHook(async (ctx, ct) =>
            {
                foreach (var hook in postHooks)
                    await hook.InvokeAsync(ctx, ct).ConfigureAwait(false);
            });
        }
    }
}
