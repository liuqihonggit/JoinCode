using JoinCode.Abstractions.Configuration.Llm;
using JoinCode.Abstractions.Configuration.Settings;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Security;
using JoinCode.Abstractions.Security.Permission;
using JoinCode.Abstractions.Tools;

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

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _services;
    private readonly IChatService _chat;
    private readonly JoinCode.Abstractions.Configuration.WorkflowConfig _config;
    private readonly IExecutionSettingsProvider? _executionSettings;

    /// <inheritdoc />
    public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }

    internal JccChatSession(
        Microsoft.Extensions.DependencyInjection.ServiceProvider services,
        IChatService chat,
        JoinCode.Abstractions.Configuration.WorkflowConfig config,
        IExecutionSettingsProvider? executionSettings = null)
    {
        _services = services;
        _chat = chat;
        _config = config;
        _executionSettings = executionSettings;
    }

    /// <summary>
    /// 创建引擎会话：加载配置 → 组装 DI（Composition + 共享管道）→ 解析 IChatService。
    /// provider 为 null 时回退到环境变量 / 默认 deepseek 配置。
    /// </summary>
    public static async Task<IJccChatSession> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var config = await LoadConfigFallbackAsync(cancellationToken).ConfigureAwait(false);
        App.LogDiag($"[JccChatSession] LoadConfigFallback: {sw.ElapsedMilliseconds}ms"); sw.Restart();

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddLogging(b => b.AddConsole());
        services.AddAiWorkflowServices(config);
        App.LogDiag($"[JccChatSession] AddAiWorkflowServices: {sw.ElapsedMilliseconds}ms"); sw.Restart();
        services.AddAllPipelines();
        App.LogDiag($"[JccChatSession] AddAllPipelines: {sw.ElapsedMilliseconds}ms"); sw.Restart();

        var sp = services.BuildServiceProvider();
        App.LogDiag($"[JccChatSession] BuildServiceProvider: {sw.ElapsedMilliseconds}ms"); sw.Restart();
        var chat = sp.GetRequiredService<IChatService>();
        App.LogDiag($"[JccChatSession] GetRequiredService<IChatService>: {sw.ElapsedMilliseconds}ms"); sw.Restart();
        var executionSettings = sp.GetService<IExecutionSettingsProvider>();
        App.LogDiag($"[JccChatSession] TOTAL CreateAsync: {swTotal.ElapsedMilliseconds}ms");
        return new JccChatSession(sp, chat, config, executionSettings);
    }

    public bool IsReady => true;

    /// <summary>当前供应商名称（deepseek/openai/azure/anthropic/agnes/sensenova）</summary>
    public string CurrentVendor => _config.Provider.Vendor;

    /// <summary>当前启用的模型 ID</summary>
    public string CurrentModelId => _config.Provider.ModelId;

    /// <summary>配置文件 models.json 驱动的供应商→模型列表映射（改 config 自动驱动下拉）</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; private set; } = BuildVendorModelMap();

    /// <summary>重新加载 models.json 并刷新 VendorModelMap（热重载入口）</summary>
    public void RefreshVendorModelMap()
    {
        ModelConfigLoader.Reload();
        VendorModelMap = BuildVendorModelMap();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildVendorModelMap()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in ModelConfigLoader.Config.Providers)
        {
            map[kvp.Key] = kvp.Value.Models.Select(m => m.Id).ToArray();
        }
        return map;
    }

    /// <summary>
    /// 切换当前模型 — 直接回写共享 WorkflowConfig.Provider（DI 单例，QueryService 请求期读取同一实例），
    /// 并持久化 modelId 到 settings.json（对齐 CLI ModelCommand.ApplyModelSwitchAsync，
    /// 键 "model" 与 SettingsJson 生成器 jsonName 一致），保证 GUI 重启后保留所选模型。
    /// </summary>
    public async Task SetModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new System.ArgumentException("模型 ID 不能为空", nameof(modelId));
        }
        _config.Provider.ModelId = modelId;

        var configService = _services.GetService<IConfigurationService>();
        if (configService is not null)
        {
            await configService.SetAsync("model", modelId, cancellationToken).ConfigureAwait(false);
        }
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
        if (_chat is IAsyncDisposable chatDisposable)
        {
            await chatDisposable.DisposeAsync().ConfigureAwait(false);
        }
        await _services.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<JoinCode.Abstractions.Configuration.WorkflowConfig> LoadConfigFallbackAsync(
        CancellationToken cancellationToken)
    {
        var config = await new Core.Configuration.ConfigLoader().LoadAsync(
                new IO.FileSystem.PhysicalFileSystem(), cancellationToken)
            .ConfigureAwait(false);

        // 进程内 GUI：不连接命名管道服务，标准 HTTP QueryService（PipeEndpoint 置 null）
        config.PipeEndpoint = null;
        return config;
    }
}