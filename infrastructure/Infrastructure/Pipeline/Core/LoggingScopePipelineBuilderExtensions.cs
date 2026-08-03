namespace Infrastructure.Pipeline;

using Infrastructure.Pipeline.Middlewares;

/// <summary>
/// 管道构建器扩展方法 — 一行接入日志 Scope
/// ILoggerFactory 为必需参数，编译期保证 scope 生效
/// </summary>
public static class LoggingScopePipelineBuilderExtensions
{
    /// <summary>
    /// 在管道最前面插入日志 Scope — 默认选择器（Entity 直接取 ObjectId，其余 Empty）
    /// ⚠️ 必须第一个调用，确保 scope 覆盖整个管道
    /// </summary>
    public static PipelineBuilder<TContext> WithLoggingScope<TContext>(
        this PipelineBuilder<TContext> builder,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<LoggingScopeMiddleware<TContext>>();
        return builder.Use(new LoggingScopeMiddleware<TContext>(logger));
    }

    /// <summary>
    /// 在管道最前面插入日志 Scope — 自定义 ObjectId 选择器
    /// </summary>
    public static PipelineBuilder<TContext> WithLoggingScope<TContext>(
        this PipelineBuilder<TContext> builder,
        Func<TContext, ObjectId> objectIdSelector,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<LoggingScopeMiddleware<TContext>>();
        return builder.Use(new LoggingScopeMiddleware<TContext>(logger, objectIdSelector));
    }

    /// <summary>
    /// Stream 管道版本 — 默认选择器
    /// </summary>
    public static StreamPipelineBuilder<TContext, TEvent> WithLoggingScope<TContext, TEvent>(
        this StreamPipelineBuilder<TContext, TEvent> builder,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<StreamLoggingScopeMiddleware<TContext, TEvent>>();
        return builder.Use(new StreamLoggingScopeMiddleware<TContext, TEvent>(logger));
    }

    /// <summary>
    /// Stream 管道版本 — 自定义 ObjectId 选择器
    /// </summary>
    public static StreamPipelineBuilder<TContext, TEvent> WithLoggingScope<TContext, TEvent>(
        this StreamPipelineBuilder<TContext, TEvent> builder,
        Func<TContext, ObjectId> objectIdSelector,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<StreamLoggingScopeMiddleware<TContext, TEvent>>();
        return builder.Use(new StreamLoggingScopeMiddleware<TContext, TEvent>(logger, objectIdSelector));
    }
}
