using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM.Chat;

namespace JoinCode.Gui.Hosting;

/// <summary>
/// JCC 引擎宿主门面 — UI 与引擎解耦的唯一边界。
/// ViewModel 只允许依赖此接口与 Abstractions 门面类型，禁止直接触碰引擎内部实现。
/// </summary>
public interface IJccChatSession : IAsyncDisposable
{
    /// <summary>引擎会话是否就绪（可发送消息）</summary>
    bool IsReady { get; }

    /// <summary>初始化引擎会话（加载配置 + 组装 DI + 解析 IChatService）</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>以事件流方式发送消息（含工具调用迭代），是聊天界面主通道</summary>
    IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>获取当前会话消息列表</summary>
    Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>清空会话历史</summary>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>撤回最近一轮对话</summary>
    Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default);
}