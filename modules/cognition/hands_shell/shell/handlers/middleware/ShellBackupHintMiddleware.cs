namespace Tools.Shell;

/// <summary>
/// Shell 备份提示中间件 — 检测破坏性命令时注入"建议先备份"系统提示（GAP-039-03）
/// <para>不阻止执行：检测到破坏性命令 → 通过 SystemReminderManager 注入一次性备份提示 → 继续管道</para>
/// <para>冷却去重：CooldownService 5 分钟内不重复注入，避免频繁打扰</para>
/// <para>检测委托给 DestructiveCommandAnalyzer（统一数据源），额外覆盖 Move-Item -Force 和 &gt; 重定向覆盖</para>
/// </summary>
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellBackupHintMiddleware : ServiceEntity, IShellMiddleware {
    internal const string CooldownKeyConst = "bash-destructive-backup-hint";
    internal const string ReminderIdConst = "destructive-backup-hint";

    private readonly ISystemReminderManager? _reminderManager;

    /// <summary>
    /// 构造 Shell 备份提示中间件
    /// </summary>
    /// <param name="reminderManager">系统提醒管理器（可选，为 null 时跳过提示注入但仍放行管道）</param>
    public ShellBackupHintMiddleware(ISystemReminderManager? reminderManager = null) {
        _reminderManager = reminderManager;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct) {
        if (IsDestructiveCommand(context.Command)
            && _reminderManager is not null
            && CooldownService.ShouldTrigger(CooldownKeyConst)) {
            CooldownService.RecordTrigger(CooldownKeyConst);
            var hint = BuildBackupHint(context.Command);
            await _reminderManager.AddReminderAsync(ReminderIdConst, hint, priority: 0, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 检测命令是否含破坏性动作 — 委托 DestructiveCommandAnalyzer + 额外覆盖 Move-Item -Force 和 &gt; 重定向覆盖
    /// </summary>
    internal static bool IsDestructiveCommand(string command) {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        if (DestructiveCommandAnalyzer.IsDangerous(command))
            return true;

        var lower = command.ToLowerInvariant();

        if (lower.Contains("move-item", StringComparison.Ordinal) && lower.Contains("-force", StringComparison.Ordinal))
            return true;

        return IsRedirectOverwrite(command, lower);
    }

    /// <summary>
    /// 检测 &gt; 重定向覆盖文件（排除 &gt;&gt; 追加、2&gt; stderr、&gt; /dev/ 设备、&gt;nul 空设备）
    /// </summary>
    private static bool IsRedirectOverwrite(string command, string lower) {
        for (var i = 0; i < command.Length; i++) {
            if (command[i] != '>')
                continue;
            var prev = i > 0 ? command[i - 1] : ' ';
            if (prev == '>' || prev == '2' || prev == '&')
                continue;
            var nextIdx = i + 1;
            if (nextIdx < command.Length && command[nextIdx] == '>')
                continue;
            var afterSpace = nextIdx;
            while (afterSpace < command.Length && char.IsWhiteSpace(command[afterSpace]))
                afterSpace++;
            if (afterSpace >= command.Length)
                continue;
            var remaining = lower.AsSpan(afterSpace);
            if (remaining.StartsWith("/dev/", StringComparison.Ordinal))
                continue;
            if (remaining.StartsWith("nul", StringComparison.Ordinal))
                continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 构建备份提示文案 — 引导 AI 先 git stash 备份再执行破坏性命令
    /// </summary>
    internal static string BuildBackupHint(string command) {
        var sb = new StringBuilder();
        sb.AppendLine("检测到破坏性命令，建议先备份再执行:");
        sb.AppendLine($"  命令: {command}");
        sb.AppendLine();
        sb.AppendLine("备份方式（按优先级）:");
        sb.AppendLine("  1. git stash        — 临时保存未提交的改动");
        sb.AppendLine("  2. git add -A && git commit -m \"backup before destructive op\"");
        sb.AppendLine("  3. 复制文件到 .xxx/ 目录归档");
        sb.Append("执行破坏性命令前请确认已备份重要文件。");
        return sb.ToString();
    }
}
