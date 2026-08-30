namespace JoinCode.Gui.Hosting;

/// <summary>
/// 占位引擎会话实现 — 无真实引擎连接时作为 mock 占位，返回固定回显供 UI 运行验证。
/// 被 MainViewModel 用作 mock 连接（IsMockConnection），同时用于单元测试桩。
/// </summary>
internal sealed class PlaceholderChatSession : IJccChatSession
{
    private readonly IConfigurationService? _configService;
    private readonly IModelConfigLoader _modelConfigLoader;

    public PlaceholderChatSession(IConfigurationService? configService = null, IModelConfigLoader? modelConfigLoader = null)
    {
        _configService = configService;
        _modelConfigLoader = modelConfigLoader ?? new ModelConfigLoader();
        CurrentVendor = ResolveCurrentVendor(configService);
        CurrentModelId = ResolveCurrentModelId(CurrentVendor);
        VendorModelMap = BuildVendorModelMap();
    }

    public bool IsReady => true;

    /// <inheritdoc />
    public ITranscriptService? TranscriptService => null;

    /// <summary>占位会话当前供应商 — 从 settings.json 读取,回退 deepseek</summary>
    public string CurrentVendor { get; }

    /// <summary>占位会话当前模型 — 从 settings.json 读取,回退供应商默认模型</summary>
    public string CurrentModelId { get; }

