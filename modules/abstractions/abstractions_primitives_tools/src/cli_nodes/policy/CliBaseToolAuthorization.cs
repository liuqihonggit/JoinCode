namespace JoinCode.Abstractions.Tools;

/// <summary>
/// jcc 启动器元命令与其自有选项的单一登记表 — 策略文件与消费代码都从这里取名字，
/// 防止「命令行解析加了选项、base 策略没登记」（有参无权）。
/// </summary>
public static class CliLauncherPolicyCatalog {
    /// <summary><c>jcc tool call</c>。</summary>
    public const string ToolCall = "tool_call";

    /// <summary><c>jcc tool tree</c>。</summary>
    public const string ToolTree = "tool_tree";

    /// <summary><c>jcc tool find</c>。</summary>
    public const string ToolFind = "tool_find";

    /// <summary><c>jcc tool help</c>。</summary>
    public const string ToolHelp = "tool_help";

    /// <summary><c>jcc slash_call</c>。</summary>
    public const string SlashCall = "slash_call";

    /// <summary><c>jcc mcp_call</c>。</summary>
    public const string McpCall = "mcp_call";

    /// <summary><c>--exe</c>：显式指定工具 exe（可指向无策略 exe，agent 会话内禁止）。</summary>
    public const string Exe = "exe";

    /// <summary><c>--dir</c>：指定 node.yaml 目录（可改写工具解析来源，agent 会话内禁止）。</summary>
    public const string Dir = "dir";

    /// <summary><c>--timeout</c>：执行超时秒数。</summary>
    public const string Timeout = "timeout";

    /// <summary><c>--resume</c>：跨会话恢复（斜杠命令直调跨会话执行，agent 会话内禁止）。</summary>
    public const string Resume = "resume";

    /// <summary><c>--args-file</c>：从文件读取工具参数。</summary>
    public const string ArgsFile = "args_file";

    /// <summary><c>--args-stdin</c>：从 stdin 读取工具参数。</summary>
    public const string ArgsStdin = "args_stdin";

    /// <summary><c>--json</c>：JSON 输出。</summary>
    public const string Json = "json";

    /// <summary><c>--vendor</c>：宿主供应商覆盖。</summary>
    public const string Vendor = "vendor";

    /// <summary><c>--model</c>：宿主模型覆盖。</summary>
    public const string Model = "model";

    /// <summary>每个元命令登记的启动器选项（name 不带 <c>--</c>）。</summary>
    public static IReadOnlyDictionary<string, string[]> Tools { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal) {
            [ToolCall] = [Exe, Dir, Timeout],
            [ToolTree] = [Dir],
            [ToolFind] = [Dir],
            [ToolHelp] = [Dir],
            [SlashCall] = [Resume],
            [McpCall] = [ArgsFile, ArgsStdin, Json, Vendor, Model],
        };
}

/// <summary>
/// jcc 启动器（base 元命令）参数授权 — 只约束 agent 会话内的启动器自有选项。
/// <para>手工终端调用没有 broker 会话，直接放行（Base.Manual）；
/// agent 会话内任何未登记或 deny 的选项一律 fail-closed。</para>
/// </summary>
public static class CliBaseToolAuthorization {
    private const string PolicyId = "cli.base";

    private static readonly Lazy<BasePolicyLoad> CachedPolicy = new(LoadBasePolicy);

    /// <summary>判定启动器选项在当前会话内是否允许。</summary>
    /// <param name="metaCommand">元命令 canonical 名（见 <see cref="CliLauncherPolicyCatalog"/>）。</param>
    /// <param name="parameter">启动器选项名（不带 <c>--</c>）。</param>
    /// <returns>授权判定；拒绝时带层名与正确写法。</returns>
    public static CliAuthorizationDecision Authorize(string metaCommand, string parameter) {
        ArgumentException.ThrowIfNullOrWhiteSpace(metaCommand);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return HasSession()
            ? AuthorizeWithPolicy(metaCommand, parameter, CachedPolicy.Value)
            : CliAuthorizationDecision.Allow("Base.Manual");
    }

    internal static CliAuthorizationDecision Authorize(
        string metaCommand,
        string parameter,
        string? imagePath) {
        if (!HasSession()) return CliAuthorizationDecision.Allow("Base.Manual");
        if (string.IsNullOrWhiteSpace(imagePath)) {
            return DenyPolicyUnavailable("无法解析 jcc 镜像路径");
        }
        return AuthorizeWithPolicy(metaCommand, parameter, LoadPolicy(imagePath));
    }

