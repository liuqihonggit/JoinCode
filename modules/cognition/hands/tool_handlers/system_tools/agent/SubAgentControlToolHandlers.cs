namespace Tools.Handlers;

/// <summary>
/// 子代理控制选项 — 统一工具 subagent_control 的参数
/// </summary>
public sealed record SubAgentControlOptions {
    /// <summary>操作类型: list/pause/resume/stop/pause_all/resume_all/stop_all/clean/resume_from_transcript/resume_all_from_transcript</summary>
    [McpToolParameter("Action: list/pause/resume/stop/pause_all/resume_all/stop_all/clean/resume_from_transcript/resume_all_from_transcript")]
    public required string Action { get; init; }

    /// <summary>子代理 ID — pause/resume/stop/resume_from_transcript 必填，list/pause_all/resume_all/stop_all/clean/resume_all_from_transcript 忽略</summary>
    [McpToolParameter("Sub-agent ID (required for pause/resume/stop/resume_from_transcript)", Required = false)]
    public string? Id { get; init; }

    /// <summary>恢复提示词 — resume_from_transcript/resume_all_from_transcript 使用，默认"请继续执行你之前被中断的任务"</summary>
    [McpToolParameter("Resume prompt for transcript recovery", Required = false)]
    public string? Prompt { get; init; }
}

/// <summary>
/// 子代理控制工具 — 统一入口 subagent_control(action, id?)。
/// 主代理通过此工具控制子代理的暂停/恢复，与 GUI 按钮委托到同一服务接口。
/// </summary>
[McpToolDispatch(ToolCategory.Agent, Optional = true)]
public sealed partial class SubAgentControlToolHandlers : ServiceEntity {
    private readonly IAgentService _agentService;
    private readonly IInProcessTeammateTaskExecutor? _teammateExecutor;

    /// <summary>初始化 SubAgentControlToolHandlers 实例</summary>
    public SubAgentControlToolHandlers(
        IAgentService agentService,
        IInProcessTeammateTaskExecutor? teammateExecutor = null) {
        _agentService = agentService;
        _teammateExecutor = teammateExecutor;
    }

