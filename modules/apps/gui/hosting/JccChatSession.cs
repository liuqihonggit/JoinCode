namespace JoinCode.Gui.Hosting;

/// <summary>
/// 引擎会话实现 — 进程内组装真实 AI 工作流（AddAiWorkflowServices + 共享管道），
/// 替代骨架阶段的占位回显。UI 与引擎解耦的唯一边界仍为 <c>IJccChatSession</c>。
/// 引擎具体组装收敛在本类，不蔓延到 Views/ViewModels。
/// </summary>
internal sealed class JccChatSession : IJccChatSession {
    private readonly IServiceProvider _services;
    private readonly IChatService _chat;
    private readonly JoinCode.Abstractions.Configuration.WorkflowConfig _config;
    private readonly IExecutionSettingsProvider? _executionSettings;
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly Func<ValueTask>? _disposeAsync;

    /// <summary>引擎侧权限确认器 — 宿主注入的委托在此转发（引擎确认点由 GuiPermissionConfirmationHandler 转接）</summary>
    private readonly GuiPermissionConfirmationHandler? _enginePermissionHandler;

    private Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? _permissionConfirmationHandler;

    /// <inheritdoc />
    public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler {
        get => _permissionConfirmationHandler;
        set {
            _permissionConfirmationHandler = value;
            if (_enginePermissionHandler is not null)
                _enginePermissionHandler.Handler = value;
        }
    }

    /// <summary>
    /// AskUserQuestion 弹窗回调 — 由 MainWindow 在初始化时设置。
    /// AvaloniaInteractiveService 通过此回调在 UI 线程弹出对话框。
    /// </summary>
    public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback {
        get => _interactiveService?.ShowDialogCallback;
        set { if (_interactiveService is not null) _interactiveService.ShowDialogCallback = value; }
    }

    private readonly AvaloniaInteractiveService? _interactiveService;

    internal JccChatSession(
        IServiceProvider services,
        IChatService chat,
        JoinCode.Abstractions.Configuration.WorkflowConfig config,
        IExecutionSettingsProvider? executionSettings = null,
        IModelConfigLoader? modelConfigLoader = null,
        Func<ValueTask>? disposeAsync = null) {
        _services = services;
        _chat = chat;
        _config = config;
        _executionSettings = executionSettings;
        _modelConfigLoader = modelConfigLoader ?? services.GetService<IModelConfigLoader>() ?? new ModelConfigLoader();
        _disposeAsync = disposeAsync;

        _interactiveService = services.GetService<IInteractiveService>() as AvaloniaInteractiveService;
        _enginePermissionHandler = services.GetService<IPermissionConfirmationHandler>() as GuiPermissionConfirmationHandler;

        // 订阅 settings.json 变更 — theme 键变更时触发 ThemeChanged 驱动 GUI 热重载（双向绑定）
        var configService = services.GetService<IConfigurationService>();
        if (configService is not null)
            configService.SettingChanged += OnSettingChanged;

        VendorModelMap = BuildVendorModelMap();
    }

    /// <inheritdoc />
    public event EventHandler<ThemeKind>? ThemeChanged;

    /// <summary>
    /// 获取后台子代理快照 — IAgentService 运行列表 + 活跃 fork 归并（forkId 亦为合法终止键）
    /// </summary>
    public async Task<IReadOnlyList<BackgroundAgentInfo>> GetBackgroundAgentsAsync(CancellationToken cancellationToken = default) {
        var result = new List<BackgroundAgentInfo>();

        var agentService = _services.GetService<IAgentService>();
        if (agentService is not null) {
            foreach (var info in await agentService.GetRunningAgentsAsync(cancellationToken)) {
                result.Add(new BackgroundAgentInfo(
                    info.Id,
                    Name: info.DisplayName ?? info.Variant?.ToString() ?? info.Role.ToString().ToLowerInvariant(),
                    Description: info.Description,
                    State: info.State,
                    StartedAt: info.StartedAt,
                    ToolUseCount: info.ToolUseCount,
                    TokenCount: info.TokenCount,
                    Activities: info.Activities,
                    LastActivityText: info.LastActivityText,
                    FinalOutput: info.FinalOutput,
                    IsSuccess: info.IsSuccess,
                    ExecutionTimeMs: info.ExecutionTimeMs,
                    Role: info.Role.ToString().ToLowerInvariant()));
            }
        }

        var forkManager = _services.GetService<IForkSubAgentManager>();
        if (forkManager is not null) {
            foreach (var fork in await forkManager.GetActiveForksAsync(cancellationToken)) {
                if (fork.State != ForkState.Running)
                    continue;
                result.Add(new BackgroundAgentInfo(
                    fork.ForkId,
                    Name: "fork",
                    Description: $"fork {fork.ForkId}",
                    State: AgentStatus.Running,
                    StartedAt: fork.CreatedAt,
                    ToolUseCount: 0,
                    TokenCount: 0,
                    Activities: Array.Empty<AgentActivityEntry>(),
                    LastActivityText: null,
                    FinalOutput: fork.Result,
                    IsSuccess: fork.State == ForkState.Completed ? true : null,
                    ExecutionTimeMs: null,
                    Role: "fork"));
            }
        }

        return result;
    }

