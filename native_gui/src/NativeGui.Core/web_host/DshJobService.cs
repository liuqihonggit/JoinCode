namespace JoinCode.Gui.WebHost;

/// <summary>
/// 后台任务投影（DSH job/* → 引擎后台代理）：把 <see cref="BackgroundAgentInfo"/> 映射为 DSH JobView，
/// 输出环用「活动流 + 最终输出」的累计文本合成（引擎提供拉取式快照，无事件流）。
/// </summary>
internal sealed class DshJobService {
    private readonly DshHostServices? _services;
    private readonly Func<CancellationToken, Task<IReadOnlyList<BackgroundAgentInfo>>>? _listAgents;
    private readonly Func<string, CancellationToken, Task<bool>>? _stopAgent;

    /// <summary>构造任务投影</summary>
    /// <param name="services">宿主服务容器（取引擎门面）</param>
    internal DshJobService(DshHostServices services) => _services = services;

    /// <summary>测试用构造：直接注入后台代理读取与停止委托</summary>
    /// <param name="listAgents">读取后台代理快照</param>
    /// <param name="stopAgent">停止指定代理</param>
    internal DshJobService(
        Func<CancellationToken, Task<IReadOnlyList<BackgroundAgentInfo>>> listAgents,
        Func<string, CancellationToken, Task<bool>> stopAgent) {
        _listAgents = listAgents;
        _stopAgent = stopAgent;
    }

    /// <summary>是否已接上后台代理数据源（引擎或测试委托）</summary>
    private bool HasBackend => _listAgents is not null || _services?.Engine is not null;

    /// <summary>读取后台代理快照（引擎未就绪时为空）</summary>
    private async Task<IReadOnlyList<BackgroundAgentInfo>> AgentsAsync(CancellationToken cancellationToken) {
        if (_listAgents is { } list) {
            return await list(cancellationToken);
        }
        return _services?.Engine is { } engine
            ? await engine.GetBackgroundAgentsAsync(cancellationToken)
            : [];
    }

    /// <summary>停止指定代理</summary>
    private async Task<bool> StopAgentAsync(string jobId, CancellationToken cancellationToken) {
        if (_stopAgent is { } stop) {
            return await stop(jobId, cancellationToken);
        }
        return _services?.Engine is { } engine && await engine.StopBackgroundAgentAsync(jobId, cancellationToken);
    }

