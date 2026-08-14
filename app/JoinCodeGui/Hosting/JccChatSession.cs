using JoinCode.Abstractions.Configuration.AppData;
using JoinCode.Abstractions.Configuration.Llm;
using JoinCode.Abstractions.Configuration.Settings;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Security;
using JoinCode.Abstractions.Security.Permission;
using JoinCode.Abstractions.Tools;
using JoinCode.App.Builder;
using JoinCode.Abstractions.UI;
namespace JoinCode.Gui.Hosting;

/// <summary>
/// 引擎会话实现 — 进程内组装真实 AI 工作流（AddAiWorkflowServices + 共享管道），
/// 替代骨架阶段的占位回显。UI 与引擎解耦的唯一边界仍为 <c>IJccChatSession</c>。
/// 引擎具体组装收敛在本类，不蔓延到 Views/ViewModels。
/// </summary>
internal sealed class JccChatSession : IJccChatSession
{
    /// <summary>权限确认最大重试次数（同一消息连续触发确认）</summary>
    private const int MaxPermissionRetries = 3;

    /// <summary>临时批准时长 — 选择"允许本次"（对齐 CLI 5 分钟窗口）</summary>
    private static readonly TimeSpan AllowDuration = TimeSpan.FromMinutes(5);

    /// <summary>临时批准时长 — 选择"始终允许"（较长窗口，视为会话级始终允许）</summary>
    private static readonly TimeSpan AlwaysAllowDuration = TimeSpan.FromHours(24);

    private readonly IServiceProvider _services;
    private readonly IChatService _chat;
    private readonly JoinCode.Abstractions.Configuration.WorkflowConfig _config;
    private readonly IExecutionSettingsProvider? _executionSettings;
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly Func<ValueTask>? _disposeAsync;