    private static string ResolveCurrentVendor(IConfigurationService? configService)
    {
        if (configService is not null)
        {
            try
            {
                var profile = configService.GetAsync("profile", CancellationToken.None).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(profile))
                    return profile;
            }
            catch (Exception ex) { System.Console.Error.WriteLine($"[PlaceholderChatSession] 读取 settings.json profile 失败: {ex.Message}"); }
        }
        return "";
    }

    private string ResolveCurrentModelId(string vendor)
    {
        if (string.IsNullOrEmpty(vendor))
            return "";
        var id = _modelConfigLoader.GetDefaultModelId(vendor);
        return !string.IsNullOrEmpty(id) ? id : "";
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; private set; }

    public void RefreshVendorModelMap()
    {
        VendorModelMap = BuildVendorModelMap();
    }

    /// <summary>占位会话切换 — 无真实引擎历史，空实现</summary>
    public void SwitchSession(string sessionId) { }

    /// <summary>占位会话无真实引擎上下文，灌入历史空实现</summary>
    public Task LoadHistoryAsync(IReadOnlyList<(MessageRole Role, string Content)> messages, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private IReadOnlyDictionary<string, IReadOnlyList<string>> BuildVendorModelMap()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _modelConfigLoader.Config.Providers)
        {
            map[kvp.Key] = kvp.Value.Models.Select(m => m.Id).ToArray();
        }
        return map;
    }

    /// <summary>占位会话不触发引擎权限异常，保留回调供 UI 注入（无实际效果）</summary>
    public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }

    /// <summary>占位会话不触发 AskUserQuestion，保留回调供 UI 注入（无实际效果）</summary>
    public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback { get; set; }

    /// <summary>T9：占位会话无确认 UI，默认拒绝；事件不会触发</summary>
    public Func<string, bool>? SlashConfirmHandler { get; set; }

    public event Action? ExitRequested { add { } remove { } }

    public async Task SetModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (_configService is not null)
            await _configService.SetAsync("model", modelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>占位会话供应商切换 — 持久化 profile 到 settings.json，引擎可用后重启生效</summary>
    public async Task SetVendorAsync(string vendor, CancellationToken cancellationToken = default)
    {
        if (_configService is null) return;
        await _configService.SetAsync("profile", vendor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>占位会话固定返回 Auto，不持久化</summary>
    public EffortLevel EffortLevel => EffortLevel.Auto;

    public async Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default)
    {
        if (_configService is null) return;
        if (effortLevel is EffortLevel.Auto)
            await _configService.RemoveAsync(ConfigKeyConstants.EffortLevel, cancellationToken).ConfigureAwait(false);
        else
            await _configService.SetAsync(ConfigKeyConstants.EffortLevel, effortLevel.ToValue(), cancellationToken).ConfigureAwait(false);
    }

    public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>占位会话从 settings.json 读主题 — 引擎不可用时仍恢复上次选择</summary>
    public async Task<ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
    {
        if (_configService is null)
            return ThemeKind.Auto;
        var value = await _configService.GetAsync(ConfigKeyConstants.Theme, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(value) ? ThemeKind.Auto : (ThemeKindExtensions.FromValue(value) ?? ThemeKind.Auto);
    }

    /// <summary>占位会话不持久化主题 — 引擎不可用时仍写 settings.json，对齐 CLI /theme</summary>
    public async Task SetThemeAsync(ThemeKind theme, CancellationToken cancellationToken = default)
    {
        if (_configService is not null)
            await _configService.SetAsync(ConfigKeyConstants.Theme, theme.ToValue(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>占位会话无 settings.json 变更，事件永不触发（空 add/remove 避免 CS0067）</summary>
    public event EventHandler<ThemeKind>? ThemeChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ViewModels.BackgroundAgentInfo>> GetBackgroundAgentsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ViewModels.BackgroundAgentInfo>>([]);

    /// <inheritdoc />
    public Task<bool> StopBackgroundAgentAsync(string agentId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public float? Temperature => null;
    public int? MaxTokens => null;

    public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>占位会话无引擎命令系统 — 斜杠命令返回提示文案（G1 对齐接口契约）</summary>
    public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult("（Mock 引擎不支持斜杠命令执行，连接真实引擎后可用）");

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {        foreach (var ch in "让我先分析一下你的问题。")
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return ChatStreamEvent.Thinking(ch.ToString());
        }
        await Task.Yield();
        yield return ChatStreamEvent.Thinking("\n[推理过程] 用户询问：");

        // 后台 agent 动作 1：搜索
        yield return ChatStreamEvent.ToolStart("WebSearch", "call_search_01", "{\"query\":\"" + message + "\"}");
        yield return ChatStreamEvent.ToolProgress("WebSearch", "query_update", "正在搜索关键词…", "call_search_01");
        await Task.Yield();
        yield return ChatStreamEvent.ToolProgress("WebSearch", "search_results_received", "已获取 3 条结果", "call_search_01");
        await Task.Yield();
        yield return ChatStreamEvent.ToolEnd("WebSearch", "找到相关文档，主题与用户问题吻合", "call_search_01");

        // 后台 agent 动作 2：读文件
        yield return ChatStreamEvent.ToolStart("ReadFile", "call_read_01", "{\"path\":\"docs/guide.md\"}");
        await Task.Yield();
        yield return ChatStreamEvent.ToolEnd("ReadFile", "读取 120 行，包含示例代码", "call_read_01");

        // 思考结论
        foreach (var ch in "\n[结论] 综合搜索结果与代码分析，给出以下回答：")
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return ChatStreamEvent.Thinking(ch.ToString());
        }

        // 正式回复内容（Markdown 示例，供 UI Markdown 渲染目视验证）
        foreach (var ch in "[占位引擎未接入 " + message + "]\n\n" +
            "## 功能清单\n\n" +
            "- **代码块**：等宽字体 + 深色底\n" +
            "- *斜体* 与 ~~删除线~~ 行内样式\n" +
            "- 表格对齐展示\n\n" +
            "| 功能 | 状态 |\n|---|---|\n| 代码块 | ✅ |\n| 表格 | ✅ |\n\n" +
            "```csharp\npublic void Hello()\n{\n    Console.WriteLine(\"MarkdownView\");\n}\n```\n\n" +
            "> 引用块：左侧竖条样式\n\n" +
            "---\n\n1. 有序列表第一项\n2. 有序列表第二项")
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return ChatStreamEvent.Text(ch.ToString());
        }
        yield return ChatStreamEvent.Done();
    }

    public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<ApiMessageRecord>)[]

    );

    public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new RewindResult { Success = true });

    public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands() => [];

    public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ToolSummary>>([]);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}