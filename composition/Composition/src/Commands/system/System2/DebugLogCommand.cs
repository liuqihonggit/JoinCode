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

        var content = await RenderSectionsAsync(sectionFlags, context).ConfigureAwait(false);
        TerminalHelper.WriteLine(content);
        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 根据标志位渲染对应的内容 — 委托给 DebugLogRenderer 公共实现
    /// 决策: 复用 DebugLogRenderer，避免与启动流程（InitDebugDumpStep）代码重复
    /// 优先级保持与原实现一致: Error > Warn > Log > Prompt > Init > All
    /// 原逻辑中 Init/Prompt/Log 检查顺序执行且都会 sb.Clear() 覆盖，最终优先级为 Log > Prompt > Init
    /// </summary>
    private static async Task<string> RenderSectionsAsync(DebugSection sectionFlags, ChatCommandContext context)
    {
        var services = context.Services;
        var ct = context.CancellationToken;

        if (sectionFlags.HasFlag(DebugSection.Error))
        {
            return DebugLogRenderer.RenderErrors(services);
        }

        if (sectionFlags.HasFlag(DebugSection.Warn))
        {
            return DebugLogRenderer.RenderWarningsAndErrors(services);
        }

        if (sectionFlags.HasFlag(DebugSection.Log))
        {
            return DebugLogRenderer.RenderLogs(services);
        }

        if (sectionFlags.HasFlag(DebugSection.Prompt))
        {
            return await DebugLogRenderer.RenderSystemPromptAsync(services).ConfigureAwait(false);
        }

        if (sectionFlags.HasFlag(DebugSection.Init))
        {
            return await DebugLogRenderer.RenderInitAsync(services, ct).ConfigureAwait(false);
        }

        return await DebugLogRenderer.RenderAllAsync(services, ct).ConfigureAwait(false);
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
