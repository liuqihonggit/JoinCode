namespace JoinCode.ChatCommands;

/// <summary>
/// /debuglog 子选项标志枚举 — [EnumValue] 由 EnumMetadataGenerator 自动生成 DebugLogFlagConstants + DebugLogFlagExtensions
/// </summary>
public enum DebugLogFlag
{
    [EnumValue("--error")]
    [EnumValue("-e")]
    Error,
    [EnumValue("--warn")]
    [EnumValue("-w")]
    Warn,
    [EnumValue("--init")]
    [EnumValue("-i")]
    Init,
    [EnumValue("--prompt")]
    [EnumValue("-p")]
    Prompt,
    [EnumValue("--log")]
    [EnumValue("-l")]
    Log,
    [EnumValue("--all")]
    [EnumValue("-a")]
    All,
    [EnumValue("--clear")]
    [EnumValue("-c")]
    Clear,
}

/// <summary>
/// /debuglog 命令 — 显示运行流程全貌：初始化状态、警告、错误、日志、系统提示词
/// /debuglog          全部信息（等同 --all）
/// /debuglog -a       全部信息
/// /debuglog -e       仅错误
/// /debuglog -w       仅警告+错误
/// /debuglog -i       仅初始化信息
/// /debuglog -p       仅系统提示词
/// /debuglog -l       仅诊断日志
/// /debuglog -c       清空日志缓冲区
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.DebugLog, Description = "显示运行流程全貌（初始化|警告|错误|日志|系统提示词）",
    Usage = "/debuglog [-a|-e|-w|-i|-p|-l|-c]",
    Category = ChatCommandCategory.System, ArgumentHint = "[-a|--all|-e|--error|-w|--warn|-i|--init|-p|--prompt|-l|--log|-c|--clear]")]
public sealed class DebugLogCommand : ChatCommandBase
{
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = GetSplitArgs(context);
        var (sectionFlags, clear) = ParseFlags(args);

        if (clear)
        {
            var buffer = GetService<IDebugLogBuffer>(context);
            if (buffer is not null)
            {
                buffer.Clear();
                TerminalHelper.WriteLine($"{TerminalColors.Success}日志缓冲区已清空{AnsiStyleConstants.Reset}");
            }
            return ChatCommandResult.Continue();
        }

        var sb = new StringBuilder();

        if (sectionFlags.HasFlag(DebugSection.Error))
        {
            AppendErrors(sb, context);
        }
        else if (sectionFlags.HasFlag(DebugSection.Warn))
        {
            AppendWarningsAndErrors(sb, context);
        }
        else
        {
            await AppendInitInfo(sb, context);
            AppendWarningsAndErrors(sb, context);
            AppendLogs(sb, context);
            await AppendSystemPrompt(sb, context);
        }

        if (sectionFlags.HasFlag(DebugSection.Init) && !sectionFlags.HasFlag(DebugSection.Error) && !sectionFlags.HasFlag(DebugSection.Warn))
        {
            sb.Clear();
            await AppendInitInfo(sb, context);
        }

        if (sectionFlags.HasFlag(DebugSection.Prompt) && !sectionFlags.HasFlag(DebugSection.Error) && !sectionFlags.HasFlag(DebugSection.Warn))
        {
            sb.Clear();
            await AppendSystemPrompt(sb, context);
        }

        if (sectionFlags.HasFlag(DebugSection.Log) && !sectionFlags.HasFlag(DebugSection.Error) && !sectionFlags.HasFlag(DebugSection.Warn))
        {
            sb.Clear();
            AppendLogs(sb, context);
        }

