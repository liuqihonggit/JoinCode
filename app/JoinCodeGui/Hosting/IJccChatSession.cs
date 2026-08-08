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

    /// <summary>
    /// 权限确认回调 — 引擎抛出 <c>PermissionPendingConfirmationException</c> 时由网关调用。
    /// UI 注入后返回决策；为 null 时网关默认拒绝（等价于 Deny）。
    /// 决策为 Allow/AlwaysAllow 时网关自动批准工具并重发同一条消息完成闭环。
    /// </summary>
    Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }

    /// <summary>获取当前会话消息列表</summary>
    Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>清空会话历史</summary>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>撤回最近一轮对话</summary>
    Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default);

    /// <summary>当前启用 Provider 名称（如 deepseek/openai，驱动模型下拉分组成员）</summary>
    string CurrentProvider { get; }

    /// <summary>当前启用的模型 ID</summary>
    string CurrentModelId { get; }

    /// <summary>当前 Provider 可选真实模型 ID 列表（来自共享配置 ModelConfigLoader）</summary>
    IReadOnlyList<string> AvailableModels { get; }

    /// <summary>切换当前模型（回写共享 WorkflowConfig.Provider.ModelId，下次请求引擎即生效）</summary>
    Task SetModelAsync(string modelId, CancellationToken cancellationToken = default);
}