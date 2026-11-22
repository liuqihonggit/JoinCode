namespace Tools.Shell;

/// <summary>
/// Shell 沙箱决议 — 同时给出「是否绕过会话沙箱」与「包装后的命令串」。
/// <para><see cref="DisableSandbox"/>=true 表示本次不沙箱执行；<see cref="Command"/> 为实际下发给执行器的命令。</para>
/// </summary>
/// <param name="DisableSandbox">是否绕过沙箱。</param>
/// <param name="Command">实际执行命令（可能已包进 bwrap）。</param>
/// <param name="WrapWarning">包装降级说明（无则 null）。</param>
internal readonly record struct ShellSandboxDecision(bool DisableSandbox, string Command, string? WrapWarning);

/// <summary>
/// Shell 沙箱策略适配 — 把 <see cref="SandboxGate"/> 的纯判定接到 Shell 执行链路。
/// <para>行为契约：</para>
/// <para>1. <c>sandbox.enabled=false</c>（基准缺省）→ 保持旧行为：仅 <c>dangerously_disable_sandbox</c> 生效，
/// 手动 <c>/sandbox</c> 进入的会话沙箱不受影响。</para>
/// <para>2. <c>sandbox.enabled=true</c> → 由门控决定本次命令是否进沙箱；
/// 尚未进入会话沙箱时由 provider 逐命令包装（对齐基准 <c>SandboxManager.wrapWithSandbox</c>）；
/// <c>allowUnsandboxedCommands=false</c> 时逃生阀参数被忽略。</para>
/// </summary>
internal static class ShellSandboxPolicy {
    /// <summary>
    /// 计算本次命令的沙箱执行决议。
    /// </summary>
    /// <param name="command">命令串。</param>
    /// <param name="dangerouslyDisableSandbox">调用方是否显式请求跳出沙箱。</param>
    /// <param name="config">工作流配置（可能为 null — 未装配配置时按缺省）。</param>
    /// <param name="sandboxManager">沙箱管理器（可能为 null — 无沙箱子系统）。</param>
    /// <param name="workingDirectory">命令工作目录（null = 进程当前目录）。</param>
    /// <param name="platform">平台标识（null = 当前平台；测试可指定以覆盖跨平台真值）。</param>
    /// <returns>决议。</returns>
    public static ShellSandboxDecision Resolve(
        string command,
        bool? dangerouslyDisableSandbox,
        WorkflowConfig? config,
        ISandboxManager? sandboxManager,
        string? workingDirectory = null,
        string? platform = null) {
        var dangerously = dangerouslyDisableSandbox == true;
        var sandbox = config?.Sandbox;
        var currentPlatform = platform ?? SandboxGate.CurrentPlatform;

        if (sandbox is null || !sandbox.Enabled) {
            return new ShellSandboxDecision(dangerously, command, null);
        }

        var availability = new SandboxRuntimeAvailability(
            PlatformSupported: SandboxGate.IsPlatformSupported(currentPlatform, sandbox.EnabledPlatforms),
            ProviderAvailable: sandboxManager.HasRealIsolationProvider());

        var decision = SandboxGate.Evaluate(command, sandbox, availability, dangerously);
        Diag.WriteLine($"[SANDBOX] {decision.Describe()} disable={dangerously} session={sandboxManager?.IsInSandbox == true} cmd={command}");

        if (!decision.UseSandbox) {
            return new ShellSandboxDecision(true, command, null);
        }

        // 已手动进入会话沙箱 → 沿用会话级隔离，不再重复包装。
        if (sandboxManager?.IsInSandbox == true) {
            return new ShellSandboxDecision(false, command, null);
        }

        SandboxWrappedCommand? wrapped = null;
        var wrappedSupported = sandboxManager is not null
            && sandboxManager.TryWrapCommandForSandbox(BuildWrapRequest(command, sandbox, workingDirectory), out wrapped)
            && wrapped is not null;
        if (!wrappedSupported || wrapped is null) {
            Diag.WriteLine("[SANDBOX] wrap unavailable — command will run unsandboxed");
            return new ShellSandboxDecision(true, command, "无可用逐命令沙箱包装器，命令将不沙箱执行");
        }

        if (wrapped.Warning is { Length: > 0 } warning) {
            Diag.WriteLine($"[SANDBOX] wrap warning: {warning}");
        }

        return new ShellSandboxDecision(false, wrapped.Command, wrapped.Warning);
    }

    /// <summary>
    /// 计算本次命令是否应绕过沙箱执行（兼容旧调用点，忽略包装结果）。
    /// </summary>
    /// <param name="command">命令串。</param>
    /// <param name="dangerouslyDisableSandbox">调用方是否显式请求跳出沙箱。</param>
    /// <param name="config">工作流配置（可能为 null — 未装配配置时按缺省）。</param>
    /// <param name="sandboxManager">沙箱管理器（可能为 null — 无沙箱子系统）。</param>
    /// <param name="platform">平台标识（null = 当前平台；测试可指定以覆盖跨平台真值）。</param>
    /// <returns>true = 绕过沙箱；false = 走沙箱路径（是否真隔离仍由会话沙箱状态决定）。</returns>
    public static bool ResolveDisableSandbox(
        string command,
        bool? dangerouslyDisableSandbox,
        WorkflowConfig? config,
        ISandboxManager? sandboxManager,
        string? platform = null)
        => Resolve(command, dangerouslyDisableSandbox, config, sandboxManager, platform: platform).DisableSandbox;

    /// <summary>把 settings 的 filesystem/network 清单翻译为 provider 包装请求。</summary>
    private static SandboxWrapRequest BuildWrapRequest(string command, SandboxConfig sandbox, string? workingDirectory) {
        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);
        var filesystem = sandbox.Filesystem;
        var network = sandbox.Network;

        return new SandboxWrapRequest {
            Command = command,
            WorkingDirectory = cwd,
            AllowWritePaths = [.. filesystem.AllowWrite],
            DenyWritePaths = [.. filesystem.DenyWrite],
            AllowReadPaths = [.. filesystem.AllowRead],
            DenyReadPaths = [.. filesystem.DenyRead],
            // 基准 sandbox-runtime 默认断网；域名级放行需代理，bwrap 不支持（provider 侧 fail-closed + 告警）。
            RestrictNetwork = true,
            AllowedDomains = [.. network.AllowedDomains],
            AllowLocalBinding = network.AllowLocalBinding,
            AllowUnixSockets = [.. network.AllowUnixSockets],
            AllowAllUnixSockets = network.AllowAllUnixSockets,
            HttpProxyPort = network.HttpProxyPort,
            SocksProxyPort = network.SocksProxyPort,
            EnableWeakerNestedSandbox = sandbox.EnableWeakerNestedSandbox,
            EnableWeakerNetworkIsolation = sandbox.EnableWeakerNetworkIsolation,
            IgnoreViolations = sandbox.IgnoreViolations,
            RipgrepCommand = sandbox.Ripgrep?.Command,
        };
    }
}
