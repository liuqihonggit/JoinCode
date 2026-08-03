namespace Infrastructure.Pipeline;

using Infrastructure.Pipeline.Middlewares;

/// <summary>
/// 管道构建器扩展方法 — 一行接入日志 Scope
/// </summary>
public static class LoggingScopePipelineBuilderExtensions
{
    /// <summary>
    /// 在管道最前面插入日志 Scope 中间件 — 后续所有中间件的日志自动携带 TraceId + ObjectId
    /// ⚠️ 必须第一个调用，确保 scope 覆盖整个管道
    /// </summary>
    public static PipelineBuilder<TContext> WithLoggingScope<TContext>(
        this PipelineBuilder<TContext> builder,
        ILogger<LoggingScopeMiddleware<TContext>>? logger = null)
    {
        return builder.Use(new LoggingScopeMiddleware<TContext>(logger));
    }

    /// <summary>
    /// Stream 管道版本
    /// </summary>
    public static StreamPipelineBuilder<TContext, TEvent> WithLoggingScope<TContext, TEvent>(
        this StreamPipelineBuilder<TContext, TEvent> builder,
        ILogger<StreamLoggingScopeMiddleware<TContext, TEvent>>? logger = null)
    {
        return builder.Use(new StreamLoggingScopeMiddleware<TContext, TEvent>(logger));
    }
}
