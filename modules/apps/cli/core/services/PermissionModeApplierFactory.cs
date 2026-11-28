namespace JoinCode;

/// <summary>
/// 权限模式应用器工厂（TASK143）— 协议字符串 → 枚举 → 应用 → 读回实际生效值。
/// <para>**唯一一份**：bridge_serve 的 <c>permission_mode_changed</c> 与 SDK 流式输入的
/// <c>control_response</c> 共用同一套解析、应用与失败文案。此前
/// <c>BridgeServeCommand.BuildPermissionModeApplier</c> 与 <c>EngineSession.BuildPermissionModeApplier</c>
/// 各自持有实现与合法值清单，本类先收编 CLI 侧两处（Engine 侧用不同枚举，不在此列）。</para>
/// <para>返回值语义：<c>null</c> = 未知值或引擎无权限系统（调用方回 error）；非 <c>null</c> = 实际生效模式，
/// 可能与请求值不同 —— bypass 受 settings.json 的 <c>disableBypassPermissionsMode</c> 安全闸管控。</para>
/// </summary>
internal static class PermissionModeApplierFactory {
    /// <summary>
    /// 合法权限模式值清单（与 <c>PermissionMode</c> 的 <c>[EnumValue]</c> 一致）。
    /// <para>给客户端看的契约文案都引用这里，避免「提示里说能用、实际 FromValue 返回 null」。</para>
    /// </summary>
    internal const string ValidPermissionModes = "plan, auto, ask, bypass, unattended";

    /// <summary>
    /// 从 DI 容器解析权限检查器并等待其模式初始化完成。
    /// </summary>
    /// <param name="services">服务容器</param>
    /// <returns>权限检查器；引擎未注册 <see cref="IPermissionChecker"/> 时为 <c>null</c></returns>
    internal static async Task<PermissionChecker?> ResolveAsync(IServiceProvider services) {
        if (services.GetService<IPermissionChecker>() is not PermissionChecker checker)
            return null;

        await checker.ModeInitialization.ConfigureAwait(false);
        return checker;
    }

    /// <summary>
    /// 构造权限模式应用器。
    /// </summary>
    /// <param name="checker">权限检查器（<c>null</c> = 无权限系统）</param>
    /// <returns>应用器；无权限系统时为 <c>null</c></returns>
    internal static BridgeServeLoop.PermissionModeApplier? Create(PermissionChecker? checker) {
        if (checker is null)
            return null;

        return mode => {
            var parsed = PermissionModeExtensions.FromValue(mode);
            if (parsed is null)
                return null;

            checker.CurrentMode = parsed.Value;
            return checker.CurrentMode;
        };
    }

    /// <summary>
    /// 应用失败的统一文案（bridge_serve 的 error 事件与 SDK 的 control_response.error 共用）。
    /// </summary>
    /// <param name="mode">客户端请求的模式字符串</param>
    /// <returns>可读原因（含合法值清单与 bypass 安全闸说明）</returns>
    internal static string DescribeFailure(string mode) =>
        $"权限模式 '{mode}' 无法应用（未知值或引擎未提供权限系统）。合法值：{ValidPermissionModes}。bypass 受 settings.json 的 disableBypassPermissionsMode 安全闸管控，被拦截时保持原模式。";
}
