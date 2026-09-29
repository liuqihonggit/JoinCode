namespace Tools.Handlers;

/// <summary>
/// 子代理控制选项 — 统一工具 subagent_control 的参数
/// </summary>
public sealed record SubAgentControlOptions {
    /// <summary>操作类型: list/pause/resume/pause_all/resume_all</summary>
    [McpToolParameter("Action: list/pause/resume/pause_all/resume_all")]
    public required string Action { get; init; }

    /// <summary>子代理 ID — pause/resume 必填，list/pause_all/resume_all 忽略</summary>
    [McpToolParameter("Sub-agent ID (required for pause/resume)", Required = false)]
    public string? Id { get; init; }
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
    /// 子代理控制 — list/pause/resume/pause_all/resume_all 统一入口。
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.SubAgentControl, "Control sub-agents: list/pause/resume/pause_all/resume_all", AgentToolNameEnumConstants.Agent)]
    public async Task<ToolResult> ControlAsync(
        [McpToolOptions] SubAgentControlOptions options,
        CancellationToken cancellationToken = default) {
        return options.Action switch {
            "list" => await ListAsync(cancellationToken).ConfigureAwait(false),
            "pause" => await PauseAsync(options.Id, cancellationToken).ConfigureAwait(false),
            "resume" => await ResumeAsync(options.Id, cancellationToken).ConfigureAwait(false),
            "pause_all" => await PauseAllAsync(cancellationToken).ConfigureAwait(false),
            "resume_all" => await ResumeAllAsync(cancellationToken).ConfigureAwait(false),
            _ => ToolResultBuilder.Error().WithText($"未知 action: {options.Action}，有效值: list/pause/resume/pause_all/resume_all").Build()
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
}
