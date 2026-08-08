using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM.Chat;

namespace JoinCode.Gui.Hosting;

/// <summary>
/// 占位引擎会话实现 — 骨架阶段无真实引擎引用，返回固定回显供 UI 运行验证。
/// 正式接入引擎时由 <c>JccChatSession</c> 替换，本类保留作测试桩或删除。
/// </summary>
internal sealed class PlaceholderChatSession : IJccChatSession
{
    public bool IsReady => true;

    public string CurrentProvider => "deepseek";

    public string CurrentModelId => "deepseek-chat";

    public IReadOnlyList<string> AvailableModels { get; } = ["deepseek-chat", "deepseek-reasoner"];

    public Task SetModelAsync(string modelId, CancellationToken cancellationToken = default)
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

        // 正式回复内容
        foreach (var ch in "[占位引擎未接入 " + message + "]\n\n这是一个模拟回复，包含工具调用与思考过程的完整链路展示。")
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}