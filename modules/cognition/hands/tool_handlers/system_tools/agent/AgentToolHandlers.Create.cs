namespace Tools.Handlers;

/// <summary>
/// AgentToolHandlers 创建职责 — 创建并启动子代理、dry-run 模式、dry-run 状态持久化管理。
/// 从 AgentToolHandlers.cs 拆分而来（单一职责：Create）。
/// </summary>
public sealed partial class AgentToolHandlers {

    /// <summary>
    /// 创建并启动子代理
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.Agent, "Create and launch a sub-agent to handle a task", AgentToolNameEnumConstants.Agent)]
    public async Task<ToolResult> CreateAgentAsync(
        [McpToolOptions] AgentCreateOptions options,
        CancellationToken cancellationToken = default) {
        if (options.DryRun == true)
            return await CreateDryRunAgentAsync(options, cancellationToken).ConfigureAwait(false);

        var context = new AgentToolContext {
            Description = options.Description,
            Prompt = options.Prompt,
            SubagentType = options.SubagentType,
            Model = options.Model,
            Name = options.Name,
            RunInBackground = options.RunInBackground,
            Isolation = options.Isolation,
            Cwd = options.Cwd,
            Memory = options.Memory,
        };

        try {
            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            return context.Result ?? ToolResultBuilder.PipelineNoResult();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.AgentCreateFailed));
            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "spawn", false);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("agent", ex, _logger);
        }
    }

    /// <summary>
    /// 干跑模式 — 不调用 LLM，直接创建 mock agent 并持久化到 ~/.jcc/agents/，支持跨进程测试
    /// dry_run 也走 worktree 隔离决策链路,验证完整链路: isolation 决策 → worktree 创建 → cwd 隔离
    /// </summary>
    private async Task<ToolResult> CreateDryRunAgentAsync(AgentCreateOptions options, CancellationToken cancellationToken) {
        var agentId = $"agent-dryrun-{Guid.NewGuid():N}"[..^16];
        var now = _clock.GetUtcNow();
        var stateDir = AppDataConstants.Paths.AgentsDirectory;
#pragma warning disable JCC9001
        Directory.CreateDirectory(stateDir);
#pragma warning restore JCC9001
        var statePath = Path.Combine(stateDir, $"{agentId}.json");

        var isolationMode = ResolveDryRunIsolationMode(options.Isolation);
        string? worktreePath = null;
        string? worktreeBranch = null;

        if (isolationMode == AgentIsolationMode.Worktree && _worktreeManager is not null) {
            try {
                var wtSession = await _worktreeManager.CreateWorktreeForAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
                if (wtSession is not null) {
                    worktreePath = wtSession.WorktreePath;
                    worktreeBranch = wtSession.BranchName;
                }
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "dry_run worktree 创建失败,降级为 none 隔离");
                isolationMode = AgentIsolationMode.None;
            }
        }

        var state = new DryRunAgentState {
            Id = agentId,
            Description = options.Description,
            Status = "running",
            StartedAt = now,
            Prompt = options.Prompt,
            IsolationMode = isolationMode.ToValue(),
            WorktreePath = worktreePath,
            WorktreeBranch = worktreeBranch,
        };
#pragma warning disable JCC9001
        await File.WriteAllTextAsync(statePath, RelaxedJsonSerializer.Serialize(state, DryRunAgentStateJsonContext.Default), cancellationToken).ConfigureAwait(false);
