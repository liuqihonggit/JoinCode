namespace Core.Permission;

/// <summary>
/// 沙箱内自动免确认中间件 — 对齐基准 <c>autoAllowBashIfSandboxed</c>
/// （<c>tools/BashTool/bashPermissions.ts:1349-1361</c>、<c>utils/permissions/permissions.ts:1186-1200</c>）。
/// <para>免确认只在<b>真的会进沙箱</b>时生效，四个条件同时成立：</para>
/// <para>1. <c>sandbox.enabled=true</c> 且 <c>autoAllowBashIfSandboxed=true</c>；</para>
/// <para>2. 会话已进入沙箱（<c>ISandboxManager.IsInSandbox</c>）；</para>
/// <para>3. 当前提供器具备真实隔离能力（文件系统 + 网络隔离）——只有路径重定向的 Soft/Process 不算，
/// 否则免确认会变成权限绕过（基准在 Windows 上根本不启用沙箱）；</para>
/// <para>4. 本次命令经 <see cref="SandboxGate"/> 判定仍会进沙箱（排除清单 / 逃生阀命令照常 ask）。</para>
/// </summary>
[Register(typeof(IPermissionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class SandboxAutoAllowMiddleware : ServiceEntity, IPermissionMiddleware {
    private readonly JoinCode.Abstractions.Configuration.SandboxConfig _sandbox;
    private readonly ISandboxManager? _sandboxManager;

    /// <summary>
    /// 构造中间件。
    /// </summary>
    /// <param name="config">工作流配置（读取 <c>sandbox</c> 节点）。</param>
    /// <param name="sandboxManager">沙箱管理器（可选 — 无沙箱子系统时免确认不生效）。</param>
    public SandboxAutoAllowMiddleware(WorkflowConfig config, ISandboxManager? sandboxManager = null) {
        ArgumentNullException.ThrowIfNull(config);
        _sandbox = config.Sandbox;
        _sandboxManager = sandboxManager;
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
        if (!TryApprove(context)) {
            return next(context, ct);
        }

        context.Result = ToolPermissionCheckResult.Approved();
        Diag.WriteLine($"[SANDBOX] auto-allow tool={context.ToolName} provider={_sandboxManager?.ActiveSandboxType.ToValue()}");
        return Task.CompletedTask;
    }

    /// <summary>判定本工具调用是否可免确认。</summary>
    private bool TryApprove(PermissionCheckContext context) {
        if (!_sandbox.Enabled || !_sandbox.AutoAllowBashIfSandboxed) {
            return false;
        }

        if (!IsSandboxableShellTool(context.ToolName)) {
            return false;
        }

        var manager = _sandboxManager;
        if (manager is not { IsInSandbox: true }) {
            return false;
        }

        var provider = manager.ActiveProvider;
        if (provider is null || !provider.Capabilities.HasRealIsolation()) {
            return false;
        }

        var command = PermissionRuleParser.ExtractCommand(context.Arguments);
        var availability = new SandboxRuntimeAvailability(
            PlatformSupported: SandboxGate.IsPlatformSupported(SandboxGate.CurrentPlatform, _sandbox.EnabledPlatforms),
            ProviderAvailable: true);
        var dangerously = ReadBoolArgument(context.Arguments, "dangerously_disable_sandbox");
        var decision = SandboxGate.Evaluate(command, _sandbox, availability, dangerously);

        if (!decision.UseSandbox) {
            Diag.WriteLine($"[SANDBOX] auto-allow skipped: {decision.Describe()} cmd={command}");
        }

        return decision.UseSandbox;
    }

    /// <summary>是否为受沙箱门控的 Shell 工具（Bash / PowerShell）。</summary>
    private static bool IsSandboxableShellTool(string toolName)
        => string.Equals(toolName, ShellToolNameEnumConstants.Bash, StringComparison.OrdinalIgnoreCase)
           || string.Equals(toolName, ShellToolNameEnumConstants.Powershell, StringComparison.OrdinalIgnoreCase);

    /// <summary>读取布尔参数 — 缺失或非布尔返回 false。</summary>
    internal static bool ReadBoolArgument(Dictionary<string, JsonElement> arguments, string key)
        => arguments.TryGetValue(key, out var element)
           && element.ValueKind == JsonValueKind.True;
}
