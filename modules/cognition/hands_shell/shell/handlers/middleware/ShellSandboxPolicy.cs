namespace Tools.Shell;

/// <summary>
/// Shell 沙箱策略适配 — 把 <see cref="SandboxGate"/> 的纯判定接到 Shell 执行链路。
/// <para>行为契约：</para>
/// <para>1. <c>sandbox.enabled=false</c>（基准缺省）→ 保持旧行为：仅 <c>dangerously_disable_sandbox</c> 生效，
/// 手动 <c>/sandbox</c> 进入的会话沙箱不受影响。</para>
/// <para>2. <c>sandbox.enabled=true</c> → 由门控决定本次命令是否进沙箱；
/// <c>allowUnsandboxedCommands=false</c> 时逃生阀参数被忽略（企业强制全沙箱）。</para>
/// </summary>
internal static class ShellSandboxPolicy {
    /// <summary>
    /// 计算本次命令是否应绕过沙箱执行。
    /// </summary>
    /// <param name="command">命令串。</param>
    /// <param name="dangerouslyDisableSandbox">调用方是否显式请求跳出沙箱。</param>
    /// <param name="config">工作流配置（可能为 null — 未装配配置时按缺省）。</param>
    /// <param name="sandboxManager">沙箱管理器（可能为 null — 无沙箱子系统）。</param>
    /// <returns>true = 绕过沙箱；false = 走沙箱路径（是否真隔离仍由会话沙箱状态决定）。</returns>
    public static bool ResolveDisableSandbox(
        string command,
        bool? dangerouslyDisableSandbox,
        WorkflowConfig? config,
        ISandboxManager? sandboxManager) {
        var dangerously = dangerouslyDisableSandbox == true;
        var sandbox = config?.Sandbox;

        if (sandbox is null || !sandbox.Enabled) {
            return dangerously;
        }

        var availability = new SandboxRuntimeAvailability(
            PlatformSupported: SandboxGate.IsPlatformSupported(SandboxGate.CurrentPlatform, sandbox.EnabledPlatforms),
            ProviderAvailable: sandboxManager.HasRealIsolationProvider());

        var decision = SandboxGate.Evaluate(command, sandbox, availability, dangerously);
        Diag.WriteLine($"[SANDBOX] {decision.Describe()} disable={dangerously} session={sandboxManager?.IsInSandbox == true} cmd={command}");

        return !decision.UseSandbox;
    }
}