#pragma warning restore JCC9001
        var response = new System.Text.StringBuilder();
        response.AppendLine("Agent launched in dry-run mode (no LLM)");
        response.AppendLine($"Agent ID: {agentId}");
        response.AppendLine($"Description: {options.Description}");
        response.AppendLine($"Status: running");
        response.AppendLine($"Isolation: {isolationMode.ToValue()}");
        if (worktreePath is not null) {
            response.AppendLine($"WorktreePath: {worktreePath}");
            response.AppendLine($"WorktreeBranch: {worktreeBranch}");
        }
        response.AppendLine();
        response.AppendLine("Use agent_status to query status, agent_stop to stop, agent_get_messages to get messages.");
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 解析 dry_run 隔离模式 — 与 AgentForkMiddleware.ResolveTeammateIsolationMode 同逻辑
    /// 优先级: 显式 isolation > WorktreeDecisionPolicy.Decide > None
    /// </summary>
    private AgentIsolationMode ResolveDryRunIsolationMode(string? explicitIsolation) {
        var explicitMode = AgentIsolationModeExtensions.FromValue(explicitIsolation);
        if (explicitMode is not null)
            return explicitMode.Value;

        if (_worktreeDecisionPolicy is null || _worktreeManager is null)
            return AgentIsolationMode.None;

        var enableWorktree = _worktreeManager.IsWorktreeIsolationEnabled;
        return _worktreeDecisionPolicy.Decide(enableWorktree, ExecutorVariant.Teammate);
    }

    /// <summary>
    /// 从 ~/.jcc/agents/{agentId}.json 加载 dry-run agent 状态
    /// </summary>
    private DryRunAgentState? TryLoadDryRunState(string agentId) {
        var statePath = Path.Combine(
            AppDataConstants.Paths.AgentsDirectory, $"{agentId}.json");
#pragma warning disable JCC9001
        if (!File.Exists(statePath))
            return null;
        try {
            var json = File.ReadAllText(statePath);
            return RelaxedJsonSerializer.Deserialize(json, DryRunAgentStateJsonContext.Default.DryRunAgentState);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to load dry-run agent state for {AgentId}", agentId);
            return null;
        }
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 列出 ~/.jcc/agents/ 目录下所有 dry-run agent 状态文件
    /// </summary>
    private List<DryRunAgentState> ListDryRunAgents() {
        var stateDir = AppDataConstants.Paths.AgentsDirectory;
#pragma warning disable JCC9001
        if (!Directory.Exists(stateDir))
            return [];
        var result = new List<DryRunAgentState>();
        foreach (var file in Directory.EnumerateFiles(stateDir, "*.json")) {
            if (file.EndsWith(".messages.json", StringComparison.OrdinalIgnoreCase))
                continue;
            try {
                var json = File.ReadAllText(file);
                var state = RelaxedJsonSerializer.Deserialize(json, DryRunAgentStateJsonContext.Default.DryRunAgentState);
                if (state is not null)
                    result.Add(state);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "Failed to load dry-run agent state from {File}", file);
            }
        }
        return result;
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 保存 dry-run agent 状态到 ~/.jcc/agents/{agentId}.json
    /// </summary>
    private void TrySaveDryRunState(DryRunAgentState state) {
        var stateDir = AppDataConstants.Paths.AgentsDirectory;
#pragma warning disable JCC9001
        Directory.CreateDirectory(stateDir);
        var statePath = Path.Combine(stateDir, $"{state.Id}.json");
        try {
            File.WriteAllText(statePath, RelaxedJsonSerializer.Serialize(state, DryRunAgentStateJsonContext.Default));
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to save dry-run agent state for {AgentId}", state.Id);
        }
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 追加 dry-run agent 消息到 ~/.jcc/agents/{agentId}.messages.json
    /// </summary>
    private void AppendDryRunMessage(string agentId, string content, string? summary) {
        var msgDir = AppDataConstants.Paths.AgentsDirectory;
#pragma warning disable JCC9001
        Directory.CreateDirectory(msgDir);
        var msgPath = Path.Combine(msgDir, $"{agentId}.messages.json");
        var msgs = new List<DryRunAgentMessage>();
        if (File.Exists(msgPath)) {
            try {
                var existing = File.ReadAllText(msgPath);
                var loaded3 = RelaxedJsonSerializer.Deserialize(existing, DryRunAgentStateJsonContext.Default.ListDryRunAgentMessage);
                if (loaded3 is not null)
                    msgs = [.. loaded3];
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "Failed to load dry-run messages for {AgentId}", agentId);
            }
        }
        msgs.Add(new DryRunAgentMessage { Content = content, Summary = summary, Timestamp = _clock.GetUtcNow() });
        try {
            File.WriteAllText(msgPath, RelaxedJsonSerializer.Serialize(msgs, DryRunAgentStateJsonContext.Default));
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to save dry-run messages for {AgentId}", agentId);
        }
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 从 ~/.jcc/agents/{agentId}.messages.json 加载 dry-run agent 消息
    /// </summary>
    private List<DryRunAgentMessage>? TryLoadDryRunMessages(string agentId) {
        var msgPath = Path.Combine(
            AppDataConstants.Paths.AgentsDirectory, $"{agentId}.messages.json");
#pragma warning disable JCC9001
        if (!File.Exists(msgPath))
            return null;
        try {
            var json = File.ReadAllText(msgPath);
            return RelaxedJsonSerializer.Deserialize(json, DryRunAgentStateJsonContext.Default.ListDryRunAgentMessage)?.ToList();
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to load dry-run messages for {AgentId}", agentId);
            return null;
        }
#pragma warning restore JCC9001
    }
}
