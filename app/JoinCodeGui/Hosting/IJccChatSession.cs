using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.LLM.Chat;

namespace JoinCode.Gui.Hosting;

/// <summary>工具摘要 — 名称与描述，供 GUI #工具补全展示</summary>
public sealed record ToolSummary(string Name, string Description);

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

    /// <summary>
    /// 当前推理力度（默认 Auto；对齐 CLI /effort current 语义）。
    /// 进程内引擎经 <c>IExecutionSettingsProvider.EffortLevel</c> 实时消费。
    /// </summary>
    EffortLevel EffortLevel { get; }

    /// <summary>
    /// 设置推理力度并持久化到 settings.json（对齐 CLI /effort：
    /// auto → 移除持久化键，其它级别 → 写 effortLevel 键）。
    /// </summary>
    Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default);

    /// <summary>
    /// 应用系统提示词 — 经 <c>IChatService.SetSystemPromptAsync</c>（admin 管道）替换静态系统提示词，
    /// 对齐 CLI SystemPromptApplyStep 的 --system-prompt 语义。下次请求即生效。
    /// </summary>
    Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 当前温度 — 未设置返回 null，引擎回退 LlmParameters.Chat（默认 0.7）。
    /// </summary>
    float? Temperature { get; }

    /// <summary>
    /// 当前最大长度 — 未设置返回 null，引擎回退 LlmParameters.Chat（默认 2000）。
    /// </summary>
    int? MaxTokens { get; }

    /// <summary>
    /// 设置温度并即时生效 — 经共享 ExecutionSettingsProvider 覆盖引擎默认值。
    /// </summary>
    Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置最大长度并即时生效 — 经共享 ExecutionSettingsProvider 覆盖引擎默认值。
    /// </summary>
    Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取可用斜杠命令清单 — 由源码生成器从 [ChatCommand] 特性自动提取，
    /// 供 GUI 命令面板消费。不包含隐藏命令。
    /// </summary>
    IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands();

    /// <summary>
    /// 获取可用工具清单 — 从引擎 IToolRegistry 提取工具名与描述，
    /// 供 GUI #工具补全消费。引擎未注册时返回空列表。
    /// </summary>
    Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default);
}