        TerminalHelper.WriteLine(sb.ToString());
        return ChatCommandResult.Continue();
    }

    private static async Task AppendInitInfo(StringBuilder sb, ChatCommandContext context)
    {
        sb.AppendLine($"{TerminalColors.Accent}═══ 初始化状态 ═══{AnsiStyleConstants.Reset}");

        var debugLogStatus = Diag.IsDebugLog
            ? $"{TerminalColors.Success}已启用{AnsiStyleConstants.Reset}"
            : $"{TerminalColors.Muted}未启用{AnsiStyleConstants.Reset}";
        sb.AppendLine($"  调试日志: {debugLogStatus}");

        var envVars = new[]
        {
            JccEnvVar.DebugLog, JccEnvVar.DiTrace, JccEnvVar.DumpMessages, JccEnvVar.LogLevel,
        };

        sb.AppendLine("  环境变量:");
        foreach (var envVar in envVars)
        {
            var value = Environment.GetEnvironmentVariable(envVar.ToValue());
            var display = value is not null
                ? $"{TerminalColors.Warning}{envVar.ToValue()}={value}{AnsiStyleConstants.Reset}"
                : $"{TerminalColors.Muted}{envVar.ToValue()}=(未设置){AnsiStyleConstants.Reset}";
            sb.AppendLine($"    {display}");
        }

        var crashStore = context.Services.GetService<ICrashSnapshotStore>();
        if (crashStore is not null)
        {
            sb.AppendLine($"  崩溃快照: {TerminalColors.Muted}{crashStore.TotalCount} 条记录, {crashStore.UnacknowledgedCount} 条未确认{AnsiStyleConstants.Reset}");
        }

        var debugBuffer = context.Services.GetService<IDebugLogBuffer>();
        if (debugBuffer is not null)
        {
            sb.AppendLine($"  日志缓冲区: {TerminalColors.Muted}{debugBuffer.Count} 条{AnsiStyleConstants.Reset}");
        }

        var toolRegistry = context.Services.GetService<IToolRegistry>();
        if (toolRegistry is not null)
        {
            var toolCount = await toolRegistry.GetCountAsync(context.CancellationToken).ConfigureAwait(false);
            sb.AppendLine($"  MCP 工具: {TerminalColors.Muted}{toolCount} 个已注册{AnsiStyleConstants.Reset}");
        }

        var promptProvider = context.Services.GetService<ISystemPromptProvider>();
        if (promptProvider is not null)
        {
            var sections = promptProvider.GetSections().ToList();
            sb.AppendLine($"  系统提示词: {TerminalColors.Muted}{sections.Count} 个部分{AnsiStyleConstants.Reset}");
            foreach (var section in sections)
            {
                var cacheTag = section.CacheBreak ? "动态" : "缓存";
                sb.AppendLine($"    {TerminalColors.Muted}[{cacheTag}]{AnsiStyleConstants.Reset} {section.Name}");
            }
        }

        sb.AppendLine();
    }

    private static void AppendWarningsAndErrors(StringBuilder sb, ChatCommandContext context)
    {
        var crashStore = context.Services.GetService<ICrashSnapshotStore>();
        var debugBuffer = context.Services.GetService<IDebugLogBuffer>();

        sb.AppendLine($"{TerminalColors.Accent}═══ 警告与错误 ═══{AnsiStyleConstants.Reset}");

        if (crashStore is not null)
        {
            var warnings = crashStore.GetRecent(50).Where(s => s.Severity == CrashSeverity.Warning).ToList();
            var errors = crashStore.GetRecent(50).Where(s => s.Severity is CrashSeverity.Error or CrashSeverity.Fatal).ToList();

            if (warnings.Count > 0)
            {
                sb.AppendLine($"  {TerminalColors.Warning}警告 ({warnings.Count}):{AnsiStyleConstants.Reset}");
                foreach (var w in warnings.Take(20))
                {
                    sb.AppendLine($"    [{w.Severity.ToValue()}] {w.FenceName}: {w.ExceptionType}: {w.ExceptionMessage}");
                }
            }

            if (errors.Count > 0)
            {
                sb.AppendLine($"  {TerminalColors.Error}错误 ({errors.Count}):{AnsiStyleConstants.Reset}");
                foreach (var e in errors.Take(20))
                {
                    sb.AppendLine($"    [{e.Severity.ToValue()}] {e.FenceName}: {e.ExceptionType}: {e.ExceptionMessage}");
                    if (e.ExecutionContext.ToolName is not null)
                        sb.AppendLine($"      工具: {e.ExecutionContext.ToolName}  轮次: {e.ExecutionContext.TurnIndex}");
                    sb.AppendLine($"      时间: {e.CapturedAt:HH:mm:ss.fff}  ID: {e.Id:N}");
                }
            }

            if (warnings.Count == 0 && errors.Count == 0)
            {
                sb.AppendLine($"  {TerminalColors.Success}无警告或错误{AnsiStyleConstants.Reset}");
            }
        }
        else
        {
            sb.AppendLine($"  {TerminalColors.Muted}CrashSnapshotStore 不可用{AnsiStyleConstants.Reset}");
        }

        if (debugBuffer is not null)
        {
            var errorLogs = debugBuffer.GetByLevel(DebugLogLevel.Error, 30);
            if (errorLogs.Count > 0)
            {
                sb.AppendLine($"  {TerminalColors.Error}诊断错误日志 ({errorLogs.Count}):{AnsiStyleConstants.Reset}");
                foreach (var entry in errorLogs)
                {
                    sb.AppendLine($"    {entry.Timestamp:HH:mm:ss.fff} {entry.Message}");
                }
            }
        }

        sb.AppendLine();
    }

    private static void AppendErrors(StringBuilder sb, ChatCommandContext context)
    {
        var crashStore = context.Services.GetService<ICrashSnapshotStore>();
        var debugBuffer = context.Services.GetService<IDebugLogBuffer>();

        sb.AppendLine($"{TerminalColors.Error}═══ 错误 ═══{AnsiStyleConstants.Reset}");

        var hasErrors = false;

        if (crashStore is not null)
        {
            var errors = crashStore.GetRecent(50).Where(s => s.Severity is CrashSeverity.Error or CrashSeverity.Fatal).ToList();
            if (errors.Count > 0)
            {
                hasErrors = true;
                foreach (var e in errors)
                {
                    sb.AppendLine($"  [{e.Severity.ToValue()}] {e.FenceName}");
                    sb.AppendLine($"    {e.ExceptionType}: {e.ExceptionMessage}");
                    if (e.ErrorCode is not null)
                        sb.AppendLine($"    错误码: {e.ErrorCode}");
                    if (e.ExecutionContext.ToolName is not null)
                        sb.AppendLine($"    工具: {e.ExecutionContext.ToolName}  轮次: {e.ExecutionContext.TurnIndex}");
                    sb.AppendLine($"    时间: {e.CapturedAt:HH:mm:ss.fff}  ID: {e.Id:N}");
                    sb.AppendLine();
                }
            }
        }

        if (debugBuffer is not null)
        {
            var errorLogs = debugBuffer.GetByLevel(DebugLogLevel.Error, 50);
            if (errorLogs.Count > 0)
            {
                hasErrors = true;
                sb.AppendLine("  诊断错误日志:");
                foreach (var entry in errorLogs)
                {
                    sb.AppendLine($"    {entry.Timestamp:HH:mm:ss.fff} {entry.Message}");
                }
            }
        }

        if (!hasErrors)
        {
            sb.AppendLine($"  {TerminalColors.Success}无错误记录{AnsiStyleConstants.Reset}");
        }

        sb.AppendLine();
    }

    private static void AppendLogs(StringBuilder sb, ChatCommandContext context)
    {
        var debugBuffer = context.Services.GetService<IDebugLogBuffer>();

        sb.AppendLine($"{TerminalColors.Accent}═══ 诊断日志 ═══{AnsiStyleConstants.Reset}");

        if (debugBuffer is null)
        {
            sb.AppendLine($"  {TerminalColors.Muted}DebugLogBuffer 不可用{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var entries = debugBuffer.GetRecent(100);
        if (entries.Count == 0)
        {
            sb.AppendLine($"  {TerminalColors.Muted}无日志记录（启用 --verbose 可捕获更多日志）{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var grouped = entries.GroupBy(e => e.Category).OrderByDescending(g => g.Count()).ToList();
        sb.AppendLine($"  共 {debugBuffer.Count} 条日志，显示最近 {entries.Count} 条");
        sb.AppendLine($"  分类统计: {string.Join(", ", grouped.Select(g => $"{g.Key}={g.Count()}"))}");
        sb.AppendLine();

        foreach (var entry in entries.Take(80))
        {
            var levelColor = entry.Level switch
            {
                DebugLogLevel.Error => TerminalColors.Error,
                DebugLogLevel.Warn => TerminalColors.Warning,
                _ => TerminalColors.Muted,
            };
            sb.AppendLine($"  {levelColor}{entry.Timestamp:HH:mm:ss.fff} [{entry.Category}]{AnsiStyleConstants.Reset} {Truncate(entry.Message, 200)}");
        }

        if (entries.Count > 80)
            sb.AppendLine($"  ... 还有 {entries.Count - 80} 条");

        sb.AppendLine();
    }

    private static async Task AppendSystemPrompt(StringBuilder sb, ChatCommandContext context)
    {
        var promptProvider = context.Services.GetService<ISystemPromptProvider>();

        sb.AppendLine($"{TerminalColors.Accent}═══ 系统提示词 ═══{AnsiStyleConstants.Reset}");

        if (promptProvider is null)
        {
            sb.AppendLine($"  {TerminalColors.Muted}ISystemPromptProvider 不可用{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var sections = promptProvider.GetSections().ToList();
        if (sections.Count == 0)
        {
            sb.AppendLine($"  {TerminalColors.Muted}无系统提示词部分{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var totalLength = 0;
        foreach (var section in sections)
        {
            var cacheTag = section.CacheBreak ? "动态" : "缓存";
            sb.AppendLine($"  {TerminalColors.Primary}[{cacheTag}] {section.Name}{AnsiStyleConstants.Reset}");

            try
            {
                var content = await section.ComputeValueTaskAsync().ConfigureAwait(false);
                if (content is not null)
                {
                    totalLength += content.Length;
                    var display = content.Length > 500
                        ? content[..500] + $"... (共 {content.Length} 字符)"
                        : content;
                    sb.AppendLine($"    {TerminalColors.Muted}{display}{AnsiStyleConstants.Reset}");
                }
                else
                {
                    sb.AppendLine($"    {TerminalColors.Muted}(空){AnsiStyleConstants.Reset}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    {TerminalColors.Error}计算失败: {ex.Message}{AnsiStyleConstants.Reset}");
            }

            sb.AppendLine();
        }

        sb.AppendLine($"  总计: {sections.Count} 个部分, {totalLength:N0} 字符");
        sb.AppendLine();
    }

    private static (DebugSection Flags, bool Clear) ParseFlags(string[] args)
    {
        var flags = DebugSection.None;
        var clear = false;

        foreach (var arg in args)
        {
            var flag = DebugLogFlagExtensions.FromValue(arg);
            switch (flag)
            {
                case DebugLogFlag.Error: flags |= DebugSection.Error; break;
                case DebugLogFlag.Warn: flags |= DebugSection.Warn; break;
                case DebugLogFlag.Init: flags |= DebugSection.Init; break;
                case DebugLogFlag.Prompt: flags |= DebugSection.Prompt; break;
                case DebugLogFlag.Log: flags |= DebugSection.Log; break;
                case DebugLogFlag.All: flags = DebugSection.All; break;
                case DebugLogFlag.Clear: clear = true; break;
            }
        }

        if (flags == DebugSection.None && !clear)
            flags = DebugSection.All;

        return (flags, clear);
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    [Flags]
    private enum DebugSection
    {
        None = 0,
        Init = 1,
        Error = 2,
        Warn = 4,
        Log = 8,
        Prompt = 16,
        All = Init | Error | Warn | Log | Prompt,
    }
}
