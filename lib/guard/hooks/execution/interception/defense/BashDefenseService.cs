namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// Bash 防御服务 — 薄编排层，注入各 node，适配 node 到 <see cref="BashDefenseStepAsync"/>。
/// <para>
/// 一切皆为 node/插件（ADR 0104）：每个 node 是独立公共对象，可被任意工具注入使用。
/// 本服务职责：① 注入 node ② 将 node 结果包装为 ToolResult + 诊断 ③ 提供 Begin() 入口。
/// </para>
/// <para>
/// 调用方组装示例（LINQ 链式 + 有名函数）：
/// <code>
/// var (ctx, rejection) = await _bashDefenseService
///     .Begin(command, workDir, shellKind, confirmMode, confirmedCmd, argvHash)
///     .Then(_bashDefenseService.CheckRetainedDevice)
///     .Then(_bashDefenseService.CheckRedirectWhitelist)
///     .Then(_bashDefenseService.RequireArgvHash)
///     .ExecuteAsync(ct);
/// </code>
/// </para>
/// </summary>
[Register(typeof(BashDefenseService), ServiceLifetime.Singleton)]
public sealed class BashDefenseService
{
    private readonly RetainedDeviceNode _retainedDeviceNode;

    /// <summary>
    /// 构造 Bash 防御服务
    /// </summary>
    /// <param name="retainedDeviceNode">保留设备名检测 node</param>
    public BashDefenseService(RetainedDeviceNode retainedDeviceNode)
    {
        _retainedDeviceNode = retainedDeviceNode ?? throw new ArgumentNullException(nameof(retainedDeviceNode));
    }

    /// <summary>
    /// 开始构建 Bash 防御链。返回 <see cref="BashDefense"/> 构建器，链式追加防御步骤。
    /// </summary>
    /// <param name="command">原始命令字符串</param>
    /// <param name="workingDirectory">工作目录路径</param>
    /// <param name="shellKind">Shell 类型</param>
    /// <param name="confirmMode">确认模式（默认 None）</param>
    /// <param name="confirmedCommand">已确认命令（二次确认场景，默认 null）</param>
    /// <param name="argvHash">argv hash（防意图反推，默认 null）</param>
    /// <returns>Bash 防御链构建器</returns>
    public BashDefense Begin(
        string command,
        string workingDirectory,
        SystemActuatorKind shellKind,
        GuardConfirmMode confirmMode = GuardConfirmMode.None,
        string? confirmedCommand = null,
        string? argvHash = null)
        => BashDefense.Begin(command, workingDirectory, shellKind, confirmMode, confirmedCommand, argvHash);

    // ════════════════════════════════════════════════════════════════════
    //  防御步骤（委托各 node，适配 ToolResult）— 有名函数，非 lambda
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 保留设备名检测 — 委托 <see cref="RetainedDeviceNode"/>，检测到重定向到保留设备名构建拒绝诊断。
    /// <para>
    /// 拒绝消息封死替代路径（MTP 扰动纵深防御约束第5条）：
    /// <list type="bullet">
    /// <item>显式列出同类禁止写法（&gt;NUL / &gt;nul. / &gt;con）</item>
    /// <item>提示疑似 MTP 扰动，建议丢弃当前命令完整重新生成</item>
    /// <item>不在错误字符串上局部修补（约束第1条）</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="ctx">Bash 防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> CheckRetainedDevice(BashDefenseContext ctx, CancellationToken ct)
    {
        var deviceName = _retainedDeviceNode.FindRetainedDevice(ctx.CurrentCommand);
        if (deviceName is null)
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildRetainedDeviceRejectedDiagnostic(deviceName);
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    // ════════════════════════════════════════════════════════════════════
    //  诊断模板构建（封死替代路径 + MTP 扰动提示）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 构建保留设备名拒绝诊断 — 封死替代路径 + 提示 MTP 扰动重生成。
    /// </summary>
    /// <param name="deviceName">检测到的保留设备名</param>
    /// <returns>拒绝诊断</returns>
    private static ToolDiagnostic BuildRetainedDeviceRejectedDiagnostic(string deviceName)
        => ToolDiagnostic.Create(
            "JCC9005",
            $"检测到重定向到 Windows 保留设备名 '{deviceName}' — 在 git bash 中会创建名为 '{deviceName}' 的文件，" +
            $"常规命令难以删除。" +
            $"\n\n✅ 推荐写法：> /dev/null（丢弃输出）或 touch {deviceName}（确需创建文件）" +
            $"\n❌ 禁止尝试：>NUL / >nul. / >con / 先建后删 —— 全部会被拦截" +
            $"\n⚠️ 疑似 MTP 扰动：建议丢弃当前命令，完整重新生成，不要在错误字符串上局部修补",
            "设备名", deviceName,
            "请改用 > /dev/null 丢弃输出，或使用 touch 创建文件。" +
            "禁止尝试 >NUL / >nul. / >con 等变体，全部会被拦截。" +
            "疑似 MTP 扰动时建议完整重新生成命令。");
}
