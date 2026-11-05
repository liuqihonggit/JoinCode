namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 工具进度中继（TASK065 P3 — Bash 工具流式进度引擎侧接线）— 工具执行网关 onProgress 回调 →
/// 主对话事件通道：SubAgentEventChannel.Current 在场时构造闭包，把 ToolProgressData 投影为
/// ChatStreamEvent.ToolProgress Emit（复用既有排空合流点 — QueryLoop WaitForTaskWithDrainAsync
/// 50ms 轮询 / SessionController 主代理 chunk 循环逐拍排空，零新管道）；
/// 无通道（CLI 纯文本等无显示消费方场景）返回 null — gateway 走无进度路径行为零变化。
/// 通道事件 AgentId 为 null → SessionController 主 Switch onToolProgress 分流 → IEventConsumer.OnToolProgress
/// → bridge 模式 tool_progress 事件行 / 进程内直连内存事件（双消费形态）。
/// 闭包直接持有通道引用（不经 AsyncLocal 回读）— ShellExecutionMiddleware 的进度 Timer 在
/// ThreadPool 回调发射时同样可靠（ExecutionContext 捕获链无关）。
/// </summary>
public static class ToolProgressRelay
{
    /// <summary>
    /// 构造进度中继回调 — 通道在场时闭包捕获通道引用，进度到达即 Emit ChatStreamEvent.ToolProgress
    /// （toolName/toolCallId 构造期捕获 — ToolProgressData 不携带工具名，由调用方闭包供给）；
    /// 无通道返回 null（调用方直传 gateway 第四参，gateway 判 null 跳过进度路径）。
    /// </summary>
    /// <param name="toolName">工具名称（进度事件归属 — 闭包捕获）</param>
    /// <param name="toolCallId">工具调用标识（可选 — 协议侧暂不投影，通道事件携带供后续按调用路由）</param>
    public static JoinCode.Abstractions.Tools.ToolProgressCallback? Build(string toolName, string? toolCallId = null)
    {
        var channel = SubAgentEventChannel.Current;
        if (channel is null)
        {
            return null;
        }

        return progress => channel.Emit(ChatStreamEvent.ToolProgress(
            toolName, progress.ProgressType, progress.Message ?? string.Empty, toolCallId));
    }
}