    /// <inheritdoc />
    public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }

    internal JccChatSession(
        IServiceProvider services,
        IChatService chat,
        JoinCode.Abstractions.Configuration.WorkflowConfig config,
        IExecutionSettingsProvider? executionSettings = null,
        IModelConfigLoader? modelConfigLoader = null,
        Func<ValueTask>? disposeAsync = null)
    {
        _services = services;
        _chat = chat;
        _config = config;
        _executionSettings = executionSettings;
        _modelConfigLoader = modelConfigLoader ?? services.GetService<IModelConfigLoader>() ?? new ModelConfigLoader();
        _disposeAsync = disposeAsync;

        // 订阅 settings.json 变更 — theme 键变更时触发 ThemeChanged 驱动 GUI 热重载（双向绑定）
        var configService = services.GetService<IConfigurationService>();
        if (configService is not null)
            configService.SettingChanged += OnSettingChanged;

        VendorModelMap = BuildVendorModelMap();
    }

    /// <inheritdoc />
    public event EventHandler<ThemeKind>? ThemeChanged;

    /// <summary>settings.json 变更转发 — theme 键变更时解析为 ThemeKind 并触发 ThemeChanged</summary>
    private void OnSettingChanged(object? sender, SettingChangeEventArgs e)
    {
        if (e.Key == ConfigKeyConstants.Theme && e.NewValue is not null)
        {
            var theme = ThemeKindExtensions.FromValue(e.NewValue) ?? ThemeKind.Auto;
            ThemeChanged?.Invoke(this, theme);
        }
    }

    /// <summary>
    /// 创建引擎会话：调用 CLI 侧 EngineSessionFactory 一行完成 LoadConfig+BuildHost+ConfigureModules，
    /// 消除 GUI 和 CLI 的双引擎初始化差异。
    /// </summary>
    public static async Task<IJccChatSession> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        var result = await EngineSessionFactory.CreateGuiSessionAsync(cancellationToken).ConfigureAwait(false);
        App.LogDiag($"[JccChatSession] EngineSessionFactory.CreateGuiSessionAsync: {swTotal.ElapsedMilliseconds}ms");

        var executionSettings = result.Services.GetService<IExecutionSettingsProvider>();

        Func<ValueTask> disposeAsync = result.Host is IAsyncDisposable ad
            ? () => ad.DisposeAsync()
            : () => { result.Host.Dispose(); return ValueTask.CompletedTask; };

        return new JccChatSession(result.Services, result.ChatService, result.Config, executionSettings, disposeAsync: disposeAsync);
    }

    public bool IsReady => true;

    /// <summary>当前供应商名称（deepseek/openai/azure/anthropic/agnes/sensenova）</summary>
    public string CurrentVendor => _config.Provider.Vendor;

    /// <summary>当前启用的模型 ID</summary>
    public string CurrentModelId => _config.Provider.ModelId;

    /// <summary>配置文件 models.json 驱动的供应商→模型列表映射（改 config 自动驱动下拉）</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; private set; }

    /// <summary>刷新 VendorModelMap（热重载入口）</summary>
    public void RefreshVendorModelMap()
    {
        VendorModelMap = BuildVendorModelMap();
    }

    /// <summary>切换会话 — 通过 IChatContextManager.SwitchSession 按 sessionId 隔离对话历史</summary>
    public void SwitchSession(string sessionId)
    {
        var ctxMgr = _services.GetService<IChatContextManager>();
        ctxMgr?.SwitchSession(sessionId);
    }

    /// <summary>
    /// 从持久化历史灌入底层对话上下文 — GUI 新进程 StateService 内存为空，
    /// 先 ClearMessagesAsync 清空当前桶，再逐条灌入历史消息到 IChatContextManager。
    /// 对齐 CLI /resume 的 LoadContextAsync 语义。
    /// </summary>
    public async Task LoadHistoryAsync(IReadOnlyList<(MessageRole Role, string Content)> messages, CancellationToken cancellationToken = default)
    {
        var ctxMgr = _services.GetService<IChatContextManager>();
        if (ctxMgr is null)
            return;

        await ctxMgr.ClearMessagesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (role, content) in messages)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;
            switch (role)
            {
                case MessageRole.User:
                    await ctxMgr.AddUserMessageAsync(content, cancellationToken).ConfigureAwait(false);
                    break;
                case MessageRole.Assistant:
                    await ctxMgr.AddAssistantMessageAsync(content, cancellationToken).ConfigureAwait(false);
                    break;
                case MessageRole.System:
                    await ctxMgr.AddSystemMessageAsync(content, cancellationToken).ConfigureAwait(false);
                    break;
                case MessageRole.Tool:
                    await ctxMgr.AddToolResultMessageAsync(content, new Dictionary<string, JsonElement>(), cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> BuildVendorModelMap()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _modelConfigLoader.Config.Providers)
        {
            map[kvp.Key] = kvp.Value.Models.Select(m => m.Id).ToArray();
        }
        return map;
    }

    /// <summary>
    /// 切换当前模型 — 回写 WorkflowConfig + 持久化 vendor[profile].model 到 settings.json
    /// </summary>
    public async Task SetModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new System.ArgumentException("模型 ID 不能为空", nameof(modelId));
        _config.Provider.ModelId = modelId;

        // 持久化 vendor[profile].model
        await UpdateVendorProfileModelAsync(modelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 切换当前供应商 — 回写 WorkflowConfig + 持久化 profile 到 settings.json
    /// </summary>
    public async Task SetVendorAsync(string vendor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vendor))
            throw new System.ArgumentException("供应商名称不能为空", nameof(vendor));

        _config.Provider.Vendor = vendor;

        var defaultModelId = _modelConfigLoader.GetDefaultModelId(vendor);
        if (!string.IsNullOrEmpty(defaultModelId))
            _config.Provider.ModelId = defaultModelId;

        var configService = _services.GetService<IConfigurationService>();
        if (configService is not null)
        {
            await configService.SetAsync("profile", vendor, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(defaultModelId))
            await UpdateVendorProfileModelAsync(vendor, defaultModelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新 settings.json 的 vendor[profile].model — 直接用 JsonNode 操作嵌套键
    /// </summary>
    private async Task UpdateVendorProfileModelAsync(string profileName, string modelId, CancellationToken ct)
    {
        var fs = _services.GetService<IFileSystem>() ?? new IO.FileSystem.PhysicalFileSystem();
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            AppDataConstants.SettingsFileName);
        if (!fs.FileExists(path)) return;
        try
        {
            var json = await fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            if (node is null) return;
            var vendorNode = node["vendor"];
            if (vendorNode is null)
            {
                vendorNode = new System.Text.Json.Nodes.JsonObject();
                node["vendor"] = vendorNode;
            }
            var profileNode = vendorNode[profileName];
            if (profileNode is null)
            {
                profileNode = new System.Text.Json.Nodes.JsonObject();
                vendorNode[profileName] = profileNode;
            }
            profileNode["model"] = modelId;
            await fs.WriteAllTextAsync(path, node.ToJsonString(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"更新 vendor profile model 失败: {profileName} - {ex.Message}");
        }
    }

    /// <summary>
    /// 更新当前 profile 的 model — 用 _config.CurrentProfile 推导 profile 名
    /// </summary>
    private Task UpdateVendorProfileModelAsync(string modelId, CancellationToken ct)
    {
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
    public async Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default)
    {
        if (_executionSettings is not null)
        {
            _executionSettings.EffortLevel = effortLevel;
        }

        var configService = _services.GetService<IConfigurationService>();
        if (configService is not null)
        {
            if (effortLevel is EffortLevel.Auto)
            {
                await configService.RemoveAsync(ConfigKeyConstants.EffortLevel, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await configService.SetAsync(ConfigKeyConstants.EffortLevel, effortLevel.ToValue(), cancellationToken).ConfigureAwait(false);
            }
        }
    }

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
    public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default)
    {
        if (_executionSettings is not null)
        {
            _executionSettings.Temperature = temperature;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 设置最大长度并即时生效 — 写入共享 ExecutionSettingsProvider，ChatOptionsFactory 下次创建即覆盖默认值。
    /// </summary>
    public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default)
    {
        if (_executionSettings is not null)
        {
            _executionSettings.MaxTokens = maxTokens;
        }
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        string message,
        CancellationToken cancellationToken = default)
        => StreamWithPermissionRetryAsync(message, cancellationToken);

    /// <summary>
    /// 带权限确认闭环的事件流：引擎抛出 <see cref="PermissionPendingConfirmationException"/>
    /// 时调用 <see cref="PermissionConfirmationHandler"/> 获取用户决策；
    /// Allow/AlwaysAllow → 临时批准工具 + 撤回本轮（用户消息已在 ChatPreprocessor 入上下文，
    /// 不撤回会重复）→ 重发同一条消息（无重复）；Deny → 产出工具错误事件后结束。
    /// </summary>
    private async IAsyncEnumerable<ChatStreamEvent> StreamWithPermissionRetryAsync(
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var retries = 0;
        while (true)
        {
            // 手动枚举器：yield return 不能出现在 try/catch 内（CS1626），
            // 因此 try 只包住 MoveNextAsync，yield return 在 try 外。
            PermissionPendingConfirmationException? pending = null;
            await using var enumerator = _chat.StreamWithEventsAsync(message, cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (PermissionPendingConfirmationException ex)
                {
                    pending = ex;
                    break;
                }

                if (!hasNext)
                {
                    // 正常完成，无权限异常
                    yield break;
                }

                yield return enumerator.Current;
            }

            if (retries >= MaxPermissionRetries)
            {
                yield return ChatStreamEvent.ToolEnd(pending!.ToolName,
                    $"权限确认重试次数超限: {pending.ConfirmationPrompt}", isError: true);
                yield break;
            }

            var decision = PermissionConfirmationDecision.Deny;
            if (PermissionConfirmationHandler is not null)
            {
                decision = await PermissionConfirmationHandler(
                    new PermissionConfirmationRequest(
                        pending.ToolName, pending.ConfirmationPrompt, pending.RequestId, pending.RuleContent))
                    .ConfigureAwait(false);
            }

            if (decision == PermissionConfirmationDecision.Deny)
            {
                yield return ChatStreamEvent.ToolEnd(pending.ToolName,
                    $"权限确认被拒绝: {pending.ConfirmationPrompt}", isError: true);
                yield break;
            }

            // 允许/始终允许 → 批准工具（共享 PermissionManager，与 CLI 同源）
            var permissionManager = _services.GetService<IToolPermissionManager>();
            if (permissionManager is not null)
            {
                var duration = decision == PermissionConfirmationDecision.AlwaysAllow
                    ? AlwaysAllowDuration
                    : AllowDuration;
                permissionManager.ApproveToolTemporarily(pending.ToolName, duration);
            }

            // 撤回本轮（含用户消息 + 部分助手回复），重发同一条消息无重复
            await RewindLastTurnAsync(cancellationToken).ConfigureAwait(false);
            retries++;
            // while 循环重发同一条消息
        }
    }

    public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
        => _chat.GetMessageListAsync(cancellationToken);

    /// <summary>
    /// 应用系统提示词 — 转发到引擎 IChatService.SetSystemPromptAsync（admin 管道替换静态提示词），
    /// 对齐 CLI SystemPromptApplyStep 的 --system-prompt 语义。
    /// </summary>
    public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default)
        => _chat.SetSystemPromptAsync(systemPrompt, cancellationToken);

    /// <summary>
    /// 当前主题 — 从 settings.json 读取（键 ConfigKeyConstants.Theme），对齐 CLI ThemeCommand。
    /// 未设置或损坏返回 <see cref="ThemeKind.Auto"/>（对齐 CLI GetCurrentThemeAsync 默认回退）。
    /// </summary>
    public async Task<ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
    {
        var configService = _services.GetService<IConfigurationService>();
        if (configService is null)
            return ThemeKind.Auto;

        var value = await configService.GetAsync(ConfigKeyConstants.Theme, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(value) ? ThemeKind.Auto : (ThemeKindExtensions.FromValue(value) ?? ThemeKind.Auto);
    }

    /// <summary>
    /// 设置主题并持久化到 settings.json（键 ConfigKeyConstants.Theme），对齐 CLI ThemeCommand。
    /// </summary>
    public async Task SetThemeAsync(ThemeKind theme, CancellationToken cancellationToken = default)
    {
        var configService = _services.GetService<IConfigurationService>();
        if (configService is not null)
            await configService.SetAsync(ConfigKeyConstants.Theme, theme.ToValue(), cancellationToken).ConfigureAwait(false);
    }

    public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
        => _chat.ClearHistoryAsync(cancellationToken);

    public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
        => _chat.RewindLastTurnAsync(cancellationToken);

    /// <summary>
    /// 获取可用斜杠命令清单 — 从 DI 解析 <c>ISlashCommandCatalog</c>（源码生成器在 Composition 中生成）。
    /// 未注册时返回空列表（兜底）。
    /// </summary>
    public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands()
    {
        var catalog = _services.GetService<ISlashCommandCatalog>();
        if (catalog is null)
            return [];
        return catalog.Commands.Where(c => !c.IsHidden).ToList();
    }

    /// <summary>
    /// 获取可用工具清单 — 从 DI 解析 IToolRegistry，提取全部工具名与描述。
    /// 未注册时返回空列表（兜底）。
    /// </summary>
    public async Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        var registry = _services.GetService<IToolRegistry>();
        if (registry is null)
            return [];
        var tools = await registry.GetAllToolsAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<ToolSummary>(tools.Count);
        foreach (var handler in tools.Values)
            list.Add(new ToolSummary(handler.Name, handler.Description));
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposeAsync is not null)
        {
            await _disposeAsync().ConfigureAwait(false);
        }
    }

}