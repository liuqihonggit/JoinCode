namespace JoinCode.Abstractions.Configuration;

/// <summary>沙箱启动期可用性结论。</summary>
public enum SandboxStartupOutcome {
    /// <summary>未启用，或启用且可用 — 正常启动。</summary>
    Ok,
    /// <summary>启用但不可用且未要求硬失败 — 警告后降级启动。</summary>
    Warn,
    /// <summary>启用但不可用且 <c>failIfUnavailable=true</c> — 拒绝启动。</summary>
    Fail,
}

/// <summary>沙箱启动期判定结果 — 结论 + 可操作提示。</summary>
/// <param name="Outcome">启动结论。</param>
/// <param name="Message">给用户的说明（含原因、触发配置、下一步）。</param>
public readonly record struct SandboxStartupDecision(SandboxStartupOutcome Outcome, string Message);

/// <summary>
/// 沙箱启动期判定 — 对齐基准 <c>utils/sandbox/sandbox-adapter.ts:562-592</c>：
/// 用户显式开启沙箱但跑不起来时，不能静默忽略；<c>failIfUnavailable=true</c> 必须拒绝启动。
/// </summary>
public static class SandboxStartupGate {
    /// <summary>
    /// 评估启动期沙箱可用性。
    /// </summary>
    /// <param name="config">沙箱配置。</param>
    /// <param name="platformSupported">当前平台是否在 <c>enabledPlatforms</c> 内。</param>
    /// <param name="realIsolationAvailable">是否存在真隔离提供器（文件系统 + 网络）。</param>
    /// <returns>启动结论与提示。</returns>
    public static SandboxStartupDecision Evaluate(
        SandboxConfig config,
        bool platformSupported,
        bool realIsolationAvailable) {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.Enabled) {
            return new SandboxStartupDecision(SandboxStartupOutcome.Ok, string.Empty);
        }

        if (platformSupported && realIsolationAvailable) {
            return new SandboxStartupDecision(SandboxStartupOutcome.Ok, string.Empty);
        }

        var reason = !platformSupported
            ? $"当前平台 {SandboxGate.CurrentPlatform} 不在 sandbox.enabledPlatforms 内"
            : "未找到具备文件系统 + 网络隔离能力的沙箱提供器（bubblewrap / docker）";

        var message =
            $"已启用沙箱（sandbox.enabled=true），但沙箱不可用：{reason}。" +
            "此时命令会不经沙箱直接执行，autoAllowBashIfSandboxed 也不会生效。" +
            "修复方式：安装/启用支持真隔离的沙箱提供器；或把 sandbox.enabled 改回 false；" +
            "或显式接受降级（保留 sandbox.enabled=true 但设 sandbox.failIfUnavailable=false）。";

        return config.FailIfUnavailable
            ? new SandboxStartupDecision(
                SandboxStartupOutcome.Fail,
                message + " 当前 sandbox.failIfUnavailable=true，拒绝启动。")
            : new SandboxStartupDecision(SandboxStartupOutcome.Warn, message);
    }
}
