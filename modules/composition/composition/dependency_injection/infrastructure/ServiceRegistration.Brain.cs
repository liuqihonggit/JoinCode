namespace Core.DependencyInjection;

public static partial class ServiceRegistration {
    /// <summary>
    /// 注册 Brain 管道服务：上下文折叠执行器（<see cref="ContextFoldExecutor"/>）和聊天预处理器（<see cref="ChatPreprocessor"/>）。
    /// <para>ChatPreprocessor 包含 Analyze 和 Prepare 两条中间件管道，分别处理消息分析和准备阶段。</para>
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddBrainPipelines(this IServiceCollection services) {
        services.AddSingleton<ChatContextOptions>(sp => {
            var summarizer = sp.GetRequiredService<IFoldSummarizer>();
            var foldLogger = sp.GetService<ILogger<ContextFoldExecutor>>();
            var executor = new ContextFoldExecutor(summarizer, foldLogger);
            return new ChatContextOptions { FoldExecutor = executor };
        });

        services.AddSingleton<IChatPreprocessor>(sp => {
            var analyzeMiddlewares = sp.GetServices<IAnalyzePreprocessMiddleware>().Cast<IMiddleware<PreprocessContext>>();
            var prepareMiddlewares = sp.GetServices<IPreparePreprocessMiddleware>().Cast<IMiddleware<PreprocessContext>>();
            var logger = sp.GetService<ILogger<ChatPreprocessor>>();

            var analyzeBuilder = new PipelineBuilder<PreprocessContext>()
                .UseRange(analyzeMiddlewares);
            if (logger is not null)
                analyzeBuilder.OnError((ctx, ex) => logger.LogError(ex, "[ChatPreprocessor.Analyze] 中间件执行异常"));
            var analyzePipeline = analyzeBuilder.Build();

            var prepareBuilder = new PipelineBuilder<PreprocessContext>()
                .UseRange(prepareMiddlewares);
            if (logger is not null)
                prepareBuilder.OnError((ctx, ex) => logger.LogError(ex, "[ChatPreprocessor.Prepare] 中间件执行异常"));
            var preparePipeline = prepareBuilder.Build();

            return new ChatPreprocessor(
                analyzePipeline,
                preparePipeline,
                sp.GetRequiredService<ISystemReminderManager>(),
                sp.GetRequiredService<IChatContextManager>(),
                logger);
        });

        return services;
    }
}