    /// <summary>终止后台代理 — 先按 agentId 停止；未命中且是活跃 teammate 时停止之；再未命中且是活跃 fork 时取消之</summary>
    public async Task<bool> StopBackgroundAgentAsync(string agentId, CancellationToken cancellationToken = default) {
        var agentService = _services.GetService<IAgentService>();
        if (agentService is not null && await agentService.StopAgentAsync(agentId, cancellationToken))
            return true;

        var teammateExecutor = _services.GetService<IInProcessTeammateTaskExecutor>();
        if (teammateExecutor is not null) {
            var teammates = await teammateExecutor.GetActiveTeammatesAsync(cancellationToken);
            if (teammates.Contains(agentId)) {
                await teammateExecutor.StopTeammateAsync(agentId, cancellationToken);
                return true;
            }
        }

        var forkManager = _services.GetService<IForkSubAgentManager>();
        if (forkManager is not null) {
            var forks = await forkManager.GetActiveForksAsync(cancellationToken);
            if (forks.Any(f => f.ForkId == agentId)) {
                await forkManager.CancelForkAsync(agentId, cancellationToken);
                return true;
            }
        }

        return false;
    }

    /// <summary>中断子代理当前 work（非终止）— 委托 teammateExecutor.InterruptTeammateAsync，teammate 进 idle 等 next prompt</summary>
    public async Task<bool> InterruptSubAgentAsync(string agentId, CancellationToken cancellationToken = default) {
        var teammateExecutor = _services.GetService<IInProcessTeammateTaskExecutor>();
        if (teammateExecutor is null)
            return false;
        return await teammateExecutor.InterruptTeammateAsync(agentId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> FindSubAgentIdByNameAsync(string name, CancellationToken cancellationToken = default) {
        var agentService = _services.GetService<IAgentService>();
        if (agentService is null)
            return null;
        return await agentService.FindAgentIdByNameAsync(name, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ForwardInputToSubAgentAsync(string agentId, string message, CancellationToken cancellationToken = default) {
        var agentService = _services.GetService<IAgentService>();
        if (agentService is null)
            return false;
        return await agentService.ForwardUserInputToAgentAsync(agentId, message, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> GetSubAgentWorktreePathAsync(string agentId, CancellationToken cancellationToken = default) {
        var agentService = _services.GetService<IAgentService>();
        if (agentService is null)
            return null;
        return await agentService.GetAgentWorktreePathAsync(agentId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubSessionInfo>> GetSubSessionsAsync(string parentSessionId, CancellationToken cancellationToken = default) {
        var result = new List<SubSessionInfo>();

        // teammate 子会话（GUI 子代理走 teammate 模型，支持 Interrupt 中断 + idle 恢复）
        var teammateExecutor = _services.GetService<IInProcessTeammateTaskExecutor>();
        if (teammateExecutor is not null) {
            var snapshots = await teammateExecutor.GetActiveTeammateSnapshotsAsync(cancellationToken);
            foreach (var s in snapshots) {
                if (s.ParentSessionId != parentSessionId)
                    continue;
                var title = $"子会话 {s.TeammateId.AsSpan(0, Math.Min(8, s.TeammateId.Length)).ToString()}";
                var worktree = await GetSubAgentWorktreePathAsync(s.TeammateId, cancellationToken);
                var state = s.IsIdle ? "Idle" : "Running";
                result.Add(new SubSessionInfo(s.TeammateId, s.ParentSessionId ?? parentSessionId, title, state, worktree));
            }
        }

        // fork 子会话（保留 — fork 其他消费方如 MagicDocsManager 创建的仍需显示）
        var forkManager = _services.GetService<IForkSubAgentManager>();
        if (forkManager is not null) {
            var forks = await forkManager.GetActiveForksAsync(cancellationToken);
            foreach (var f in forks) {
                if (f.ParentSessionId != parentSessionId)
                    continue;
                var title = $"子会话 {f.ForkId.AsSpan(0, Math.Min(8, f.ForkId.Length)).ToString()}";
                var worktree = await GetSubAgentWorktreePathAsync(f.ForkId, cancellationToken);
                result.Add(new SubSessionInfo(f.ForkId, f.ParentSessionId, title, f.State.ToString(), worktree));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public void UpdateToolBlacklist(HashSet<string> blacklistedTools) {
        var monitor = _services.GetService<IToolHealthMonitor>();
        monitor?.UpdateBlacklist(blacklistedTools);
    }

    /// <inheritdoc />
    public void UpdateProtectedDrives(HashSet<string> protectedDrives) {
        var store = _services.GetService<Core.Hooks.Execution.Interception.IProtectedDriveStore>();
        store?.Update(protectedDrives.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteToolAsync(string toolName, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default) {
        var registry = _services.GetService<IToolRegistry>();
        if (registry is null)
            return new ToolResult { IsError = true, Content = [new() { Text = "引擎未就绪：工具注册表不可用" }] };
        return await registry.ExecuteToolAsync(toolName, arguments, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default) {
        var registry = _services.GetService<IToolRegistry>();
        if (registry is null)
            return null;
        return await registry.GetToolInfoAsync(toolName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default) {
        var manager = _services.GetService<IToolPermissionManager>();
        if (manager is not null)
            await manager.SetPermissionModeAsync(mode, cancellationToken);
    }

    /// <summary>settings.json 变更转发 — theme 键变更时解析为 ThemeKind 并触发 ThemeChanged</summary>
    private void OnSettingChanged(object? sender, SettingChangeEventArgs e) {
        if (e.Key == ConfigKeyEnumConstants.Theme && e.NewValue is not null) {
            var theme = ThemeKindExtensions.FromValue(e.NewValue) ?? ThemeKind.Auto;
            ThemeChanged?.Invoke(this, theme);
        }
    }

    /// <summary>
    /// 创建引擎会话：调用引擎侧 EngineSessionFactory 一行完成 LoadConfig+BuildHost+ConfigureModules，
    /// 消除 GUI 和 CLI 的双引擎初始化差异。
    /// ADR 0143 Step 4：gui 切直引 engine 后不再传 cli 的 CliRegistrationModule——
    /// HOST 25 类型全是终端交互/pipe/CLI 启动步骤形态，Avalonia GUI 有自己的交互栈。
    /// </summary>
    public static async Task<IJccChatSession> CreateAsync(
        CancellationToken cancellationToken = default) {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        var result = await EngineSessionFactory.CreateGuiSessionAsync(
            [new GuiInteractionModule()], cancellationToken);
        App.LogDiag($"[JccChatSession] EngineSessionFactory.CreateGuiSessionAsync: {swTotal.ElapsedMilliseconds}ms");

        var executionSettings = result.Services.GetService<IExecutionSettingsProvider>();

        Func<ValueTask> disposeAsync = result.Host is IAsyncDisposable ad
            ? () => ad.DisposeAsync()
            : () => { result.Host.Dispose(); return ValueTask.CompletedTask; };

        return new JccChatSession(result.Services, result.ChatService, result.Config, executionSettings, disposeAsync: disposeAsync);
    }

    /// <summary>会话是否就绪 — 引擎会话始终返回 true</summary>
    public bool IsReady => true;

    /// <inheritdoc />
    public ITranscriptService? TranscriptService => _services.GetService<ITranscriptService>();

    /// <summary>当前供应商名称（deepseek/openai/azure/anthropic/agnes/sensenova）</summary>
    public string CurrentVendor => _config.Provider.Vendor;

    /// <summary>当前启用的模型 ID</summary>
    public string CurrentModelId => _config.Provider.ModelId;

    /// <summary>配置文件驱动的供应商→模型列表映射（改 settings.json vendor 自动驱动下拉）</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; private set; }

    /// <summary>刷新 VendorModelMap（热重载入口）</summary>
    public void RefreshVendorModelMap() {
        VendorModelMap = BuildVendorModelMap();
    }

    /// <summary>切换会话 — 通过 IChatContextManager.SwitchSession 按 sessionId 隔离对话历史</summary>
    public void SwitchSession(string sessionId) {
        var ctxMgr = _services.GetService<IChatContextManager>();
        ctxMgr?.SwitchSession(sessionId);
    }

    /// <summary>
    /// 从持久化历史灌入底层对话上下文 — GUI 新进程 StateService 内存为空，
    /// 先 ClearMessagesAsync 清空当前桶，再逐条灌入历史消息到 IChatContextManager。
    /// 对齐 CLI /resume 的 LoadContextAsync 语义。
    /// </summary>
    public async Task LoadHistoryAsync(IReadOnlyList<(MessageRole Role, string Content)> messages, CancellationToken cancellationToken = default) {
        var ctxMgr = _services.GetService<IChatContextManager>();
        if (ctxMgr is null)
            return;

        await ctxMgr.ClearMessagesAsync(cancellationToken);
        foreach (var (role, content) in messages) {
            if (string.IsNullOrWhiteSpace(content))
                continue;
            switch (role) {
                case MessageRole.User:
                await ctxMgr.AddUserMessageAsync(content, cancellationToken: cancellationToken);
                break;
                case MessageRole.Assistant:
                await ctxMgr.AddAssistantMessageAsync(content, cancellationToken);
                break;
                case MessageRole.Tool:
                await ctxMgr.AddToolResultMessageAsync(content, new Dictionary<string, JsonElement>(), cancellationToken);
                break;
            }
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> BuildVendorModelMap() {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _modelConfigLoader.Config.Providers) {
            map[kvp.Key] = kvp.Value.Models.Select(m => m.Id).ToArray();
        }
        return map;
    }

    /// <summary>
    /// 切换当前模型 — 回写 WorkflowConfig + 持久化 vendor[profile].model 到 settings.json
    /// </summary>
    public async Task SetModelAsync(string modelId, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new System.ArgumentException("模型 ID 不能为空", nameof(modelId));
        _config.Provider.ModelId = modelId;

        // 双写：平铺键 "model" 对齐 CLI ModelCommand.ApplyModelSwitchAsync（configService 缓存+磁盘，
        // DoctorCommand 等跨前端消费方可见）；嵌套键 vendor[profile].model 是 SettingsMapper
        // 启动读取的唯一模型来源，两者缺一不可。
        var configService = _services.GetService<IConfigurationService>();
        if (configService is not null)
            await configService.SetAsync("model", modelId, cancellationToken);

        await UpdateVendorProfileModelAsync(modelId, cancellationToken);
    }

    /// <summary>
    /// 切换当前供应商 — 回写 WorkflowConfig + 持久化 profile 到 settings.json
    /// </summary>
    public async Task SetVendorAsync(string vendor, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(vendor))
            throw new System.ArgumentException("供应商名称不能为空", nameof(vendor));

        _config.Provider.Vendor = vendor;

        var defaultModelId = _modelConfigLoader.GetDefaultModelId(vendor);
        if (!string.IsNullOrEmpty(defaultModelId))
            _config.Provider.ModelId = defaultModelId;

        var configService = _services.GetService<IConfigurationService>();
        if (configService is not null) {
            await configService.SetAsync("profile", vendor, cancellationToken);
        }

        if (!string.IsNullOrEmpty(defaultModelId))
            await UpdateVendorProfileModelAsync(vendor, defaultModelId, cancellationToken);
    }

    /// <summary>
    /// 更新 settings.json 的 vendor[profile].model — 直接用 JsonNode 操作嵌套键
    /// </summary>
    private async Task UpdateVendorProfileModelAsync(string profileName, string modelId, CancellationToken ct) {
        var fs = _services.GetService<IFileSystem>() ?? new IO.FileSystem.PhysicalFileSystem();
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            AppDataConstants.SettingsFileName);
        if (!fs.FileExists(path)) return;
        try {
            await fs.EditFileAsync<bool>(path, async (bytes, cancellationToken) => {
                var (content, encoding) = FileEncodingDetector.DecodeBytes(bytes);
                var node = System.Text.Json.Nodes.JsonNode.Parse(content);
                if (node is null) return (null, false);
                var vendorNode = node["vendor"];
                if (vendorNode is null) {
                    vendorNode = new System.Text.Json.Nodes.JsonObject();
                    node["vendor"] = vendorNode;
                }
                var profileNode = vendorNode[profileName];
                if (profileNode is null) {
                    profileNode = new System.Text.Json.Nodes.JsonObject();
                    vendorNode[profileName] = profileNode;
                }
                profileNode["model"] = modelId;
                var newJson = node.ToJsonString();
                var newBytes = FileEncodingDetector.EncodeString(newJson, encoding);
                return (newBytes, true);
            }, ct);
        } catch (Exception ex) {
            System.Console.Error.WriteLine($"更新 vendor profile model 失败: {profileName} - {ex.Message}");
        }
    }

    /// <summary>
    /// 更新当前 profile 的 model — 用 _config.CurrentProfile 推导 profile 名
    /// </summary>
    private Task UpdateVendorProfileModelAsync(string modelId, CancellationToken ct) {
        var profile = _config.CurrentProfile;
        if (string.IsNullOrEmpty(profile))
            profile = _config.Provider.Vendor;
        if (string.IsNullOrEmpty(profile))
            return Task.CompletedTask;
        return UpdateVendorProfileModelAsync(profile, modelId, ct);
    }

    /// <summary>
    /// 当前推理力度 — 经共享 IExecutionSettingsProvider 读取（默认 Auto）。
    /// 未注册时回退 Auto（对齐 CLI ShowCurrentEffort 的 fallback 语义）。
    /// </summary>
    public EffortLevel EffortLevel => _executionSettings?.EffortLevel ?? EffortLevel.Auto;

    /// <summary>
    /// 设置推理力度并持久化 — 对齐 CLI EffortCommand.PersistEffortAsync：
    /// auto 移除 effortLevel 键，其它级别写入 settings.json。
    /// </summary>
    public async Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) {
        if (_executionSettings is not null) {
            _executionSettings.EffortLevel = effortLevel;
        }

        var configService = _services.GetService<IConfigurationService>();
        if (configService is not null) {
            if (effortLevel is EffortLevel.Auto) {
                await configService.RemoveAsync(ConfigKeyEnumConstants.EffortLevel, cancellationToken);
            } else {
                await configService.SetAsync(ConfigKeyEnumConstants.EffortLevel, effortLevel.ToValue(), cancellationToken);
            }
        }
    }

    /// <summary>初始化会话 — 引擎会话无需额外初始化，空实现</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// 当前温度 — 经共享 IExecutionSettingsProvider 读取（未设置返回 null → 引擎回退 LlmParameters.Chat）。
    /// </summary>
    public float? Temperature => _executionSettings?.Temperature;

    /// <summary>
    /// 当前最大长度 — 经共享 IExecutionSettingsProvider 读取（未设置返回 null → 引擎回退 LlmParameters.Chat）。
    /// </summary>
    public int? MaxTokens => _executionSettings?.MaxTokens;

    /// <summary>
    /// 设置温度并即时生效 — 写入共享 ExecutionSettingsProvider，ChatOptionsFactory 下次创建即覆盖默认值。
    /// </summary>
    public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default) {
        if (_executionSettings is not null) {
            _executionSettings.Temperature = temperature;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 设置最大长度并即时生效 — 写入共享 ExecutionSettingsProvider，ChatOptionsFactory 下次创建即覆盖默认值。
    /// </summary>
    public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default) {
        if (_executionSettings is not null) {
            _executionSettings.MaxTokens = maxTokens;
        }
        return Task.CompletedTask;
    }

    /// <summary>流式发送用户消息 — 权限确认由引擎侧 <see cref="IPermissionConfirmationHandler.ConfirmAsync"/> 同轮处理，此处直通</summary>
    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        string message,
        CancellationToken cancellationToken = default)
        => _chat.StreamWithEventsAsync(message, cancellationToken);

    /// <summary>
    /// 执行斜杠命令 — 委托共享 <see cref="SlashCommandRunner"/>（与 TUI 同一链路）。
    /// T9：确认回调经 SlashConfirmHandler 注入（GUI 弹窗），退出请求经 ExitRequested 事件上浮。
    /// </summary>
    public Func<string, bool>? SlashConfirmHandler { get; set; }

    /// <summary>退出请求事件 — 斜杠命令触发 /exit 时上浮到 UI 关闭窗口</summary>
    public event Action? ExitRequested;

    /// <summary>执行斜杠命令 — 委托共享 SlashCommandRunner，确认回调与退出请求经注入上浮</summary>
    public async Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default) {
        var result = await SlashCommandRunner.RunAsync(
            input,
            _services,
            confirm: message => SlashConfirmHandler?.Invoke(message) ?? false,
            onExitRequested: () => ExitRequested?.Invoke(),
            cancellationToken: cancellationToken);
        return result.Output;
    }

    /// <summary>获取底层对话消息记录 — 委托引擎 IChatService.GetMessageListAsync</summary>
    public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
        => _chat.GetMessageListAsync(cancellationToken);

    /// <summary>
    /// 应用系统提示词 — 转发到引擎 IChatService.SetSystemPromptAsync（admin 管道替换静态提示词），
    /// 对齐 CLI SystemPromptApplyStep 的 --system-prompt 语义。
    /// </summary>
    public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default)
        => _chat.SetSystemPromptAsync(systemPrompt, cancellationToken);

    /// <summary>
    /// 当前主题 — 从 settings.json 读取（键 ConfigKeyEnumConstants.Theme），对齐 CLI ThemeCommand。
    /// 未设置或损坏返回 <see cref="ThemeKind.Auto"/>（对齐 CLI GetCurrentThemeAsync 默认回退）。
    /// </summary>
    public async Task<ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default) {
        var configService = _services.GetService<IConfigurationService>();
        if (configService is null)
            return ThemeKind.Auto;

        var value = await configService.GetAsync(ConfigKeyEnumConstants.Theme, cancellationToken);
        return string.IsNullOrEmpty(value) ? ThemeKind.Auto : (ThemeKindExtensions.FromValue(value) ?? ThemeKind.Auto);
    }

    /// <summary>
    /// 设置主题并持久化到 settings.json（键 ConfigKeyEnumConstants.Theme），对齐 CLI ThemeCommand。
    /// </summary>
    public async Task SetThemeAsync(ThemeKind theme, CancellationToken cancellationToken = default) {
        var configService = _services.GetService<IConfigurationService>();
        if (configService is not null)
            await configService.SetAsync(ConfigKeyEnumConstants.Theme, theme.ToValue(), cancellationToken);
    }

    /// <summary>清空当前会话历史 — 委托引擎 IChatService.ClearHistoryAsync</summary>
    public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
        => _chat.ClearHistoryAsync(cancellationToken);

    /// <summary>回退上一轮对话 — 委托引擎 IChatService.RewindLastTurnAsync</summary>
    public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
        => _chat.RewindLastTurnAsync(cancellationToken);

    /// <summary>
    /// 获取可用斜杠命令清单 — 从 DI 解析 <c>ISlashCommandCatalog</c>（源码生成器在 Composition 中生成）。
    /// 未注册时返回空列表（兜底）。
    /// </summary>
    public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands() {
        var catalog = _services.GetService<ISlashCommandCatalog>();
        if (catalog is null)
            return [];
        return catalog.ByCategory.SelectMany(g => g.Value).Where(c => !c.IsHidden).ToList();
    }

    /// <summary>
    /// 获取可用工具清单 — 从 DI 解析 IToolRegistry，提取全部工具名与描述。
    /// 未注册时返回空列表（兜底）。
    /// </summary>
    public async Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default) {
        var registry = _services.GetService<IToolRegistry>();
        if (registry is null)
            return [];
        var tools = await registry.GetAllToolsAsync(cancellationToken);
        var list = new List<ToolSummary>(tools.Count);
        foreach (var handler in tools.Values)
            list.Add(new ToolSummary(handler.Name, handler.Description));
        return list;
    }

    /// <summary>
    /// 获取可用子代理清单 — 从 DI 解析 IAgentDefinitionProvider，提取全部代理定义的
    /// DisplayId 与 Description（Description 为空回退 WhenToUse）。未注册时返回空列表。
    /// </summary>
    public async Task<IReadOnlyList<SubAgentSummary>> GetAvailableSubAgentsAsync(CancellationToken cancellationToken = default) {
        var provider = _services.GetService<IAgentDefinitionProvider>();
        if (provider is null)
            return [];
        var definitions = await provider.GetAgentDefinitionsAsync(null, cancellationToken);
        var list = new List<SubAgentSummary>(definitions.Count);
        foreach (var def in definitions) {
            var displayId = def.DisplayId;
            var description = def.Description is { } d && !string.IsNullOrWhiteSpace(d) ? d : def.WhenToUse;
            list.Add(new SubAgentSummary(displayId, description, displayId));
        }
        return list;
    }

    /// <summary>异步释放会话资源 — 释放底层引擎 Host</summary>
    public async ValueTask DisposeAsync() {
        if (_disposeAsync is not null) {
            await _disposeAsync();
        }
    }

    /// <summary>暂停子代理 — 委托 InterruptSubAgentAsync（teammate 进 idle 等 next prompt）</summary>
    public Task<bool> PauseSubAgentAsync(string agentId, CancellationToken cancellationToken = default)
        => InterruptSubAgentAsync(agentId, cancellationToken);

    /// <summary>恢复子代理 — 委托 ForwardInputToSubAgentAsync（转发空消息唤醒 idle teammate）</summary>
    public Task<bool> ResumeSubAgentAsync(string agentId, CancellationToken cancellationToken = default)
        => ForwardInputToSubAgentAsync(agentId, string.Empty, cancellationToken);

    /// <summary>暂停所有运行中子代理 — 遍历后台代理对 running 状态逐个暂停</summary>
    public async Task<int> PauseAllSubAgentsAsync(CancellationToken cancellationToken = default) {
        var agents = await GetBackgroundAgentsAsync(cancellationToken);
        var running = agents.Where(a => a.State is AgentStatus.Running or AgentStatus.Pending);
        var count = 0;
        foreach (var agent in running) {
            if (await PauseSubAgentAsync(agent.AgentId, cancellationToken))
                count++;
        }
        return count;
    }

    /// <summary>恢复所有暂停中子代理 — 遍历后台代理对 paused 状态逐个恢复</summary>
    public async Task<int> ResumeAllSubAgentsAsync(CancellationToken cancellationToken = default) {
        var agents = await GetBackgroundAgentsAsync(cancellationToken);
        var paused = agents.Where(a => a.State is AgentStatus.Paused);
        var count = 0;
        foreach (var agent in paused) {
            if (await ResumeSubAgentAsync(agent.AgentId, cancellationToken))
                count++;
        }
        return count;
    }

    /// <summary>终止子代理 — 委托 StopBackgroundAgentAsync（终止性，不可恢复）</summary>
    public Task<bool> StopSubAgentAsync(string agentId, CancellationToken cancellationToken = default)
        => StopBackgroundAgentAsync(agentId, cancellationToken);

    /// <summary>终止所有运行中子代理 — 遍历后台代理对 running/paused 状态逐个终止</summary>
    public async Task<int> StopAllSubAgentsAsync(CancellationToken cancellationToken = default) {
        var agents = await GetBackgroundAgentsAsync(cancellationToken);
        var active = agents.Where(a => a.State is AgentStatus.Running or AgentStatus.Paused or AgentStatus.Pending);
        var count = 0;
        foreach (var agent in active) {
            if (await StopSubAgentAsync(agent.AgentId, cancellationToken))
                count++;
        }
        return count;
    }

    /// <summary>获取 Goal 进度快照 — 委托 GoalEngine.GetGoalProgress</summary>
    public Task<GoalProgress?> GetGoalProgressAsync(CancellationToken cancellationToken = default) {
        var goalEngine = _services.GetService<Core.Goal.GoalEngine>();
        return Task.FromResult(goalEngine?.GetGoalProgress());
    }

    /// <summary>
    /// 重置运行时计数器 — 双击 ESC 时调用，重置工具配额 + 输出循环检测（不清空聊天历史）。
    /// </summary>
    public Task ResetCountersAsync(CancellationToken cancellationToken = default) {
        ToolQuotaService.Reset();
        _services.GetService<Core.Context.IOutputLoopDetector>()?.Reset();
        return Task.CompletedTask;
    }

}