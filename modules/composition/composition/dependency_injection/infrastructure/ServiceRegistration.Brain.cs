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
            var notebookService = sp.GetService<JoinCode.Abstractions.LLM.Chat.IContextArchiveService>();
            var foldLogger = sp.GetService<ILogger<ContextFoldExecutor>>();
            var executor = new ContextFoldExecutor(summarizer, notebookService, foldLogger);
            return new ChatContextOptions { FoldExecutor = executor };
        });

        // TASK100 S9（§25）：preprocess 的两条链改为**按 profile 解析**。
        //
        // 原来这里是 sp.GetServices<IAnalyzePreprocessMiddleware>() / GetServices<IPreparePreprocessMiddleware>()
        // —— 顺序由 DI 注册顺序决定，profile 声明管不到它（实证见 §25.2：往 preprocess-pipeline
        // 注入幽灵节点，运行期毫无反应）。于是「profile 能裁剪中间件」在 preprocess 上是假的，
        // 且 crash-snapshot-mw 从未在 preprocess 阶段生效。
        //
        // 现在两个阶段各对应一个 profile 消费点（preprocess-analyze-pipeline /
        // preprocess-prepare-pipeline），由 NodeRegistry 按 profile 的 priority 解析，
        // 解析不到实例时 fail-fast —— 与 DataDrivenPipelineComposition.ResolveMiddlewares 同一口径。
        services.AddSingleton<IChatPreprocessor>(sp => new DeferredChatPreprocessor(() => ResolveChatPreprocessorAsync(sp)));

        return services;
    }

    /// <summary>
    /// 构建 <see cref="ChatPreprocessor"/>：两条 preprocess 链都按 profile 解析（§25.3）。
    /// <para>
    /// 由 <see cref="DeferredChatPreprocessor"/> 在首次调用时 await 本方法 — DI 的
    /// <c>AddSingleton&lt;T&gt;(Func&lt;IServiceProvider, T&gt;)</c> 没有 async 重载，
    /// 注册阶段用延迟代理避免同步阻塞；装配失败在首次预处理时即暴露。
    /// </para>
    /// </summary>
    private static async Task<IChatPreprocessor> ResolveChatPreprocessorAsync(IServiceProvider sp) {
        {
            var profile = sp.GetRequiredService<IProfileManager>().Current;
            var registry = sp.GetRequiredService<INodeRegistry>();
            var logger = sp.GetService<ILogger<ChatPreprocessor>>();

            var analyzeMiddlewares = await ResolvePreprocessChainAsync(
                registry, profile, "preprocess-analyze-pipeline").ConfigureAwait(false);
            var prepareMiddlewares = await ResolvePreprocessChainAsync(
                registry, profile, "preprocess-prepare-pipeline").ConfigureAwait(false);

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
        }
    }

    /// <summary>
    /// 按 profile 解析一条 preprocess 阶段的中间件链。
    /// <para>
    /// 节点解析失败（工厂缺失 / 类型不匹配）时抛错而不是静默跳过 —— 与
    /// <c>DataDrivenPipelineComposition.ResolveMiddlewares</c> 保持同一口径（§24.2 的 C2）。
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<IMiddleware<PreprocessContext>>> ResolvePreprocessChainAsync(
        INodeRegistry registry, ResolvedProfile profile, string consumerId) {
        if (!profile.Consumers.TryGetValue(consumerId, out var consumerConfig))
            throw new InvalidOperationException(
                $"消费点 '{consumerId}' 在 profile '{profile.Name}' 中不存在 —— " +
                "preprocess 的两条链现在由 profile 治理，缺声明等于整条链不装配。");

        var middlewares = await registry
            .GetServicesForConsumerAsync<IMiddleware<PreprocessContext>>(consumerId, profile, default, failFastOnMissing: true)
            .ConfigureAwait(false);

        // 声明数与解析数必须一致。feature flag 关闭、meta.required-capabilities 未满足的节点
        // 都由 registry 自行跳过（TASK097 §15 H3），计数口径必须与 registry 的过滤逻辑同源，
        // 否则「能力关闭」会被误判成「节点工厂缺失」而启动失败。
        var declared = consumerConfig.Nodes.Count(n =>
            n.EnabledWhenFeatureFlag is null && NodeRegistry.RequiredCapabilitiesSatisfied(n, profile));
        if (middlewares.Count != declared) {
            throw new InvalidOperationException(
                $"消费点 '{consumerId}' 声明了 {declared} 个中间件，实际只解析出 {middlewares.Count} 个 —— " +
                "节点工厂缺失或类型与消费点期望不符。宁可启动失败，也不要静默少一道中间件。");
        }

        JoinCode.Abstractions.Utils.Diagnostics.Diag.WriteLine(
            $"[ORDER] middleware {consumerId}: {string.Join(" > ", middlewares.Select(static m => m.GetType().Name))}");
        return middlewares;
    }
}