namespace JoinCode.Pipelines;

/// <summary>
/// 数据驱动管道组装 — 从 YAML Profile 配置组装管道
/// </summary>
public static class DataDrivenPipelineComposition {

    /// <summary>
    /// 注册基础中间件服务 — 包含 CLI 独有聊天中间件、通用泛型中间件、CrashSnapshot 泛型中间件
    /// </summary>
    public static IServiceCollection RegisterBaseMiddlewareServices(this IServiceCollection services) {
        services.AddSingleton<ChatTimingMiddleware>();
        services.AddSingleton<ChatErrorHandlingMiddleware>();
        services.AddSingleton<AuditLogMiddleware>();
        services.AddSingleton<TokenBudgetMiddleware>();
        services.AddSingleton<MediaIntentDetector>();
        services.AddSingleton<ModalityValidationMiddleware>();

        services.AddSingleton(sp => new MetricsMiddleware<WebContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<SkillContext>(sp.GetService<ITelemetryService>()));
        services.AddSingleton(sp => new MetricsMiddleware<CodeContext>(sp.GetService<ITelemetryService>()));

        services.AddSingleton(sp => new CrashSnapshotMiddleware<PreprocessContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Preprocess"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<ChatInitContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "ChatInit"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<ChatAdminContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "ChatAdmin"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<CompactContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Compact"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<QueryMiddlewareContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Query"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<PermissionCheckContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Permission"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<SettingsContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Settings"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<UnifiedSpawnContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "UnifiedSpawn"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<AgentDisposeContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "AgentDispose"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<ForkContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Fork"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<WebContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Web"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<ShellPipelineContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Shell"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<SkillContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Skill"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<CodeContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Code"));
        services.AddSingleton(sp => new CrashSnapshotMiddleware<JoinCode.Dream.Pipeline.DreamContext>(sp.GetRequiredService<ICrashSnapshotStore>(), "Dream"));

