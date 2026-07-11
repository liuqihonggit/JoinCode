namespace Tests;

/// <summary>
/// 测试用管道注册辅助类 — 注册所有管道（与生产代码 AddAllPipelines 等价，但跳过 JoinCode Exe 独有的中间件）
/// </summary>
/// <remarks>
/// 生产代码的 AddAllPipelines 在 src/JoinCode/App/PipelineComposition.cs，
/// 包含 JoinCode 主程序内的中间件（ChatTimingMiddleware/ChatErrorHandlingMiddleware/
/// AuditLogMiddleware/TokenBudgetMiddleware），测试项目无法引用 Exe 项目，
/// 故此处 Chat 管道仅注册 Brain 组件中的6个公开中间件，其他管道与生产代码一致。
/// </remarks>
public static class TestPipelineRegistration
{
    /// <summary>
    /// 注册测试用全部管道 — 与生产代码 AddAllPipelines 等价
    /// </summary>
    public static IServiceCollection AddTestPipelines(this IServiceCollection services)
    {
        services.AddSingleton(sp => new MetricsMiddleware<WebContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<SkillContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<CodeContext>(sp.GetService<ITelemetryService>()));

        // ═══════════════════════════════════════════════════════════
        // Stream 管道
        // ═══════════════════════════════════════════════════════════

        // Chat 聊天管道 (Stream) — 跳过 JoinCode.Exe 独有的4个中间件
        // 省略: ChatTimingMiddleware, ChatErrorHandlingMiddleware, AuditLogMiddleware, ChatTokenBudgetMiddleware
        services.AddSingleton<StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>>(sp =>
            new StreamPipelineBuilder<ChatMiddlewareContext, ChatStreamEvent>()
                .Use(sp.GetRequiredService<PreChatMiddleware>())
                .Use(sp.GetRequiredService<QueryLoopMiddleware>())
                .Use(sp.GetRequiredService<LoopInterventionMiddleware>())
                .Use(sp.GetRequiredService<ProcessUsageMiddleware>())
                .Use(sp.GetRequiredService<CleanupInjectionsMiddleware>())
                .Use(sp.GetRequiredService<SaveContextMiddleware>())
                .WithStreamHooks(sp)
                .Build());

        // ═══════════════════════════════════════════════════════════
        // Task 管道
        // ═══════════════════════════════════════════════════════════

        // Preprocess 预处理管道
        services.AddSingleton<MiddlewarePipeline<PreprocessContext>>(sp =>
            new PipelineBuilder<PreprocessContext>()
                .Use(sp.GetRequiredService<KeywordInjectionMiddleware>())
                .Use(sp.GetRequiredService<SynonymInjectionMiddleware>())
                .Use(sp.GetRequiredService<SystemPromptMiddleware>())
                .Use(sp.GetRequiredService<ReminderInjectionMiddleware>())
                .Use(sp.GetRequiredService<ToolListingInjectionMiddleware>())
                .Use(sp.GetRequiredService<LspDiagnosticMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // ChatInit 聊天初始化管道
        services.AddSingleton<MiddlewarePipeline<ChatInitContext>>(sp =>
            new PipelineBuilder<ChatInitContext>()
                .Use(sp.GetRequiredService<ContextLoadMiddleware>())
                .Use(sp.GetRequiredService<CostRestoreMiddleware>())
                .Use(sp.GetRequiredService<ConfigChangeStartMiddleware>())
                .Use(sp.GetRequiredService<SessionStartHookMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // ChatAdmin 管理管道
        services.AddSingleton<MiddlewarePipeline<ChatAdminContext>>(sp =>
            new PipelineBuilder<ChatAdminContext>()
                .Use(sp.GetRequiredService<SessionAdminMiddleware>())
                .Use(sp.GetRequiredService<SessionSaveMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // Compact 压缩管道
        services.AddSingleton<MiddlewarePipeline<CompactContext>>(sp =>
            new PipelineBuilder<CompactContext>()
                .Use(sp.GetRequiredService<CompactHookMiddleware>())
                .Use(sp.GetRequiredService<ContextCollapseMiddleware>())
                .Use(sp.GetRequiredService<MicrocompactMiddleware>())
                .Use(sp.GetRequiredService<SessionMemoryCompactMiddleware>())
                .Use(sp.GetRequiredService<ReactiveCompactMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // Query 查询管道
        services.AddSingleton<MiddlewarePipeline<QueryMiddlewareContext>>(sp =>
            new PipelineBuilder<QueryMiddlewareContext>()
                .Use(sp.GetRequiredService<UsdBudgetMiddleware>())
                .Use(sp.GetRequiredService<TokenBudgetMiddleware>())
                .Use(sp.GetRequiredService<CostTrackingMiddleware>())
                .Use(sp.GetRequiredService<DiminishingReturnsMiddleware>())
                .Use(sp.GetRequiredService<HistorySnipMiddleware>())
                .Use(sp.GetRequiredService<IdleReminderMiddleware>())
                .Use(sp.GetRequiredService<StopHookMiddleware>())
                .Use(sp.GetRequiredService<StateTransitionMiddleware>())
                .Use(sp.GetRequiredService<ContentReplacementMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // Permission 权限管道
        services.AddSingleton<MiddlewarePipeline<PermissionCheckContext>>(sp =>
            new PipelineBuilder<PermissionCheckContext>()
                .Use(sp.GetRequiredService<BypassPermissionMiddleware>())
                .Use(sp.GetRequiredService<AgentRestrictionMiddleware>())
                .Use(sp.GetRequiredService<AutoClassifierMiddleware>())
                .Use(sp.GetRequiredService<ConfigGetOperationMiddleware>())
                .Use(sp.GetRequiredService<WebFetchPermissionMiddleware>())
                .Use(sp.GetRequiredService<EarlyPathDenyMiddleware>())
                .Use(sp.GetRequiredService<ToolListPermissionMiddleware>())
                .Use(sp.GetRequiredService<PathPermissionMiddleware>())
                .Use(sp.GetRequiredService<DangerousOperationMiddleware>())
                .Use(sp.GetRequiredService<PlanModeMiddleware>())
                .Use(sp.GetRequiredService<AutoSafetyMiddleware>())
                .Use(sp.GetRequiredService<DefaultResultMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // Settings 设置管道
        services.AddSingleton<MiddlewarePipeline<SettingsContext>>(sp =>
            new PipelineBuilder<SettingsContext>()
                .Use(sp.GetRequiredService<SettingsReloadMiddleware>())
                .Use(sp.GetRequiredService<EffortLevelMiddleware>())
                .Use(sp.GetRequiredService<HookRefreshMiddleware>())
                .Use(sp.GetRequiredService<PermissionCacheMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // AgentSpawn 智能体生成管道
        services.AddSingleton<MiddlewarePipeline<AgentSpawnContext>>(sp =>
            new PipelineBuilder<AgentSpawnContext>()
                .Use(sp.GetRequiredService<DefinitionResolutionMiddleware>())
                .Use(sp.GetRequiredService<PromptBuildingMiddleware>())
                .Use(sp.GetRequiredService<ContextSetupMiddleware>())
                .Use(sp.GetRequiredService<AgentWorktreeSpawnMiddleware>())
                .Use(sp.GetRequiredService<HookSetupMiddleware>())
                .Use(sp.GetRequiredService<McpSetupMiddleware>())
                .Use(sp.GetRequiredService<MetadataMiddleware>())
                .Use(sp.GetRequiredService<TranscriptMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // Fork 分支管道
        services.AddSingleton<MiddlewarePipeline<ForkContext>>(sp =>
            new PipelineBuilder<ForkContext>()
                .Use(sp.GetRequiredService<ForkValidationMiddleware>())
                .Use(sp.GetRequiredService<ForkSpawnMiddleware>())
                .Use(sp.GetRequiredService<ForkPermissionMiddleware>())
                .Use(sp.GetRequiredService<ForkExecutionMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // Web 网页管道
        services.AddSingleton<MiddlewarePipeline<WebContext>>(sp =>
            new PipelineBuilder<WebContext>()
                .Use(sp.GetRequiredService<MetricsMiddleware<WebContext>>())
                .Use(sp.GetRequiredService<WebValidationMiddleware>())
                .Use(sp.GetRequiredService<WebSsrfGuardMiddleware>())
                .Use(sp.GetRequiredService<WebCacheCheckMiddleware>())
                .Use(sp.GetRequiredService<WebDomainCheckMiddleware>())
                .Use(sp.GetRequiredService<WebFetchMiddleware>())
                .Use(sp.GetRequiredService<WebContentProcessingMiddleware>())
                .Use(sp.GetRequiredService<WebCacheWriteMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // Shell 命令管道
        services.AddSingleton<MiddlewarePipeline<ShellContext>>(sp =>
            new PipelineBuilder<ShellContext>()
                .Use(sp.GetRequiredService<ShellValidationMiddleware>())
                .Use(sp.GetRequiredService<ShellClassificationMiddleware>())
                .Use(sp.GetRequiredService<ShellSedInterceptMiddleware>())
                .Use(sp.GetRequiredService<ShellBackgroundMiddleware>())
                .Use(sp.GetRequiredService<ShellBuildInterceptMiddleware>())
                .Use(sp.GetRequiredService<ShellExecutionMiddleware>())
                .Use(sp.GetRequiredService<ShellOutputMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // Skill 技能管道
        services.AddSingleton<MiddlewarePipeline<SkillContext>>(sp =>
            new PipelineBuilder<SkillContext>()
                .Use(sp.GetRequiredService<MetricsMiddleware<SkillContext>>())
                .Use(sp.GetRequiredService<SkillValidationMiddleware>())
                .Use(sp.GetRequiredService<SkillTelemetryMiddleware>())
                .Use(sp.GetRequiredService<SkillExecutionMiddleware>())
                .WithTaskHooks(sp)
                .Build());

        // Code 代码管道
        services.AddSingleton<MiddlewarePipeline<CodeContext>>(sp =>
            new PipelineBuilder<CodeContext>()
                .Use(sp.GetRequiredService<CodeCacheMiddleware>())
                .Use(sp.GetRequiredService<CodeSecurityMiddleware>())
                .Use(sp.GetRequiredService<CodeLlmMiddleware>())
                .Use(sp.GetRequiredService<CodeSandboxMiddleware>())
                .Use(sp.GetRequiredService<MetricsMiddleware<CodeContext>>())
                .WithTaskHooks(sp)
                .Build());

        return services;
    }

    /// <summary>
    /// 为 PipelineBuilder 附加 Hook 解析
    /// </summary>
    private static PipelineBuilder<TContext> WithTaskHooks<TContext>(this PipelineBuilder<TContext> builder, IServiceProvider sp)
    {
        var preHooks = sp.GetServices<IPipelinePreHook<TContext>>().ToArray();
        if (preHooks.Length > 0)
        {
            builder.WithPreHook(async (ctx, ct) =>
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
            builder.WithPostHook(async (ctx, ct) =>
            {
                foreach (var hook in postHooks)
                    await hook.InvokeAsync(ctx, ct).ConfigureAwait(false);
            });
        }

        return builder;
    }

    /// <summary>
    /// 为 StreamPipelineBuilder 附加 Hook 解析
    /// </summary>
    private static StreamPipelineBuilder<TContext, TEvent> WithStreamHooks<TContext, TEvent>(
        this StreamPipelineBuilder<TContext, TEvent> builder, IServiceProvider sp)
    {
        var preHooks = sp.GetServices<IPipelinePreHook<TContext>>().ToArray();
        if (preHooks.Length > 0)
        {
            builder.WithPreHook(async (ctx, ct) =>
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
            builder.WithPostHook(async (ctx, ct) =>
            {
                foreach (var hook in postHooks)
                    await hook.InvokeAsync(ctx, ct).ConfigureAwait(false);
            });
        }

        return builder;
    }
}
