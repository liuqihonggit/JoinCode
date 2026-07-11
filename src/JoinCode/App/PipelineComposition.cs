namespace JoinCode.App;

/// <summary>
/// 管道组合根 — 洋葱模型，手动 .Use() 注册中间件，顺序肉眼可见
/// 对齐洋葱例子：注册即顺序，无需 Order 魔数
/// </summary>
public static class PipelineComposition
{
    /// <summary>
    /// 注册所有管道到 DI — 替代源码生成器的 AddAutoRegisteredPipelines
    /// </summary>
    public static IServiceCollection AddAllPipelines(this IServiceCollection services)
    {
        // 通用泛型中间件注册（工厂方式避免 IL2066）
        services.AddSingleton(sp => new MetricsMiddleware<WebContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<SkillContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<CodeContext>(sp.GetService<ITelemetryService>()));
        // ═══════════════════════════════════════════════════════════
        // Stream 管道
        // ═══════════════════════════════════════════════════════════

        // Chat 聊天管道 (Stream) — 限流: 每60s最多30次请求
        services.AddSingleton<StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>>(sp =>
            new StreamPipelineBuilder<ChatMiddlewareContext, ChatStreamEvent>()
                .Use(new FixedStreamRateLimitMiddleware<ChatMiddlewareContext, ChatStreamEvent>(30, TimeSpan.FromSeconds(60)))
                .Use(sp.GetRequiredService<ChatTimingMiddleware>())
                .Use(sp.GetRequiredService<ChatErrorHandlingMiddleware>())
                .Use(sp.GetRequiredService<AuditLogMiddleware>())
                .Use(sp.GetRequiredService<ChatTokenBudgetMiddleware>())
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
                .Use(sp.GetRequiredService<CompactTelemetryMiddleware>())
                .Use(sp.GetRequiredService<ContextCollapseMiddleware>())
                .Use(sp.GetRequiredService<MicrocompactMiddleware>())
                .Use(sp.GetRequiredService<SessionMemoryCompactMiddleware>())
                .Use(sp.GetRequiredService<ReactiveCompactMiddleware>())
                .WithHooks(sp)
                .Build());

        // Query 查询管道 — 限流: 每60s最多60次请求
        services.AddSingleton<MiddlewarePipeline<QueryMiddlewareContext>>(sp =>
            new PipelineBuilder<QueryMiddlewareContext>()
                .Use(new FixedRateLimitMiddleware<QueryMiddlewareContext>(60, TimeSpan.FromSeconds(60)))
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

        // Permission 权限管道 — 责任链模式：第一个设置 Result 的中间件短路后续
        services.AddSingleton<MiddlewarePipeline<PermissionCheckContext>>(sp =>
            new PipelineBuilder<PermissionCheckContext>()
                .WithShortCircuit(ctx => ctx.Result is not null)
                .Use(sp.GetRequiredService<BypassPermissionMiddleware>())
                .Use(sp.GetRequiredService<AgentRestrictionMiddleware>())
                .Use(sp.GetRequiredService<DeleteProtectionMiddleware>())
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

        // AgentSpawnCoord 协调层生成管道 — SpawnSubAgentAsync 的7步协调流程
        services.AddSingleton<MiddlewarePipeline<AgentSpawnCoordContext>>(sp =>
            new PipelineBuilder<AgentSpawnCoordContext>()
                .Use(sp.GetRequiredService<SpawnCoordLifecycleMiddleware>())
                .Use(sp.GetRequiredService<SpawnCoordWorktreeMiddleware>())
                .Use(sp.GetRequiredService<SpawnCoordRegisterMessageMiddleware>())
                .Use(sp.GetRequiredService<SpawnCoordRecordContextMiddleware>())
                .Use(sp.GetRequiredService<SpawnCoordPermissionRoutingMiddleware>())
                .Use(sp.GetRequiredService<SpawnCoordTeammatePaneMiddleware>())
                .WithHooks(sp)
                .Build());

        // AgentDispose 协调层释放管道 — DisposeAgentAsync 的5步协调流程
        services.AddSingleton<MiddlewarePipeline<AgentDisposeContext>>(sp =>
            new PipelineBuilder<AgentDisposeContext>()
                .Use(sp.GetRequiredService<DisposeUnregisterMessageMiddleware>())
                .Use(sp.GetRequiredService<DisposeWorktreeCleanupMiddleware>())
                .Use(sp.GetRequiredService<DisposeShellTasksMiddleware>())
                .Use(sp.GetRequiredService<DisposePaneMiddleware>())
                .Use(sp.GetRequiredService<DisposeLifecycleMiddleware>())
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

        // Web 网页管道 — 超时30s + 失败重试2次 + 熔断(连续5次失败冷却30s)
        services.AddSingleton<MiddlewarePipeline<WebContext>>(sp =>
            new PipelineBuilder<WebContext>()
                .Use(new FixedTimeoutMiddleware<WebContext>(TimeSpan.FromSeconds(30)))
                .Use(new FixedRetryMiddleware<WebContext>(2, ex => ex is HttpRequestException or TimeoutException))
                .Use(new FixedCircuitBreakerMiddleware<WebContext>(5, TimeSpan.FromSeconds(30)))
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

        // Shell 命令管道 — 超时120s
        services.AddSingleton<MiddlewarePipeline<ShellContext>>(sp =>
            new PipelineBuilder<ShellContext>()
                .Use(new FixedTimeoutMiddleware<ShellContext>(TimeSpan.FromSeconds(120)))
                .Use(sp.GetRequiredService<ShellValidationMiddleware>())
                .Use(sp.GetRequiredService<ShellClassificationMiddleware>())
                .Use(sp.GetRequiredService<ShellSedInterceptMiddleware>())
                .Use(sp.GetRequiredService<ShellBackgroundMiddleware>())
                .Use(sp.GetRequiredService<ShellBuildInterceptMiddleware>())
                .Use(sp.GetRequiredService<ShellExecutionMiddleware>())
                .Use(sp.GetRequiredService<ShellOutputMiddleware>())
                .WithHooks(sp)
                .Build());

        // Skill 技能管道 — 超时60s
        services.AddSingleton<MiddlewarePipeline<SkillContext>>(sp =>
            new PipelineBuilder<SkillContext>()
                .Use(new FixedTimeoutMiddleware<SkillContext>(TimeSpan.FromSeconds(60)))
                .Use(sp.GetRequiredService<MetricsMiddleware<SkillContext>>())
                .Use(sp.GetRequiredService<SkillValidationMiddleware>())
                .Use(sp.GetRequiredService<SkillTelemetryMiddleware>())
                .Use(sp.GetRequiredService<SkillExecutionMiddleware>())
                .WithHooks(sp)
                .Build());

        // Code 代码管道 — 超时120s（含LLM调用+沙箱执行）
        services.AddSingleton<MiddlewarePipeline<CodeContext>>(sp =>
            new PipelineBuilder<CodeContext>()
                .Use(new FixedTimeoutMiddleware<CodeContext>(TimeSpan.FromSeconds(120)))
                .Use(sp.GetRequiredService<CodeCacheMiddleware>())
                .Use(sp.GetRequiredService<CodeSecurityMiddleware>())
                .Use(sp.GetRequiredService<CodeLlmMiddleware>())
                .Use(sp.GetRequiredService<CodeSandboxMiddleware>())
                .Use(sp.GetRequiredService<MetricsMiddleware<CodeContext>>())
                .WithHooks(sp)
                .Build());

        // Dream 记忆整合管道 — 6步流程: 门控→扫描→注册→提示→LLM→记录
        services.AddSingleton<MiddlewarePipeline<JoinCode.Dream.Pipeline.DreamContext>>(sp =>
            new PipelineBuilder<JoinCode.Dream.Pipeline.DreamContext>()
                .WithShortCircuit(ctx => ctx.Result is not null)
                .Use(sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamGateCheckMiddleware>())
                .Use(sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamSessionScanMiddleware>())
                .Use(sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamTaskRegisterMiddleware>())
                .Use(sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamPromptBuildMiddleware>())
                .Use(sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamLlmConsolidateMiddleware>())
                .Use(sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamRecordTurnMiddleware>())
                .WithHooks(sp)
                .Build());

        return services;
    }

}
