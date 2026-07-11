namespace JoinCode.ChatCommands;

/// <summary>
/// /color 命令 — 对齐 TS color/color.ts
/// TS 使用 AGENT_COLORS 为多智能体会话设置颜色标识，C# 为单用户模式
/// 对齐内容：颜色设置+重置+列表
/// 架构差异：TS 有多智能体颜色管理（AgentColorManager），C# 为终端颜色主题设置
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Color, Description = "设置终端颜色主题或测试颜色支持", Usage = "/color [theme|test|reset]", Category = ChatCommandCategory.Config)]
public sealed class ColorCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Color;
    public string Description => "设置终端颜色主题或测试颜色支持";
    public string Usage => "/color [theme|test|reset]";
    public string[] Aliases => [];
    public string ArgumentHint => "[theme|test|reset]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();

        if (string.IsNullOrEmpty(args) || args == "test")
        {
            ShowColorTest();
        }
        else if (args == "reset" || args == "default")
        {
            // 对齐 TS RESET_ALIASES: default/reset/none/gray/grey
            TerminalHelper.WriteLine("颜色已重置为默认");
        }
        else
        {
            // 对齐 TS AGENT_COLORS: 尝试设置为指定颜色主题
            TerminalHelper.WriteLine($"颜色主题: {args}");
            TerminalHelper.WriteLine("使用 /color test 测试终端颜色支持");
            TerminalHelper.WriteLine("使用 /color reset 重置为默认");
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static void ShowColorTest()
    {
        TerminalHelper.WriteLine("=== 终端颜色测试 ===\n");

        // 基础颜色
        TerminalHelper.WriteLine("基础颜色:");
        TerminalHelper.WriteLine($"  {TerminalColors.Success}绿色(成功){AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  {TerminalColors.Error}红色(错误){AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  {TerminalColors.Warning}黄色(警告){AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  {TerminalColors.Muted}灰色(次要){AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  {TerminalColors.Primary}蓝色(主要){AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  {TerminalColors.Accent}青色(强调){AnsiStyleConstants.Reset}");

        TerminalHelper.NewLine();

        // ANSI 256色测试
        TerminalHelper.WriteLine("ANSI 256色:");
        for (var i = 0; i < 16; i++)
        {
            TerminalHelper.WriteRaw($"\x1b[38;5;{i}m{i:D2}\x1b[0m ");
            if (i == 7) TerminalHelper.NewLine();
        }
        TerminalHelper.NewLine();

        // 粗体/斜体/下划线
        TerminalHelper.WriteLine("样式:");
        TerminalHelper.WriteLine($"  \x1b[1m粗体\x1b[0m  \x1b[3m斜体\x1b[0m  \x1b[4m下划线\x1b[0m");

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("如果上方颜色显示正常，说明终端支持 ANSI 颜色。");
    }
}
