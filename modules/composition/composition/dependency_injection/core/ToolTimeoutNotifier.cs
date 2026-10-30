namespace Core.DependencyInjection;

/// <summary>
/// 工具超时通知器实现 — 注入 steer 提示词唤醒宿主 AI 决策（ADR 0153）
/// <para>
/// WarningThreshold 到达时：构造提示词 → ISteerInputMailbox.Enqueue 注入 → 宿主下一步步首 drain → LLM 决策继续/结束。
/// KillThreshold 到达时：日志记录（进程杀死由 SessionToolMap.TryKillProcess 处理）。
/// </para>
/// </summary>
public sealed class ToolTimeoutNotifier : IToolTimeoutNotifier {
    private readonly ISteerInputMailbox _mailbox;
    private readonly ILogger<ToolTimeoutNotifier>? _logger;

    /// <summary>
    /// 构造工具超时通知器
    /// </summary>
    /// <param name="mailbox">steer 轮内注入邮箱（ADR 0146）</param>
    /// <param name="logger">日志记录器</param>
    public ToolTimeoutNotifier(ISteerInputMailbox mailbox, ILogger<ToolTimeoutNotifier>? logger = null) {
        _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
        _logger = logger;
    }

    /// <inheritdoc />
    public Task NotifyWarningAsync(ToolTimeoutInfo info, CancellationToken ct = default) {
        var sessionId = SubAgentContext.Current?.SessionId;
        if (sessionId is null) {
            _logger?.LogDebug("[ToolTimeoutNotifier] 无 sessionId，跳过 steer 注入: {ToolName} PID={Pid}", info.ToolName, info.ProcessId);
            return Task.CompletedTask;
        }

        var prompt = BuildWarningPrompt(info);
        _mailbox.Enqueue(sessionId, prompt);
        _logger?.LogInformation("[ToolTimeoutNotifier] 工具超时 steer 注入: {ToolName} 已运行 {Elapsed}s PID={Pid} Agent={Agent}",
            info.ToolName, (int)info.Elapsed.TotalSeconds, info.ProcessId, info.AgentId ?? "main");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyKillAsync(ToolTimeoutInfo info, CancellationToken ct = default) {
        _logger?.LogWarning("[ToolTimeoutNotifier] 工具到达 KillThreshold 强制杀死: {ToolName} 已运行 {Elapsed}s PID={Pid} Agent={Agent}",
            info.ToolName, (int)info.Elapsed.TotalSeconds, info.ProcessId, info.AgentId ?? "main");
        return Task.CompletedTask;
    }

    private static string BuildWarningPrompt(ToolTimeoutInfo info) {
        var elapsed = (int)info.Elapsed.TotalSeconds;
        var killThreshold = (int)info.KillThreshold.TotalSeconds;
        return $"<tool-timeout-warning>\n工具 \"{info.ToolName}\" 已运行 {elapsed} 秒（PID={info.ProcessId}）。\n请决定:\n- 回复\"继续\" — 工具继续运行，下次警报在 {killThreshold} 秒\n- 回复\"结束\" — 终止工具进程，返回已收集的输出\n</tool-timeout-warning>";
    }
}
