using Core.Context;
using Infrastructure.Pipeline;

namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddBrainPipelines(this IServiceCollection services)
    {
        services.AddSingleton<IChatPreprocessor>(sp =>
        {
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