        return services;
    }

    /// <summary>
    /// 构建所有节点工厂映射 — (consumerId:nodeId) → 服务实例工厂
    /// 同一节点ID在不同消费点中可能对应不同的泛型实例
    /// </summary>
    public static IEnumerable<KeyValuePair<string, Func<IServiceProvider, object>>> BuildNodeFactories() {
        // ═══════════════════════════════════════════════════════════
        // chat-pipeline (Stream) — IStreamMiddleware<ChatMiddlewareContext, ChatStreamEvent>
        // ═══════════════════════════════════════════════════════════
        yield return F("chat-pipeline:stream-crash-snapshot-mw", sp => sp.GetRequiredService<StreamCrashSnapshotMiddleware>());
        yield return F("chat-pipeline:stream-rate-limit-mw", _ => new FixedStreamRateLimitMiddleware<ChatMiddlewareContext, ChatStreamEvent>(30, TimeSpan.FromSeconds(60)));
        yield return F("chat-pipeline:chat-error-handling-mw", sp => sp.GetRequiredService<ChatErrorHandlingMiddleware>());
        yield return F("chat-pipeline:chat-timing-mw", sp => sp.GetRequiredService<ChatTimingMiddleware>());
        yield return F("chat-pipeline:token-budget-mw", sp => sp.GetRequiredService<TokenBudgetMiddleware>());
        yield return F("chat-pipeline:modality-validation-mw", sp => sp.GetRequiredService<ModalityValidationMiddleware>());
        yield return F("chat-pipeline:audit-log-mw", sp => sp.GetRequiredService<AuditLogMiddleware>());
        yield return F("chat-pipeline:transcript-persist-mw", sp => sp.GetRequiredService<TranscriptPersistMiddleware>());
        yield return F("chat-pipeline:pre-chat-mw", sp => sp.GetRequiredService<PreChatMiddleware>());
        yield return F("chat-pipeline:query-loop-mw", sp => sp.GetRequiredService<QueryLoopMiddleware>());
        yield return F("chat-pipeline:loop-intervention-mw", sp => sp.GetRequiredService<LoopInterventionMiddleware>());
        yield return F("chat-pipeline:process-usage-mw", sp => sp.GetRequiredService<ProcessUsageMiddleware>());
        yield return F("chat-pipeline:cleanup-injections-mw", sp => sp.GetRequiredService<CleanupInjectionsMiddleware>());
        yield return F("chat-pipeline:save-context-mw", sp => sp.GetRequiredService<SaveContextMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // preprocess-pipeline — IMiddleware<PreprocessContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("preprocess-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<PreprocessContext>>());
        yield return F("preprocess-pipeline:keyword-injection-mw", sp => sp.GetRequiredService<KeywordInjectionMiddleware>());
        yield return F("preprocess-pipeline:synonym-injection-mw", sp => sp.GetRequiredService<SynonymInjectionMiddleware>());
        yield return F("preprocess-pipeline:system-prompt-mw", sp => sp.GetRequiredService<SystemPromptMiddleware>());
        yield return F("preprocess-pipeline:reminder-injection-mw", sp => sp.GetRequiredService<ReminderInjectionMiddleware>());
        yield return F("preprocess-pipeline:tool-listing-injection-mw", sp => sp.GetRequiredService<ToolListingInjectionMiddleware>());
        yield return F("preprocess-pipeline:lsp-diagnostic-mw", sp => sp.GetRequiredService<LspDiagnosticMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // chatinit-pipeline — IMiddleware<ChatInitContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("chatinit-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<ChatInitContext>>());
        yield return F("chatinit-pipeline:context-load-mw", sp => sp.GetRequiredService<ContextLoadMiddleware>());
        yield return F("chatinit-pipeline:cost-restore-mw", sp => sp.GetRequiredService<CostRestoreMiddleware>());
        yield return F("chatinit-pipeline:config-change-start-mw", sp => sp.GetRequiredService<ConfigChangeStartMiddleware>());
        yield return F("chatinit-pipeline:session-start-hook-mw", sp => sp.GetRequiredService<SessionStartHookMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // chatadmin-pipeline — IMiddleware<ChatAdminContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("chatadmin-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<ChatAdminContext>>());
        yield return F("chatadmin-pipeline:session-admin-mw", sp => sp.GetRequiredService<SessionAdminMiddleware>());
        yield return F("chatadmin-pipeline:session-save-mw", sp => sp.GetRequiredService<SessionSaveMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // compact-pipeline — IMiddleware<CompactContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("compact-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<CompactContext>>());
        yield return F("compact-pipeline:compact-hook-mw", sp => sp.GetRequiredService<CompactHookMiddleware>());
        yield return F("compact-pipeline:compact-telemetry-mw", sp => sp.GetRequiredService<CompactTelemetryMiddleware>());
        yield return F("compact-pipeline:context-collapse-mw", sp => sp.GetRequiredService<ContextCollapseMiddleware>());
        yield return F("compact-pipeline:microcompact-mw", sp => sp.GetRequiredService<MicrocompactMiddleware>());
        yield return F("compact-pipeline:session-memory-compact-mw", sp => sp.GetRequiredService<SessionMemoryCompactMiddleware>());
        yield return F("compact-pipeline:reactive-compact-mw", sp => sp.GetRequiredService<ReactiveCompactMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // query-pipeline — IMiddleware<QueryMiddlewareContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("query-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<QueryMiddlewareContext>>());
        yield return F("query-pipeline:query-rate-limit-mw", _ => new FixedRateLimitMiddleware<QueryMiddlewareContext>(60, TimeSpan.FromSeconds(60)));
        yield return F("query-pipeline:usd-budget-mw", sp => sp.GetRequiredService<UsdBudgetMiddleware>());
        yield return F("query-pipeline:query-token-budget-mw", sp => sp.GetRequiredService<QueryTokenBudgetMiddleware>());
        yield return F("query-pipeline:cost-tracking-mw", sp => sp.GetRequiredService<CostTrackingMiddleware>());
        yield return F("query-pipeline:diminishing-returns-mw", sp => sp.GetRequiredService<DiminishingReturnsMiddleware>());
        yield return F("query-pipeline:history-snip-mw", sp => sp.GetRequiredService<HistorySnipMiddleware>());
        yield return F("query-pipeline:idle-reminder-mw", sp => sp.GetRequiredService<IdleReminderMiddleware>());
        yield return F("query-pipeline:stop-hook-mw", sp => sp.GetRequiredService<StopHookMiddleware>());
        yield return F("query-pipeline:state-transition-mw", sp => sp.GetRequiredService<StateTransitionMiddleware>());
        yield return F("query-pipeline:content-replacement-mw", sp => sp.GetRequiredService<ContentReplacementMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // permission-pipeline — IMiddleware<PermissionCheckContext> (短路)
        // ═══════════════════════════════════════════════════════════
        yield return F("permission-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<PermissionCheckContext>>());
        yield return F("permission-pipeline:bypass-permission-mw", sp => sp.GetRequiredService<BypassPermissionMiddleware>());
        yield return F("permission-pipeline:agent-restriction-mw", sp => sp.GetRequiredService<AgentRestrictionMiddleware>());
        yield return F("permission-pipeline:dangerous-command-mw", sp => sp.GetRequiredService<DangerousCommandProtectionMiddleware>());
        yield return F("permission-pipeline:auto-class-permission-mw", sp => sp.GetRequiredService<AutoClassifierMiddleware>());
        yield return F("permission-pipeline:config-permission-mw", sp => sp.GetRequiredService<ConfigGetOperationMiddleware>());
        yield return F("permission-pipeline:web-fetch-permission-mw", sp => sp.GetRequiredService<WebFetchPermissionMiddleware>());
        yield return F("permission-pipeline:path-deny-permission-mw", sp => sp.GetRequiredService<EarlyPathDenyMiddleware>());
        yield return F("permission-pipeline:tool-list-permission-mw", sp => sp.GetRequiredService<ToolListPermissionMiddleware>());
        yield return F("permission-pipeline:path-permission-mw", sp => sp.GetRequiredService<PathPermissionMiddleware>());
        yield return F("permission-pipeline:dangerous-op-permission-mw", sp => sp.GetRequiredService<DangerousOperationMiddleware>());
        yield return F("permission-pipeline:plan-mode-permission-mw", sp => sp.GetRequiredService<PlanModeMiddleware>());
        yield return F("permission-pipeline:default-permission-mw", sp => sp.GetRequiredService<DefaultResultMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // settings-pipeline — IMiddleware<SettingsContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("settings-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<SettingsContext>>());
        yield return F("settings-pipeline:settings-reload-mw", sp => sp.GetRequiredService<SettingsReloadMiddleware>());
        yield return F("settings-pipeline:effort-level-mw", sp => sp.GetRequiredService<EffortLevelMiddleware>());
        yield return F("settings-pipeline:hook-refresh-mw", sp => sp.GetRequiredService<HookRefreshMiddleware>());
        yield return F("settings-pipeline:permission-cache-mw", sp => sp.GetRequiredService<PermissionCacheMiddleware>());
        yield return F("settings-pipeline:tool-score-settings-mw", sp => sp.GetRequiredService<ToolScoreSettingsMiddleware>());
        yield return F("settings-pipeline:subagent-concurrency-mw", sp => sp.GetRequiredService<SubAgentConcurrencyMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // unified-spawn-pipeline — IMiddleware<UnifiedSpawnContext> (自定义 LoggingScope)
        // ═══════════════════════════════════════════════════════════
        yield return F("unified-spawn-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<UnifiedSpawnContext>>());
        yield return F("unified-spawn-pipeline:definition-resolution-mw", sp => sp.GetRequiredService<DefinitionResolutionMiddleware>());
        yield return F("unified-spawn-pipeline:prompt-building-mw", sp => sp.GetRequiredService<PromptBuildingMiddleware>());
        yield return F("unified-spawn-pipeline:context-setup-mw", sp => sp.GetRequiredService<ContextSetupMiddleware>());
        yield return F("unified-spawn-pipeline:lifecycle-spawn-mw", sp => sp.GetRequiredService<LifecycleSpawnMiddleware>());
        yield return F("unified-spawn-pipeline:worktree-spawn-mw", sp => sp.GetRequiredService<WorktreeSpawnMiddleware>());
        yield return F("unified-spawn-pipeline:record-context-mw", sp => sp.GetRequiredService<RecordContextMiddleware>());
        yield return F("unified-spawn-pipeline:register-message-mw", sp => sp.GetRequiredService<RegisterMessageMiddleware>());
        yield return F("unified-spawn-pipeline:hook-setup-mw", sp => sp.GetRequiredService<HookSetupMiddleware>());
        yield return F("unified-spawn-pipeline:mcp-setup-mw", sp => sp.GetRequiredService<McpSetupMiddleware>());
        yield return F("unified-spawn-pipeline:permission-routing-mw", sp => sp.GetRequiredService<PermissionRoutingMiddleware>());
        yield return F("unified-spawn-pipeline:teammate-pane-mw", sp => sp.GetRequiredService<TeammatePaneMiddleware>());
        yield return F("unified-spawn-pipeline:metadata-mw", sp => sp.GetRequiredService<MetadataMiddleware>());
        yield return F("unified-spawn-pipeline:transcript-mw", sp => sp.GetRequiredService<TranscriptMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // agent-dispose-pipeline — IMiddleware<AgentDisposeContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("agent-dispose-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<AgentDisposeContext>>());
        yield return F("agent-dispose-pipeline:dispose-unregister-message-mw", sp => sp.GetRequiredService<DisposeUnregisterMessageMiddleware>());
        yield return F("agent-dispose-pipeline:dispose-worktree-cleanup-mw", sp => sp.GetRequiredService<DisposeWorktreeCleanupMiddleware>());
        yield return F("agent-dispose-pipeline:dispose-shell-tasks-mw", sp => sp.GetRequiredService<DisposeShellTasksMiddleware>());
        yield return F("agent-dispose-pipeline:dispose-pane-mw", sp => sp.GetRequiredService<DisposePaneMiddleware>());
        yield return F("agent-dispose-pipeline:dispose-lifecycle-mw", sp => sp.GetRequiredService<DisposeLifecycleMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // fork-pipeline — IMiddleware<ForkContext> (自定义 LoggingScope)
        // ═══════════════════════════════════════════════════════════
        yield return F("fork-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<ForkContext>>());
        yield return F("fork-pipeline:fork-validation-mw", sp => sp.GetRequiredService<ForkValidationMiddleware>());
        yield return F("fork-pipeline:fork-spawn-mw", sp => sp.GetRequiredService<ForkSpawnMiddleware>());
        yield return F("fork-pipeline:fork-permission-mw", sp => sp.GetRequiredService<ForkPermissionMiddleware>());
        yield return F("fork-pipeline:fork-execution-mw", sp => sp.GetRequiredService<ForkExecutionMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // web-pipeline — IMiddleware<WebContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("web-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<WebContext>>());
        yield return F("web-pipeline:web-timeout-mw", _ => new FixedTimeoutMiddleware<WebContext>(TimeSpan.FromSeconds(30)));
        yield return F("web-pipeline:web-retry-mw", _ => new FixedRetryMiddleware<WebContext>(2, ex => ex is HttpRequestException or TimeoutException));
        yield return F("web-pipeline:web-circuit-breaker-mw", _ => new FixedCircuitBreakerMiddleware<WebContext>(5, TimeSpan.FromSeconds(30)));
        yield return F("web-pipeline:web-metrics-mw", sp => sp.GetRequiredService<MetricsMiddleware<WebContext>>());
        yield return F("web-pipeline:web-validation-mw", sp => sp.GetRequiredService<WebValidationMiddleware>());
        yield return F("web-pipeline:web-ssrf-guard-mw", sp => sp.GetRequiredService<WebSsrfGuardMiddleware>());
        yield return F("web-pipeline:web-cache-check-mw", sp => sp.GetRequiredService<WebCacheCheckMiddleware>());
        yield return F("web-pipeline:web-domain-check-mw", sp => sp.GetRequiredService<WebDomainCheckMiddleware>());
        yield return F("web-pipeline:web-fetch-mw", sp => sp.GetRequiredService<WebFetchMiddleware>());
        yield return F("web-pipeline:web-content-processing-mw", sp => sp.GetRequiredService<WebContentProcessingMiddleware>());
        yield return F("web-pipeline:web-cache-write-mw", sp => sp.GetRequiredService<WebCacheWriteMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // shell-pipeline — IMiddleware<ShellPipelineContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("shell-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<ShellPipelineContext>>());
        yield return F("shell-pipeline:shell-timeout-keyword-mw", sp => sp.GetRequiredService<ShellTimeoutKeywordMiddleware>());
        yield return F("shell-pipeline:absolute-timeout-mw", sp => sp.GetRequiredService<AbsoluteTimeoutMiddleware>());
        yield return F("shell-pipeline:shell-validate-mw", sp => sp.GetRequiredService<ShellValidationMiddleware>());
        yield return F("shell-pipeline:shell-intercept-mw", sp => sp.GetRequiredService<ShellCommandInterceptionMiddleware>());
        yield return F("shell-pipeline:shell-path-gate-mw", sp => sp.GetRequiredService<ShellPathGateMiddleware>());
        yield return F("shell-pipeline:shell-classify-mw", sp => sp.GetRequiredService<ShellClassificationMiddleware>());
        yield return F("shell-pipeline:shell-sed-intercept-mw", sp => sp.GetRequiredService<ShellSedInterceptMiddleware>());
        yield return F("shell-pipeline:shell-background-mw", sp => sp.GetRequiredService<ShellBackgroundMiddleware>());
        yield return F("shell-pipeline:shell-build-intercept-mw", sp => sp.GetRequiredService<ShellBuildInterceptMiddleware>());
        yield return F("shell-pipeline:shell-download-hint-mw", sp => sp.GetRequiredService<ShellDownloadHintMiddleware>());
        yield return F("shell-pipeline:shell-execute-mw", sp => sp.GetRequiredService<ShellExecutionMiddleware>());
        yield return F("shell-pipeline:shell-output-mw", sp => sp.GetRequiredService<ShellOutputMiddleware>());
        yield return F("shell-pipeline:shell-perturbation-audit-mw", sp => sp.GetRequiredService<ShellPerturbationAuditMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // skill-pipeline — IMiddleware<SkillContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("skill-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<SkillContext>>());
        yield return F("skill-pipeline:skill-timeout-mw", _ => new FixedTimeoutMiddleware<SkillContext>(TimeSpan.FromSeconds(60)));
        yield return F("skill-pipeline:skill-metrics-mw", sp => sp.GetRequiredService<MetricsMiddleware<SkillContext>>());
        yield return F("skill-pipeline:skill-validation-mw", sp => sp.GetRequiredService<SkillValidationMiddleware>());
        yield return F("skill-pipeline:skill-telemetry-mw", sp => sp.GetRequiredService<SkillTelemetryMiddleware>());
        yield return F("skill-pipeline:skill-execution-mw", sp => sp.GetRequiredService<SkillExecutionMiddleware>());

        // ═══════════════════════════════════════════════════════════
        // code-pipeline — IMiddleware<CodeContext>
        // ═══════════════════════════════════════════════════════════
        yield return F("code-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<CodeContext>>());
        yield return F("code-pipeline:code-timeout-mw", _ => new FixedTimeoutMiddleware<CodeContext>(TimeSpan.FromSeconds(120)));
        yield return F("code-pipeline:code-cache-mw", sp => sp.GetRequiredService<CodeCacheMiddleware>());
        yield return F("code-pipeline:code-security-mw", sp => sp.GetRequiredService<CodeSecurityMiddleware>());
        yield return F("code-pipeline:code-llm-mw", sp => sp.GetRequiredService<CodeLlmMiddleware>());
        yield return F("code-pipeline:code-sandbox-mw", sp => sp.GetRequiredService<CodeSandboxMiddleware>());
        yield return F("code-pipeline:code-metrics-mw", sp => sp.GetRequiredService<MetricsMiddleware<CodeContext>>());

        // ═══════════════════════════════════════════════════════════
        // dream-pipeline — IMiddleware<DreamContext> (短路)
        // ═══════════════════════════════════════════════════════════
        yield return F("dream-pipeline:crash-snapshot-mw", sp => sp.GetRequiredService<CrashSnapshotMiddleware<JoinCode.Dream.Pipeline.DreamContext>>());
        yield return F("dream-pipeline:dream-gate-check-mw", sp => sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamGateCheckMiddleware>());
        yield return F("dream-pipeline:dream-session-scan-mw", sp => sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamSessionScanMiddleware>());
        yield return F("dream-pipeline:dream-task-register-mw", sp => sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamTaskRegisterMiddleware>());
        yield return F("dream-pipeline:dream-prompt-build-mw", sp => sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamPromptBuildMiddleware>());
        yield return F("dream-pipeline:dream-llm-consolidate-mw", sp => sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamLlmConsolidateMiddleware>());
        yield return F("dream-pipeline:dream-record-turn-mw", sp => sp.GetRequiredService<JoinCode.Dream.Pipeline.DreamRecordTurnMiddleware>());
    }

    /// <summary>
    /// 数据驱动的管道组装 — 从 YAML Profile 配置组装所有16条管道
    /// </summary>
    /// <param name="services">DI 容器</param>
    /// <param name="profileName">Profile 名称（如 base/headless/sdk/ci）</param>
    public static IServiceCollection AddDataDrivenPipelines(this IServiceCollection services, string profileName) {
        // 注册基础中间件服务（新旧路径共享）
        services.RegisterBaseMiddlewareServices();

        // 注册 NodeServiceResolver
        services.AddSingleton<NodeServiceResolver>(_ => NodeServiceResolver.Build(BuildNodeFactories()));

        // 注册 ResolvedProfile
        services.AddSingleton<ResolvedProfile>(sp => {
            var resolver = sp.GetRequiredService<NodeProfileResolver>();
            return resolver.Resolve(profileName);
        });

        // ═══════════════════════════════════════════════════════════
        // Stream 管道
        // ═══════════════════════════════════════════════════════════

        // Chat 聊天管道 (Stream)
        services.AddSingleton<StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>>(sp =>
            BuildStreamPipeline<ChatMiddlewareContext, ChatStreamEvent>(sp, "chat-pipeline"));

        // ═══════════════════════════════════════════════════════════
        // Task 管道
        // ═══════════════════════════════════════════════════════════

        // Preprocess 预处理管道
        services.AddSingleton<MiddlewarePipeline<PreprocessContext>>(sp =>
            BuildTaskPipeline<PreprocessContext>(sp, "preprocess-pipeline"));

        // ChatInit 聊天初始化管道
        services.AddSingleton<MiddlewarePipeline<ChatInitContext>>(sp =>
            BuildTaskPipeline<ChatInitContext>(sp, "chatinit-pipeline"));

        // ChatAdmin 管理管道
        services.AddSingleton<MiddlewarePipeline<ChatAdminContext>>(sp =>
            BuildTaskPipeline<ChatAdminContext>(sp, "chatadmin-pipeline"));

        // Compact 压缩管道
        services.AddSingleton<MiddlewarePipeline<CompactContext>>(sp =>
            BuildTaskPipeline<CompactContext>(sp, "compact-pipeline"));

        // Query 查询管道
        services.AddSingleton<MiddlewarePipeline<QueryMiddlewareContext>>(sp =>
            BuildTaskPipeline<QueryMiddlewareContext>(sp, "query-pipeline"));

        // Permission 权限管道 (短路)
        services.AddSingleton<MiddlewarePipeline<PermissionCheckContext>>(sp =>
            BuildTaskPipeline<PermissionCheckContext>(sp, "permission-pipeline",
                shortCircuit: ctx => ctx.Result is not null));

        // Settings 设置管道
        services.AddSingleton<MiddlewarePipeline<SettingsContext>>(sp =>
            BuildTaskPipeline<SettingsContext>(sp, "settings-pipeline"));

        // UnifiedSpawn 统一管道 (自定义 LoggingScope)
        services.AddSingleton<MiddlewarePipeline<UnifiedSpawnContext>>(sp =>
            BuildTaskPipeline<UnifiedSpawnContext>(sp, "unified-spawn-pipeline",
                objectIdSelector: ctx => ctx.Agent?.ObjectId ?? ObjectId.Empty));

        // AgentDispose 释放管道
        services.AddSingleton<MiddlewarePipeline<AgentDisposeContext>>(sp =>
            BuildTaskPipeline<AgentDisposeContext>(sp, "agent-dispose-pipeline"));

        // Fork 分支管道 (自定义 LoggingScope)
        services.AddSingleton<MiddlewarePipeline<ForkContext>>(sp =>
            BuildTaskPipeline<ForkContext>(sp, "fork-pipeline",
                objectIdSelector: ctx => ctx.Agent?.ObjectId ?? ObjectId.Empty));

        // Web 网页管道
        services.AddSingleton<MiddlewarePipeline<WebContext>>(sp =>
            BuildTaskPipeline<WebContext>(sp, "web-pipeline"));

        // Shell 命令管道
        services.AddSingleton<MiddlewarePipeline<ShellPipelineContext>>(sp =>
            BuildTaskPipeline<ShellPipelineContext>(sp, "shell-pipeline"));

        // Skill 技能管道
        services.AddSingleton<MiddlewarePipeline<SkillContext>>(sp =>
            BuildTaskPipeline<SkillContext>(sp, "skill-pipeline"));

        // Code 代码管道
        services.AddSingleton<MiddlewarePipeline<CodeContext>>(sp =>
            BuildTaskPipeline<CodeContext>(sp, "code-pipeline"));

        // Dream 记忆整合管道 (短路)
        services.AddSingleton<MiddlewarePipeline<JoinCode.Dream.Pipeline.DreamContext>>(sp =>
            BuildTaskPipeline<JoinCode.Dream.Pipeline.DreamContext>(sp, "dream-pipeline",
                shortCircuit: ctx => ctx.Result is not null));

        return services;
    }

    /// <summary>
    /// 数据驱动构建 Task 管道 — 从 Profile 解析中间件列表，通过 NodeServiceResolver 解析实例
    /// </summary>
    private static MiddlewarePipeline<TContext> BuildTaskPipeline<TContext>(
        IServiceProvider sp, string consumerId,
        Func<TContext, bool>? shortCircuit = null,
        Func<TContext, ObjectId>? objectIdSelector = null) {
        var profile = sp.GetRequiredService<ResolvedProfile>();
        var resolver = sp.GetRequiredService<NodeServiceResolver>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

        var middlewares = ResolveMiddlewares<IMiddleware<TContext>>(sp, resolver, profile, consumerId);

        var builder = new PipelineBuilder<TContext>();

        if (objectIdSelector is not null)
            builder.WithLoggingScope(objectIdSelector, loggerFactory);
        else
            builder.WithLoggingScope(loggerFactory);

        if (shortCircuit is not null)
            builder.WithShortCircuit(shortCircuit);

        builder.UseRange(middlewares);
        builder.WithHooks(sp);

        return builder.Build();
    }

    /// <summary>
    /// 数据驱动构建 Stream 管道
    /// </summary>
    private static StreamMiddlewarePipeline<TContext, TEvent> BuildStreamPipeline<TContext, TEvent>(
        IServiceProvider sp, string consumerId) {
        var profile = sp.GetRequiredService<ResolvedProfile>();
        var resolver = sp.GetRequiredService<NodeServiceResolver>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

        var middlewares = ResolveMiddlewares<IStreamMiddleware<TContext, TEvent>>(sp, resolver, profile, consumerId);

        var builder = new StreamPipelineBuilder<TContext, TEvent>()
            .WithLoggingScope(loggerFactory)
            .UseRange(middlewares)
            .WithHooks(sp);

        return builder.Build();
    }

    /// <summary>
    /// 从 Profile + NodeServiceResolver 解析中间件列表
    /// </summary>
    private static List<TMiddleware> ResolveMiddlewares<TMiddleware>(
        IServiceProvider sp, NodeServiceResolver resolver, ResolvedProfile profile, string consumerId)
        where TMiddleware : class {
        if (!profile.Consumers.TryGetValue(consumerId, out var consumerConfig))
            throw new InvalidOperationException($"Consumer '{consumerId}' not found in profile '{profile.Name}'");

        var result = new List<TMiddleware>();
        foreach (var nodeEntry in consumerConfig.Nodes.OrderBy(n => n.Priority)) {
            if (resolver.TryResolve<TMiddleware>(consumerId, nodeEntry.Id, sp, out var middleware))
                result.Add(middleware);
        }
        return result;
    }

    /// <summary>工厂映射辅助方法</summary>
    private static KeyValuePair<string, Func<IServiceProvider, object>> F(string key, Func<IServiceProvider, object> factory)
        => new(key, factory);
}
