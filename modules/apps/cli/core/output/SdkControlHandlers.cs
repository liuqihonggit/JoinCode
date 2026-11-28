namespace JoinCode.Cli.Output;

/// <summary>
/// <c>interrupt</c> 子类型（TASK144 骨架的参照实现，语义沿用 TASK142-B）：
/// 应答空载荷 + 请求取消在途回合。取消动作本身不在这里执行 —— handler 不持有回合句柄，
/// 由调用方读 <see cref="SdkControlOutcome.CancelActiveTurn"/> 后调用自己的取消器（现网是
/// <c>NonInteractiveModeRunner.TurnCancellation.CancelActive</c>）。
/// </summary>
public sealed class InterruptControlHandler : ISdkControlHandler {
    /// <summary>协议子类型名。</summary>
    public string Subtype => "interrupt";

    /// <summary>确认收到并请求取消在途回合。</summary>
    /// <param name="command">控制请求</param>
    /// <returns>处理结果</returns>
    public SdkControlOutcome Handle(SdkControlCommand command) =>
        SdkControlOutcome.Interrupt(command.RequestId);
}

/// <summary>
/// <c>set_permission_mode</c> 子类型（TASK144 骨架的参照实现，语义沿用 TASK143）：
/// 解析 → 应用 → 回**实际生效值**；未知值 / 引擎无权限系统 / 缺 mode 都回 error。
/// <para>解析与应用交给 <see cref="PermissionModeApplierFactory"/>（唯一数据源：合法值清单、
/// 失败文案、bypass 安全闸说明都在那里），本 handler 只负责协议字段与应答形状。</para>
/// </summary>
public sealed class SetPermissionModeControlHandler : ISdkControlHandler {
    private readonly Func<string, string?>? _applyPermissionMode;

    /// <summary>
    /// 构造 handler。
    /// </summary>
    /// <param name="applyPermissionMode">
    /// 应用器：协议字符串 → 实际生效模式字符串；返回 <c>null</c> = 未知值或引擎无权限系统。
    /// </param>
    public SetPermissionModeControlHandler(Func<string, string?>? applyPermissionMode) =>
        _applyPermissionMode = applyPermissionMode;

    /// <summary>协议子类型名。</summary>
    public string Subtype => "set_permission_mode";

    /// <summary>应用权限模式并按实际生效值应答。</summary>
    /// <param name="command">控制请求（<c>request.mode</c> 为所需字段）</param>
    /// <returns>处理结果</returns>
    public SdkControlOutcome Handle(SdkControlCommand command) {
        var mode = SdkControlPayload.ReadString(command.Payload, "mode");
        if (string.IsNullOrWhiteSpace(mode))
            return SdkControlOutcome.Reject(
                command.RequestId,
                $"set_permission_mode 缺 request.mode；合法值：{PermissionModeApplierFactory.ValidPermissionModes}。");

        // 确定性失败不回 success：否则客户端以为权限已放宽，后续工具调用被拒时无法归因。
        return _applyPermissionMode?.Invoke(mode) is { } effectiveMode
            ? SdkControlOutcome.Acknowledge(command.RequestId, effectiveMode)
            : SdkControlOutcome.Reject(command.RequestId, PermissionModeApplierFactory.DescribeFailure(mode));
    }
}
