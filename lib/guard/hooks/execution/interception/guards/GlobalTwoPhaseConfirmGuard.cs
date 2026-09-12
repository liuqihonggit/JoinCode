namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// 全局防丢字符二次确认守卫 — ADR 0012 阶段7
/// <para>
/// 防止 MTP（Multi-Token Prediction）加速推理时丢字符/乱入字符导致命令变形:
/// <list type="bullet">
/// <item>本质是通信可靠性保障，不是权限控制</item>
/// <item>所有命令第一轮返回 <see cref="CommandDecision.Deny"/> 要求再次输入</item>
/// <item>第二轮（context.ConfirmedCommand 匹配）放行</item>
/// <item>通过 <see cref="GuardContext.ConfirmMode"/> = <see cref="GuardConfirmMode.AntiCharLossConfirm"/> 启用</item>
/// <item>通过 <see cref="GuardContext.ConfirmedCommand"/> 传递已确认命令</item>
/// </list>
/// </para>
/// </summary>
[Register(typeof(ICommandGuard), ServiceLifetime.Singleton)]
public sealed partial class GlobalTwoPhaseConfirmGuard : ICommandGuard
{
    /// <inheritdoc/>
    public string Name => "GlobalTwoPhaseConfirmGuard";

    /// <inheritdoc/>
    public int Priority => 900;

    /// <inheritdoc/>
    public bool CanHandle(string command, GuardContext context)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        return IsEnabled(context) && !IsConfirmed(command, context);
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, GuardContext context)
    {
        if (!IsEnabled(context))
            return new CommandDecision.Allow();

        if (IsConfirmed(command, context))
            return new CommandDecision.Allow();

        return new CommandDecision.Deny(
            ToolDiagnostic.Create(
                "JCC9006",
                $"防丢字符二次确认 — MTP 加速推理可能丢字符/乱入字符导致命令变形。" +
                $"请再次输入完全相同的命令以确认执行：\n{command}",
                "命令", command,
                "MTP（Multi-Token Prediction）加速推理时草稿预测/验证环节可能引入字符级错误，" +
                "导致命令字符串被截断或混入乱码。请再次输入同样命令以确认字符串完整无误。"));
    }

    private static bool IsEnabled(GuardContext context) =>
        context.ConfirmMode == GuardConfirmMode.AntiCharLossConfirm;

    private static bool IsConfirmed(string command, GuardContext context) =>
        context.ConfirmedCommand == command;
}