    private static bool HasSession() {
        var pipeName = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.PipeVariable);
        var token = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.TokenVariable);
        return !string.IsNullOrWhiteSpace(pipeName) && !string.IsNullOrWhiteSpace(token);
    }

    internal static CliAuthorizationDecision AuthorizeWithPolicy(
        string metaCommand,
        string parameter,
        BasePolicyLoad load) {
        if (load.Policy is null) return DenyPolicyUnavailable(load.Error);
        var tool = load.Policy.Manifest.Tools.FirstOrDefault(
            candidate => string.Equals(candidate.ToolId, metaCommand, StringComparison.Ordinal));
        if (tool is null) {
            return CliAuthorizationDecision.Deny(
                "Base.Undeclared",
                $"启动器元命令 '{metaCommand}' 未在 base 策略登记（有参无权）。"
                + $"正确写法: 在 resources/tools/base_policy.yaml 补登记该元命令后重新编译 jcc。");
        }
        var declared = tool.Parameters.FirstOrDefault(
            candidate => string.Equals(candidate.Name, parameter, StringComparison.Ordinal));
        if (declared is null) {
            return CliAuthorizationDecision.Deny(
                "Base.Undeclared",
                $"启动器选项 '--{parameter}' 未在 base 策略登记（有参无权）。"
                + $"正确写法: 在 resources/tools/base_policy.yaml 的 {metaCommand} 下补条目后重新编译 jcc。");
        }
        return declared.Effect switch {
            CliPolicyEffect.Allow => CliAuthorizationDecision.Allow("Base.Allow"),
            CliPolicyEffect.Deny => CliAuthorizationDecision.Deny(
                "Base.AgentForbidden",
                $"agent 会话内禁止 '--{parameter}'：它会改变工具解析/执行目标或跨会话执行，绕过集中鉴权。"
                + $"正确写法: 去掉 '--{parameter}'，使用 jcc tool call <分组> <工具> 的固定入口。"),
            CliPolicyEffect.Ask => CliAuthorizationDecision.Deny(
                "Base.Ask",
                $"agent 会话内 '--{parameter}' 需要宿主确认；当前 CLI 入口没有确认 UI。"
                + "正确写法: 改为在交互会话中手动执行。"),
            _ => CliAuthorizationDecision.Deny(
                "Base.Undeclared",
                $"启动器选项 '--{parameter}' 的 effect 无法识别。"
                + "正确写法: 检查 resources/tools/base_policy.yaml 的 effect 取值（allow/ask/deny）。")
        };
    }

    private static BasePolicyLoad LoadPolicy(string imagePath) {
        try {
            // 入口镜像在非 AOT 构建下是 jcc.dll、AOT 下是 jcc.exe，因此按 policy_id 定位而不校验文件名。
            var loaded = CliToolPolicyResourceReader.LoadWithHashFromExecutable(
                imagePath,
                expectedExecutableName: null,
                expectedPolicyId: PolicyId);
            return new BasePolicyLoad(loaded, null);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                         or InvalidOperationException or BadImageFormatException or YamlException) {
            return new BasePolicyLoad(null, ex.Message);
        }
    }

    private static BasePolicyLoad LoadBasePolicy() {
        var imagePath = ResolveImagePath();
        return string.IsNullOrWhiteSpace(imagePath)
            ? new BasePolicyLoad(null, "无法解析 jcc 镜像路径")
            : LoadPolicy(imagePath);
    }

    private static string? ResolveImagePath() => CliToolAuthorizationClient.ResolveEntryImagePath();

    private static CliAuthorizationDecision DenyPolicyUnavailable(string? detail) =>
        CliAuthorizationDecision.Deny(
            "Base.PolicyLoad",
            $"无法读取内嵌 base 启动器策略：{detail ?? "未知错误"}。"
            + "正确写法: 重新编译 jcc 并确认 base_policy.yaml 已作为 EmbeddedResource 嵌入。");

    /// <summary>base 策略加载结果（失败时 fail-closed，不用异常穿过调用方）。</summary>
    internal sealed record BasePolicyLoad(LoadedCliToolPolicy? Policy, string? Error);
}