    /// <summary>
    /// 子代理控制 — list/pause/resume/stop/pause_all/resume_all/stop_all/clean 统一入口。
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.SubAgentControl, "Control sub-agents: list/pause/resume/stop/pause_all/resume_all/stop_all/clean/resume_from_transcript/resume_all_from_transcript", AgentToolNameEnumConstants.Agent)]
    public async Task<ToolResult> ControlAsync(
        [McpToolOptions] SubAgentControlOptions options,
        CancellationToken cancellationToken = default) {
        return options.Action switch {
            "list" => await ListAsync(cancellationToken).ConfigureAwait(false),
            "pause" => await PauseAsync(options.Id, cancellationToken).ConfigureAwait(false),
            "resume" => await ResumeAsync(options.Id, cancellationToken).ConfigureAwait(false),
            "stop" => await StopAsync(options.Id, cancellationToken).ConfigureAwait(false),
            "pause_all" => await PauseAllAsync(cancellationToken).ConfigureAwait(false),
            "resume_all" => await ResumeAllAsync(cancellationToken).ConfigureAwait(false),
            "stop_all" => await StopAllAsync(cancellationToken).ConfigureAwait(false),
            "clean" => await CleanAsync(cancellationToken).ConfigureAwait(false),
            "resume_from_transcript" => await ResumeFromTranscriptAsync(options.Id, options.Prompt, cancellationToken).ConfigureAwait(false),
            "resume_all_from_transcript" => await ResumeAllFromTranscriptAsync(options.Prompt, cancellationToken).ConfigureAwait(false),
            _ => ToolResultBuilder.Error().WithText($"未知 action: {options.Action}，有效值: list/pause/resume/stop/pause_all/resume_all/stop_all/clean/resume_from_transcript/resume_all_from_transcript").Build()
        };
    }

    /// <summary>列出所有运行中子代理</summary>
    private async Task<ToolResult> ListAsync(CancellationToken ct) {
        var agents = await _agentService.GetRunningAgentsAsync(ct).ConfigureAwait(false);
        if (!agents.Any())
            return ToolResultBuilder.Success().WithText("当前没有运行中的子代理。").Build();
        var lines = agents.Select(a => $"- {a.Id} | {a.Description} | {a.State}");
        return ToolResultBuilder.Success().WithText("运行中子代理:\n" + string.Join("\n", lines)).Build();
    }

    /// <summary>暂停指定子代理 — 委托 InterruptTeammateAsync（teammate 进 idle 等 next prompt）</summary>
    private async Task<ToolResult> PauseAsync(string? agentId, CancellationToken ct) {
        if (string.IsNullOrEmpty(agentId))
            return ToolResultBuilder.Error().WithText("pause 需要 id 参数").Build();
        if (_teammateExecutor is null)
            return ToolResultBuilder.Error().WithText("teammate 执行器未注入，暂停不可用").Build();
        var ok = await _teammateExecutor.InterruptTeammateAsync(agentId, ct).ConfigureAwait(false);
        return ok ? ToolResultBuilder.Success().WithText($"子代理 {agentId} 已暂停（进 idle 等 next prompt）").Build()
                  : ToolResultBuilder.Error().WithText($"暂停失败：子代理 {agentId} 不存在或非 teammate 路径").Build();
    }

    /// <summary>恢复指定子代理 — 委托 ForwardUserInputToAgentAsync（转发空消息唤醒 idle teammate）</summary>
    private async Task<ToolResult> ResumeAsync(string? agentId, CancellationToken ct) {
        if (string.IsNullOrEmpty(agentId))
            return ToolResultBuilder.Error().WithText("resume 需要 id 参数").Build();
        var ok = await _agentService.ForwardUserInputToAgentAsync(agentId, string.Empty, ct).ConfigureAwait(false);
        return ok ? ToolResultBuilder.Success().WithText($"子代理 {agentId} 已恢复").Build()
                  : ToolResultBuilder.Error().WithText($"恢复失败：子代理 {agentId} 不存在").Build();
    }

    /// <summary>暂停所有运行中子代理</summary>
    private async Task<ToolResult> PauseAllAsync(CancellationToken ct) {
        if (_teammateExecutor is null)
            return ToolResultBuilder.Error().WithText("teammate 执行器未注入，暂停不可用").Build();
        var agents = await _agentService.GetRunningAgentsAsync(ct).ConfigureAwait(false);
        var count = 0;
        foreach (var agent in agents) {
            if (await _teammateExecutor.InterruptTeammateAsync(agent.Id, ct).ConfigureAwait(false))
                count++;
        }
        return ToolResultBuilder.Success().WithText($"已暂停 {count} 个子代理").Build();
    }

    /// <summary>恢复所有暂停中子代理</summary>
    private async Task<ToolResult> ResumeAllAsync(CancellationToken ct) {
        var agents = await _agentService.GetRunningAgentsAsync(ct).ConfigureAwait(false);
        var count = 0;
        foreach (var agent in agents) {
            if (await _agentService.ForwardUserInputToAgentAsync(agent.Id, string.Empty, ct).ConfigureAwait(false))
                count++;
        }
        return ToolResultBuilder.Success().WithText($"已恢复 {count} 个子代理").Build();
    }

    /// <summary>终止指定子代理 — 委托 StopAgentAsync（终止性，不可恢复）</summary>
    private async Task<ToolResult> StopAsync(string? agentId, CancellationToken ct) {
        if (string.IsNullOrEmpty(agentId))
            return ToolResultBuilder.Error().WithText("stop 需要 id 参数").Build();
        var ok = await _agentService.StopAgentAsync(agentId, ct).ConfigureAwait(false);
        return ok ? ToolResultBuilder.Success().WithText($"子代理 {agentId} 已终止").Build()
                   : ToolResultBuilder.Error().WithText($"终止失败：子代理 {agentId} 不存在").Build();
    }

    /// <summary>终止所有运行中子代理</summary>
    private async Task<ToolResult> StopAllAsync(CancellationToken ct) {
        var agents = await _agentService.GetRunningAgentsAsync(ct).ConfigureAwait(false);
        var count = 0;
        foreach (var agent in agents) {
            if (await _agentService.StopAgentAsync(agent.Id, ct).ConfigureAwait(false))
                count++;
        }
        return ToolResultBuilder.Success().WithText($"已终止 {count} 个子代理").Build();
    }

    /// <summary>清理已完成代理 — 刷新列表，仅返回当前运行中代理</summary>
    private async Task<ToolResult> CleanAsync(CancellationToken ct) {
        var agents = await _agentService.GetRunningAgentsAsync(ct).ConfigureAwait(false);
        if (!agents.Any())
            return ToolResultBuilder.Success().WithText("清理完成，当前没有运行中的子代理。").Build();
        var lines = agents.Select(a => $"- {a.Id} | {a.Description} | {a.State}");
        return ToolResultBuilder.Success().WithText($"清理完成，仍有 {agents.Count()} 个子代理运行中:\n" + string.Join("\n", lines)).Build();
    }

    /// <summary>从 transcript 恢复单个子代理 — 加载历史对话+工具记录，重新 spawn 带历史的子代理</summary>
    private async Task<ToolResult> ResumeFromTranscriptAsync(string? agentId, string? prompt, CancellationToken ct) {
        if (string.IsNullOrEmpty(agentId))
            return ToolResultBuilder.Error().WithText("resume_from_transcript 需要 id 参数").Build();
        var resumePrompt = string.IsNullOrEmpty(prompt) ? "请继续执行你之前被中断的任务" : prompt;
        try {
            var info = await _agentService.ResumeAgentAsync(new AgentResumeOptions {
                AgentId = agentId,
                NewPrompt = resumePrompt,
                RunInBackground = true,
            }, ct).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText($"子代理 {agentId} 已从 transcript 恢复为新代理 {info.Id}，历史已加载").Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText($"恢复失败：{ex.Message}").Build();
        }
    }

    /// <summary>批量从 transcript 恢复所有被中断的子代理 — 列出所有非运行中子代理，逐个加载历史重新 spawn</summary>
    private async Task<ToolResult> ResumeAllFromTranscriptAsync(string? prompt, CancellationToken ct) {
        var resumePrompt = string.IsNullOrEmpty(prompt) ? "请继续执行你之前被中断的任务" : prompt;
        try {
            var resumed = await _agentService.ResumeAllAgentsAsync(resumePrompt, ct).ConfigureAwait(false);
            if (resumed.Count == 0)
                return ToolResultBuilder.Success().WithText("没有需要恢复的子代理（所有子代理已在运行中或无历史记录）").Build();
            var lines = resumed.Select(a => $"- {a.Id} | {a.Description}");
            return ToolResultBuilder.Success().WithText($"已从 transcript 恢复 {resumed.Count} 个子代理:\n" + string.Join("\n", lines)).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText($"批量恢复失败：{ex.Message}").Build();
        }
    }
}