    /// <summary>该会话可见的任务花名册（引擎未就绪时为空，前端显示空态）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<IReadOnlyList<DshJobView>> ListAsync(string sessionId, CancellationToken cancellationToken) {
        var agents = await AgentsAsync(cancellationToken);
        return agents.Select(agent => Project(agent, sessionId)).ToArray();
    }

    /// <summary>取单个任务快照（含合成输出文本与终态判定）；不存在返回 null</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="jobId">任务 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<DshJobSnapshot?> SnapshotAsync(string sessionId, string jobId, CancellationToken cancellationToken) {
        var agents = await AgentsAsync(cancellationToken);
        return agents.Where(agent => string.Equals(agent.AgentId, jobId, StringComparison.Ordinal))
            .Select(agent => new DshJobSnapshot(Project(agent, sessionId), ComposeOutput(agent), IsTerminal(agent.State)))
            .FirstOrDefault();
    }

    /// <summary>受理停止请求（对齐 DSH：已结束返回 already-finished，否则 requested）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="jobId">任务 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受理结果；任务不存在或引擎未就绪时返回错误</returns>
    internal async Task<(string? Outcome, DshRpcError? Error)> KillAsync(string sessionId, string jobId, CancellationToken cancellationToken) {
        if (!HasBackend) {
            return (null, new DshRpcError {
                Code = "job/unavailable",
                Message = "引擎尚未就绪，无法停止后台任务；请等待会话初始化完成后重试",
            });
        }
        var agents = await AgentsAsync(cancellationToken);
        var agent = agents.FirstOrDefault(item => string.Equals(item.AgentId, jobId, StringComparison.Ordinal));
        if (agent is null) {
            return (null, new DshRpcError {
                Code = "job/not-found",
                Message = $"找不到后台任务 {jobId}（会话 {sessionId}）；请先刷新任务列表再选择要停止的任务",
            });
        }
        if (IsTerminal(agent.State)) {
            return ("already-finished", null);
        }
        var stopped = await StopAgentAsync(jobId, cancellationToken);
        return stopped
            ? ("requested", null)
            : (null, new DshRpcError {
                Code = "job/rejected",
                Message = $"引擎拒绝停止任务 {jobId}（当前状态 {agent.State}）；该代理可能不可停止，请改用子代理面板操作",
            });
    }

    /// <summary>后台代理 → DSH JobView</summary>
    private static DshJobView Project(BackgroundAgentInfo agent, string sessionId) {
        var text = ComposeOutput(agent);
        var started = ToUnixMs(agent.StartedAt);
        return new DshJobView {
            Id = agent.AgentId,
            Kind = "subagent",
            Label = string.IsNullOrWhiteSpace(agent.Description) ? agent.Name : agent.Description,
            Owner = sessionId,
            Status = MapStatus(agent),
            Progress = string.IsNullOrWhiteSpace(agent.LastActivityText) ? null : agent.LastActivityText,
            Detail = TerminalDetail(agent),
            StartedAt = started,
            FinishedAt = IsTerminal(agent.State) ? started + Math.Max(0, agent.ExecutionTimeMs ?? 0) : null,
            Output = new DshJobOutputInfo { Total = Encoding.UTF8.GetByteCount(text), Earliest = 0 },
        };
    }

    /// <summary>代理状态 → DSH 任务状态词表</summary>
    private static string MapStatus(BackgroundAgentInfo agent) => agent.State switch {
        AgentStatus.Running => "running",
        AgentStatus.Paused => "stopping",
        AgentStatus.Failed => "failed",
        AgentStatus.Stopped => "killed",
        AgentStatus.Completed => agent.IsSuccess == false ? "failed" : "completed",
        _ => "completed",
    };

    /// <summary>终态补充说明（退出原因 / 最终输出摘要）</summary>
    private static string? TerminalDetail(BackgroundAgentInfo agent) {
        if (!IsTerminal(agent.State)) {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(agent.FinalOutput)) {
            return agent.FinalOutput;
        }
        return agent.State switch {
            AgentStatus.Failed => "failed",
            AgentStatus.Stopped => "cancelled by the user",
            _ => agent.IsSuccess == false ? "failed" : "completed",
        };
    }

    /// <summary>合成输出文本：活动逐行 + 最终输出（顺序稳定，供字节游标增量读取）</summary>
    private static string ComposeOutput(BackgroundAgentInfo agent) {
        var builder = new StringBuilder();
        foreach (var activity in agent.Activities) {
            builder.Append(activity.Glyph ?? "·").Append(' ').Append(activity.Text).Append('\n');
        }
        if (agent.Activities.Count == 0 && !string.IsNullOrWhiteSpace(agent.LastActivityText)) {
            builder.Append(agent.LastActivityText).Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(agent.FinalOutput)) {
            builder.Append('\n').Append(agent.FinalOutput).Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>是否为终态（DSH 终态帧只在输出排空后发送）</summary>
    private static bool IsTerminal(AgentStatus state) => state.IsTerminal();

    /// <summary>DateTime → Unix 毫秒（未标注 Kind 按本地时间解释）</summary>
    private static long ToUnixMs(DateTime? value) {
        if (value is not { } time) {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        var kind = time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Local) : time;
        return new DateTimeOffset(kind).ToUnixTimeMilliseconds();
    }
}

/// <summary>任务跟随快照（投影 + 合成输出 + 终态标记）</summary>
/// <param name="View">任务投影</param>
/// <param name="OutputText">累计输出文本</param>
/// <param name="Terminal">是否已结束</param>
internal sealed record DshJobSnapshot(DshJobView View, string OutputText, bool Terminal);
