namespace Integration.Tests.McpTools;

public static class TestPipelineRegistration
{
    public static IServiceCollection AddTestPipelines(this IServiceCollection services)
    {
        services.AddSingleton(sp => new MetricsMiddleware<WebContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<SkillContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<CodeContext>(sp.GetService<ITelemetryService>()));

        services.AddSingleton<StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>>(sp =>
            new StreamPipelineBuilder<ChatMiddlewareContext, ChatStreamEvent>()
                .Use(sp.GetRequiredService<PreChatMiddleware>())
                .Use(sp.GetRequiredService<QueryLoopMiddleware>())
                .Use(sp.GetRequiredService<LoopInterventionMiddleware>())
                .Use(sp.GetRequiredService<ProcessUsageMiddleware>())
                .Use(sp.GetRequiredService<CleanupInjectionsMiddleware>())
                .Use(sp.GetRequiredService<SaveContextMiddleware>())
                .Build());

        services.AddSingleton<MiddlewarePipeline<PreprocessContext>>(sp =>
            new PipelineBuilder<PreprocessContext>()
                .Use(sp.GetRequiredService<KeywordInjectionMiddleware>())
                .Use(sp.GetRequiredService<SynonymInjectionMiddleware>())
                .Use(sp.GetRequiredService<SystemPromptMiddleware>())
                .Use(sp.GetRequiredService<ReminderInjectionMiddleware>())
                .Use(sp.GetRequiredService<ToolListingInjectionMiddleware>())
                .Use(sp.GetRequiredService<LspDiagnosticMiddleware>())
                .Build());

        services.AddSingleton<MiddlewarePipeline<ChatInitContext>>(sp =>
            new PipelineBuilder<ChatInitContext>()
                .Use(sp.GetRequiredService<ContextLoadMiddleware>())
                .Use(sp.GetRequiredService<CostRestoreMiddleware>())
                .Use(sp.GetRequiredService<ConfigChangeStartMiddleware>())
                .Use(sp.GetRequiredService<SessionStartHookMiddleware>())
                .Build());

        services.AddSingleton<MiddlewarePipeline<ChatAdminContext>>(sp =>
            new PipelineBuilder<ChatAdminContext>()
                .Use(sp.GetRequiredService<SessionAdminMiddleware>())
                .Use(sp.GetRequiredService<SessionSaveMiddleware>())
                .Build());

        services.AddSingleton<MiddlewarePipeline<CompactContext>>(sp =>
            new PipelineBuilder<CompactContext>()
                .Use(sp.GetRequiredService<CompactHookMiddleware>())
                .Use(sp.GetRequiredService<ContextCollapseMiddleware>())
                .Use(sp.GetRequiredService<MicrocompactMiddleware>())
                .Use(sp.GetRequiredService<SessionMemoryCompactMiddleware>())
                .Use(sp.GetRequiredService<ReactiveCompactMiddleware>())
                .Build());

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
                .Build());

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
                .Build());

        services.AddSingleton<MiddlewarePipeline<SettingsContext>>(sp =>
            new PipelineBuilder<SettingsContext>()
                .Use(sp.GetRequiredService<SettingsReloadMiddleware>())
                .Use(sp.GetRequiredService<EffortLevelMiddleware>())
                .Use(sp.GetRequiredService<HookRefreshMiddleware>())
                .Use(sp.GetRequiredService<PermissionCacheMiddleware>())
                .Build());

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
                .Build());

        services.AddSingleton<MiddlewarePipeline<ForkContext>>(sp =>
            new PipelineBuilder<ForkContext>()
                .Use(sp.GetRequiredService<ForkValidationMiddleware>())
                .Use(sp.GetRequiredService<ForkSpawnMiddleware>())
                .Use(sp.GetRequiredService<ForkPermissionMiddleware>())
                .Use(sp.GetRequiredService<ForkExecutionMiddleware>())
                .Build());

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
                .Build());

        services.AddSingleton<MiddlewarePipeline<ShellContext>>(sp =>
            new PipelineBuilder<ShellContext>()
                .Use(sp.GetRequiredService<ShellValidationMiddleware>())
                .Use(sp.GetRequiredService<ShellClassificationMiddleware>())
                .Use(sp.GetRequiredService<ShellSedInterceptMiddleware>())
                .Use(sp.GetRequiredService<ShellBackgroundMiddleware>())
                .Use(sp.GetRequiredService<ShellBuildInterceptMiddleware>())
                .Use(sp.GetRequiredService<ShellExecutionMiddleware>())
                .Use(sp.GetRequiredService<ShellOutputMiddleware>())
                .Build());

        services.AddSingleton<MiddlewarePipeline<SkillContext>>(sp =>
            new PipelineBuilder<SkillContext>()
                .Use(sp.GetRequiredService<MetricsMiddleware<SkillContext>>())
                .Use(sp.GetRequiredService<SkillValidationMiddleware>())
                .Use(sp.GetRequiredService<SkillTelemetryMiddleware>())
                .Use(sp.GetRequiredService<SkillExecutionMiddleware>())
                .Build());

        services.AddSingleton<MiddlewarePipeline<CodeContext>>(sp =>
            new PipelineBuilder<CodeContext>()
                .Use(sp.GetRequiredService<CodeCacheMiddleware>())
                .Use(sp.GetRequiredService<CodeSecurityMiddleware>())
                .Use(sp.GetRequiredService<CodeLlmMiddleware>())
                .Use(sp.GetRequiredService<CodeSandboxMiddleware>())
                .Use(sp.GetRequiredService<MetricsMiddleware<CodeContext>>())
                .Build());

        return services;
    }
}
