namespace JoinCode.Pipelines;

/// <summary>
/// 测试用管道注册 — 全量注册已实现的中间件管道，但刻意排除生产加装的状态性保护：
/// 1) Chat 管道排除 4 个 CLI 专属中间件（ChatTiming/ChatErrorHandling/AuditLog/TokenBudget）
/// 2) 全部管道排除速率限制 / 超时 / 熔断等 Fixed*Guard（避免 Token 预算阻塞与限流抖动导致的测试偶发失败）
/// 语义与生产 AddAllPipelines 对齐（洋葱顺序），供测试工程统一调用，消除两套 AddTestPipelines 漂移。
/// </summary>
public static class TestPipelineRegistration
{
    /// <summary>
    /// 注册测试用全部管道 — 与生产 AddAllPipelines 等价但排除状态性修饰中间件
    /// </summary>
    public static IServiceCollection AddTestPipelines(this IServiceCollection services)
    {
        services.AddSingleton(sp => new MetricsMiddleware<WebContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<SkillContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<CodeContext>(sp.GetService<ITelemetryService>()));

        // ═══════════════════════════════════════════════════════════
        // Stream 管道
        // ═══════════════════════════════════════════════════════════

        // Chat 聊天管道 (Stream) — 跳过 4 个 CLI 专属中间件与限流
        services.AddSingleton<StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>>(sp =>
            new StreamPipelineBuilder<ChatMiddlewareContext, ChatStreamEvent>()
                .Use(sp.GetRequiredService<PreChatMiddleware>())
                .Use(sp.GetRequiredService<QueryLoopMiddleware>())
                .Use(sp.GetRequiredService<LoopInterventionMiddleware>())
                .Use(sp.GetRequiredService<ProcessUsageMiddleware>())
                .Use(sp.GetRequiredService<CleanupInjectionsMiddleware>())
                .Use(sp.GetRequiredService<SaveContextMiddleware>())
                .WithHooks(sp)
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
                .WithHooks(sp)
                .Build());

        // ChatInit 聊天初始化管道
        services.AddSingleton<MiddlewarePipeline<ChatInitContext>>(sp =>
            new PipelineBuilder<ChatInitContext>()
                .Use(sp.GetRequiredService<ContextLoadMiddleware>())
                .Use(sp.GetRequiredService<CostRestoreMiddleware>())
                .Use(sp.GetRequiredService<ConfigChangeStartMiddleware>())
                .Use(sp.GetRequiredService<SessionStartHookMiddleware>())
                .WithHooks(sp)
                .Build());

        // ChatAdmin 管理管道
        services.AddSingleton<MiddlewarePipeline<ChatAdminContext>>(sp =>
            new PipelineBuilder<ChatAdminContext>()
                .Use(sp.GetRequiredService<SessionAdminMiddleware>())
                .Use(sp.GetRequiredService<SessionSaveMiddleware>())
                .WithHooks(sp)
                .Build());

        // Compact 压缩管道
        services.AddSingleton<MiddlewarePipeline<CompactContext>>(sp =>
            new PipelineBuilder<CompactContext>()
                .Use(sp.GetRequiredService<CompactHookMiddleware>())
                .Use(sp.GetRequiredService<ContextCollapseMiddleware>())
                .Use(sp.GetRequiredService<MicrocompactMiddleware>())
                .Use(sp.GetRequiredService<SessionMemoryCompactMiddleware>())
                .Use(sp.GetRequiredService<ReactiveCompactMiddleware>())
                .WithHooks(sp)
                .Build());

        // Query 查询管道 — 排除限流
        services.AddSingleton<MiddlewarePipeline<QueryMiddlewareContext>>(sp =>
            new PipelineBuilder<QueryMiddlewareContext>()
                .Use(sp.GetRequiredService<UsdBudgetMiddleware>())
                .Use(sp.GetRequiredService<QueryTokenBudgetMiddleware>())
                .Use(sp.GetRequiredService<CostTrackingMiddleware>())
                .Use(sp.GetRequiredService<DiminishingReturnsMiddleware>())
                .Use(sp.GetRequiredService<HistorySnipMiddleware>())
                .Use(sp.GetRequiredService<IdleReminderMiddleware>())
                .Use(sp.GetRequiredService<StopHookMiddleware>())
                .Use(sp.GetRequiredService<StateTransitionMiddleware>())
                .Use(sp.GetRequiredService<ContentReplacementMiddleware>())
                .WithHooks(sp)
                .Build());

        // Permission 权限管道
        services.AddSingleton<MiddlewarePipeline<PermissionCheckContext>>(sp =>
            new PipelineBuilder<PermissionCheckContext>()
                .WithShortCircuit(ctx => ctx.Result is not null)
                .Use(sp.GetRequiredService<BypassPermissionMiddleware>())
                .Use(sp.GetRequiredService<AgentRestrictionMiddleware>())
                .Use(sp.GetRequiredService<DangerousCommandProtectionMiddleware>())
                .Use(sp.GetRequiredService<AutoClassifierMiddleware>())
                .Use(sp.GetRequiredService<ConfigGetOperationMiddleware>())
                .Use(sp.GetRequiredService<WebFetchPermissionMiddleware>())
                .Use(sp.GetRequiredService<EarlyPathDenyMiddleware>())
                .Use(sp.GetRequiredService<ToolListPermissionMiddleware>())
                .Use(sp.GetRequiredService<PathPermissionMiddleware>())
                .Use(sp.GetRequiredService<DangerousOperationMiddleware>())
                .Use(sp.GetRequiredService<PlanModeMiddleware>())
                .Use(sp.GetRequiredService<DefaultResultMiddleware>())
                .WithHooks(sp)
                .Build());

        // Settings 设置管道
        services.AddSingleton<MiddlewarePipeline<SettingsContext>>(sp =>
            new PipelineBuilder<SettingsContext>()
                .Use(sp.GetRequiredService<SettingsReloadMiddleware>())
                .Use(sp.GetRequiredService<EffortLevelMiddleware>())
                .Use(sp.GetRequiredService<HookRefreshMiddleware>())
                .Use(sp.GetRequiredService<PermissionCacheMiddleware>())
                .WithHooks(sp)
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
                .WithHooks(sp)
                .Build());

        // Fork 分支管道
        services.AddSingleton<MiddlewarePipeline<ForkContext>>(sp =>
            new PipelineBuilder<ForkContext>()
                .Use(sp.GetRequiredService<ForkValidationMiddleware>())
                .Use(sp.GetRequiredService<ForkSpawnMiddleware>())
                .Use(sp.GetRequiredService<ForkPermissionMiddleware>())
                .Use(sp.GetRequiredService<ForkExecutionMiddleware>())
                .WithHooks(sp)
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
                .WithHooks(sp)
                .Build());

        // Shell 命令管道
        services.AddSingleton<MiddlewarePipeline<ShellPipelineContext>>(sp =>
            new PipelineBuilder<ShellPipelineContext>()
                .Use(sp.GetRequiredService<ShellValidationMiddleware>())
                .Use(sp.GetRequiredService<ShellPathGateMiddleware>())
                .Use(sp.GetRequiredService<ShellClassificationMiddleware>())
                .Use(sp.GetRequiredService<ShellSedInterceptMiddleware>())
                .Use(sp.GetRequiredService<ShellBackgroundMiddleware>())
                .Use(sp.GetRequiredService<ShellBuildInterceptMiddleware>())
                .Use(sp.GetRequiredService<ShellExecutionMiddleware>())
                .Use(sp.GetRequiredService<ShellOutputMiddleware>())
                .WithHooks(sp)
                .Build());

        // Skill 技能管道
        services.AddSingleton<MiddlewarePipeline<SkillContext>>(sp =>
            new PipelineBuilder<SkillContext>()
                .Use(sp.GetRequiredService<MetricsMiddleware<SkillContext>>())
                .Use(sp.GetRequiredService<SkillValidationMiddleware>())
                .Use(sp.GetRequiredService<SkillTelemetryMiddleware>())
                .Use(sp.GetRequiredService<SkillExecutionMiddleware>())
                .WithHooks(sp)
                .Build());

        // Code 代码管道
        services.AddSingleton<MiddlewarePipeline<CodeContext>>(sp =>
            new PipelineBuilder<CodeContext>()
                .Use(sp.GetRequiredService<CodeCacheMiddleware>())
                .Use(sp.GetRequiredService<CodeSecurityMiddleware>())
                .Use(sp.GetRequiredService<CodeLlmMiddleware>())
                .Use(sp.GetRequiredService<CodeSandboxMiddleware>())
                .Use(sp.GetRequiredService<MetricsMiddleware<CodeContext>>())
                .WithHooks(sp)
                .Build());

        return services;
    }
}