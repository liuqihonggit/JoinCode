namespace JoinCode.Abstractions.Tools;

/// <summary>一次 CLI 工具调用所属的 agent/session 权限作用域。</summary>
public sealed record CliAuthorizationScope {
    /// <summary>来源 agent 标识。</summary>
    public required string AgentId { get; init; }

    /// <summary>来源会话标识。</summary>
    public required string SessionId { get; init; }

    /// <summary>允许工具集合；空表示未收紧。</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    /// <summary>拒绝工具集合；优先于允许集合。</summary>
    public IReadOnlyList<string> DeniedTools { get; init; } = [];

    /// <summary>权限模式快照，仅用于审计与后续动态策略。</summary>
    public string? PermissionMode { get; init; }
}

/// <summary>集中授权判定结果。</summary>
public sealed record CliAuthorizationDecision {
    /// <summary>是否允许执行。</summary>
    public required bool Allowed { get; init; }

    /// <summary>允许或拒绝原因。</summary>
    public required string Reason { get; init; }

    /// <summary>触发判定的层，用于审计与错误诱导。</summary>
    public required string Layer { get; init; }

    /// <summary>构造允许结果。</summary>
    /// <param name="layer">允许层。</param>
    /// <returns>允许判定。</returns>
    public static CliAuthorizationDecision Allow(string layer) =>
        new() { Allowed = true, Reason = "allowed", Layer = layer };

    /// <summary>构造拒绝结果。</summary>
    /// <param name="layer">拒绝层。</param>
    /// <param name="reason">可诱导的拒绝原因。</param>
    /// <returns>拒绝判定。</returns>
    public static CliAuthorizationDecision Deny(string layer, string reason) =>
        new() { Allowed = false, Reason = reason, Layer = layer };
}

/// <summary>
/// CLI 工具硬策略判定 — 不解释进程权限模式，Bypass 也不能越过这里。
/// <para>工具域 → 策略工具 → 参数集合 → 工具/参数 effect，顺序固定；所有拒绝都带层名与修复方向。</para>
/// </summary>
public static class CliToolPolicyEvaluator {
    /// <summary>执行硬策略判定。</summary>
    /// <param name="policy">目标 exe 内嵌策略。</param>
    /// <param name="scope">调用方 agent/session 作用域。</param>
    /// <param name="groupId">工具组 ID。</param>
    /// <param name="executableName">目标可执行文件名。</param>
    /// <param name="requestedTool">调用方请求的工具名或别名。</param>
    /// <param name="arguments">已规范化的工具参数。</param>
    /// <returns>集中授权判定。</returns>
    public static CliAuthorizationDecision Evaluate(
        CliToolPolicyManifest policy,
        CliAuthorizationScope scope,
        string groupId,
        string executableName,
        string requestedTool,
        IReadOnlyDictionary<string, string> arguments) {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!policy.Executables.Any(name =>
                string.Equals(name, executableName, StringComparison.OrdinalIgnoreCase))) {
            return CliAuthorizationDecision.Deny(
                "Policy.Executable",
                $"策略 '{policy.PolicyId}' 不适用于可执行文件 '{executableName}'；检查是否启动了错误的工具 exe。");
        }

        var toolIds = policy.Tools.Select(tool => tool.ToolId).ToArray();
        var canonical = ToolAliasTable.Resolve(groupId, toolIds, requestedTool);
        if (canonical is null) {
            return CliAuthorizationDecision.Deny(
                "Policy.Tool",
                $"工具 '{requestedTool}' 不在策略 '{policy.PolicyId}' 的工具清单中；先用 <exe> -h 查看可用工具。");
        }

        var tool = policy.Tools.First(item =>
            string.Equals(item.ToolId, canonical, StringComparison.OrdinalIgnoreCase));
        var aliases = ToolAliasTable.AliasesOf(groupId, tool.ToolId).ToArray();

        var deniedHit = scope.DeniedTools.FirstOrDefault(name => MatchesAnyAlias(name, aliases, groupId, tool.ToolId));
        if (deniedHit is not null) {
            return CliAuthorizationDecision.Deny(
                "Agent.DeniedTools",
                $"工具 '{tool.ToolId}' 被当前 agent 禁止（命中 '{deniedHit}'）；请改用职责范围内的工具。");
        }

        if (scope.AllowedTools.Count > 0
            && !scope.AllowedTools.Any(name => MatchesAnyAlias(name, aliases, groupId, tool.ToolId))) {
            return CliAuthorizationDecision.Deny(
                "Agent.AllowedTools",
                $"工具 '{tool.ToolId}' 不在当前 agent 的工具域中；只读/检索代理不能执行写操作，请交由具备该职责的代理。");
        }

        foreach (var parameterName in arguments.Keys) {
            if (!tool.Parameters.Any(parameter => string.Equals(parameter.Name, parameterName, StringComparison.Ordinal))) {
                return CliAuthorizationDecision.Deny(
                    "Policy.Parameter",
                    $"参数 '{parameterName}' 未登记在工具 '{tool.ToolId}' 的权限策略中；调用方只能传 <exe> <tool> -h 列出的参数。");
            }
        }

        var toolEffect = EvaluateEffect(tool.Effect, "工具", tool.ToolId, policy.PolicyId);
        if (toolEffect is not null) return toolEffect;

        foreach (var parameter in tool.Parameters) {
            if (!arguments.ContainsKey(parameter.Name)) continue;
            var parameterEffect = EvaluateEffect(parameter.Effect, "参数", parameter.Name, policy.PolicyId);
            if (parameterEffect is not null) return parameterEffect;
        }

        return CliAuthorizationDecision.Allow("Policy.Allow");
    }

    private static CliAuthorizationDecision? EvaluateEffect(
        CliPolicyEffect effect,
        string kind,
        string name,
        string policyId) => effect switch {
            CliPolicyEffect.Allow => null,
            CliPolicyEffect.Ask => CliAuthorizationDecision.Deny(
                "Policy.Ask",
                $"{kind} '{name}' 需要交互确认，但当前执行入口没有可用的确认 UI；请在宿主交互会话中重试。"),
            CliPolicyEffect.Deny => CliAuthorizationDecision.Deny(
                "Policy.Deny",
                $"{kind} '{name}' 在策略 '{policyId}' 中为 deny；该能力对当前 CLI 入口不可用。"),
            _ => CliAuthorizationDecision.Deny(
                "Policy.UnknownEffect",
                $"{kind} '{name}' 的 effect 无效；修复策略 '{policyId}' 后重新编译。")
        };

    private static bool MatchesAnyAlias(
        string candidate,
        IReadOnlyList<string> aliases,
        string groupId,
        string canonicalToolId) {
        if (aliases.Any(alias => string.Equals(alias, candidate, StringComparison.OrdinalIgnoreCase))) {
            return true;
        }
        var resolved = ToolAliasTable.Resolve(groupId, [canonicalToolId], candidate);
        return string.Equals(resolved, canonicalToolId, StringComparison.OrdinalIgnoreCase);
    }
}
