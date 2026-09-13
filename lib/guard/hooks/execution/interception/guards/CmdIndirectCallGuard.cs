namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// cmd /c 和 powershell -Command 间接调用递归分类守卫 — ADR 0012 阶段4
/// <para>
/// 拦截通过 cmd /c、cmd /k、powershell -Command、pwsh -Command 间接调用危险命令:
/// <list type="bullet">
/// <item>提取内层命令字符串（处理引号、转义）</item>
/// <item>对内层命令递归分类</item>
/// <item>Dangerous → <see cref="CommandDecision.Deny"/>（硬拒绝，防止绕过）</item>
/// <item>Execution → <see cref="CommandDecision.Deny"/>（硬拒绝，要求直接执行内层命令）</item>
/// <item>Safe/其他 → <see cref="CommandDecision.Allow"/>（放行）</item>
/// </list>
/// </para>
/// </summary>
[Register(typeof(ICommandGuard), ServiceLifetime.Singleton)]
public sealed partial class CmdIndirectCallGuard : ICommandGuard
{
    private readonly ICommandDangerClassifier _classifier;

    /// <summary>
    /// 构造间接调用守卫 — DI 注入分类器
    /// </summary>
    /// <param name="classifier">命令危险分类器（用于递归分类内层命令）</param>
    public CmdIndirectCallGuard(ICommandDangerClassifier classifier)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    /// <inheritdoc/>
    public string Name => "CmdIndirectCallGuard";

    /// <inheritdoc/>
    public int Priority => 800;

    /// <inheritdoc/>
    public bool CanHandle(string command, GuardContext context)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        return ExtractInnerCommand(command) is not null;
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, GuardContext context)
    {
        var innerCommand = ExtractInnerCommand(command);
        if (innerCommand is null)
            return new CommandDecision.Allow();

        var innerLevel = _classifier.Classify(innerCommand);

        if (innerLevel.Level == CommandDangerLevel.Dangerous)
        {
            return new CommandDecision.Deny(
                ToolDiagnostic.Create(
                    "JCC9003",
                    $"cmd /c 间接调用危险命令被拦截 — 内层命令 '{innerCommand}' 分类为 Dangerous（黑灯直接拒绝）",
                    "建议", "请直接执行内层命令（不通过 cmd /c 间接调用），以便命令拦截系统正确分类"));
        }

        if (innerLevel.Level == CommandDangerLevel.Execution)
        {
            return new CommandDecision.Deny(
                ToolDiagnostic.Create(
                    "JCC9004",
                    $"cmd /c 间接调用不可撤回命令被拦截 — 内层命令 '{innerCommand}' 分类为 Execution（红灯不可撤回）",
                    "建议", "请直接执行内层命令（不通过 cmd /c 间接调用），以便命令拦截系统正确分类和确认"));
        }

        return new CommandDecision.Allow();
    }

    /// <summary>
    /// 提取间接调用的内层命令字符串 — 供诊断和测试使用
    /// </summary>
    /// <param name="command">完整命令字符串</param>
    /// <returns>内层命令字符串，非间接调用返回 null</returns>
    public static string? ExtractInnerCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var span = command.AsSpan().TrimStart();

        if (TryExtractCmdInner(span, out var cmdInner))
            return cmdInner;

        if (TryExtractPwshInner(span, out var pwshInner))
            return pwshInner;

        return null;
    }

    private static bool TryExtractCmdInner(ReadOnlySpan<char> span, out string? inner)
    {
        inner = null;

        if (!span.StartsWith("cmd", StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = span.Slice(3).TrimStart();
        if (rest.IsEmpty)
            return false;

        if (!rest.StartsWith("/c", StringComparison.OrdinalIgnoreCase) &&
            !rest.StartsWith("/k", StringComparison.OrdinalIgnoreCase))
            return false;

        rest = rest.Slice(2).TrimStart();
        if (rest.IsEmpty)
            return false;

        inner = ExtractQuotedOrRaw(rest);
        return true;
    }

    private static bool TryExtractPwshInner(ReadOnlySpan<char> span, out string? inner)
    {
        inner = null;

        var isPwsh = span.StartsWith("powershell", StringComparison.OrdinalIgnoreCase) ||
                     span.StartsWith("pwsh", StringComparison.OrdinalIgnoreCase);
        if (!isPwsh)
            return false;

        var cmdLen = span.StartsWith("powershell", StringComparison.OrdinalIgnoreCase) ? 10 : 4;
        var rest = span.Slice(cmdLen).TrimStart();
        if (rest.IsEmpty)
            return false;

        if (!rest.StartsWith("-Command", StringComparison.OrdinalIgnoreCase) &&
            !rest.StartsWith("-command", StringComparison.OrdinalIgnoreCase))
            return false;

        rest = rest.Slice(8).TrimStart();
        if (rest.IsEmpty)
            return false;

        inner = ExtractQuotedOrRaw(rest);
        return true;
    }

    private static string ExtractQuotedOrRaw(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return string.Empty;

        if (span[0] == '"')
        {
            var end = span.Slice(1).IndexOf('"');
            return end >= 0 ? span.Slice(1, end).ToString() : span.Slice(1).ToString();
        }

        return span.ToString();
    }
}
