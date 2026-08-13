using JoinCode.Abstractions.Configuration.Llm;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.LLM.Chat;

namespace JoinCode.Gui.Hosting;

/// <summary>
/// 占位引擎会话实现 — 无真实引擎连接时作为 mock 占位，返回固定回显供 UI 运行验证。
/// 被 MainViewModel 用作 mock 连接（IsMockConnection），同时用于单元测试桩。
/// </summary>
internal sealed class PlaceholderChatSession : IJccChatSession
{
    public bool IsReady => true;

    /// <summary>占位会话默认供应商 — 对齐真实引擎 ProviderConfig 默认值（deepseek）</summary>
    public string CurrentVendor { get; } = "deepseek";

    /// <summary>占位会话默认模型 — 从 ModelConfigLoader 读取 deepseek 的 DefaultModelId，与真实引擎默认值对齐，避免热切换闪烁</summary>
    public string CurrentModelId { get; } = ResolveDefaultModelId();

    private static string ResolveDefaultModelId()
    {
        var id = ModelConfigLoader.GetDefaultModelId("deepseek");
        return !string.IsNullOrEmpty(id) ? id : "deepseek-chat";
    }

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

    /// <summary>占位会话不触发引擎权限异常，保留回调供 UI 注入（无实际效果）</summary>
    public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }

    public Task SetModelAsync(string modelId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>占位会话固定返回 Auto，不持久化</summary>
    public EffortLevel EffortLevel => EffortLevel.Auto;

    public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public float? Temperature => null;
    public int? MaxTokens => null;

    public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var ch in "让我先分析一下你的问题。")
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