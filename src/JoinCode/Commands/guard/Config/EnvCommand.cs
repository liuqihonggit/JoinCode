namespace JoinCode.ChatCommands;

/// <summary>
/// /env 命令 — 对齐 TS env/index.js
/// TS 中此命令为 stub（isEnabled: false），C# 实现了完整的环境变量查看
/// 对齐内容：环境变量列表+过滤
/// 架构差异：TS 未实现，C# 扩展实现了过滤和格式化
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Env, Description = "显示环境变量", Usage = "/env [filter]", Category = ChatCommandCategory.Config)]
public sealed class EnvCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Env;
    public string Description => "显示环境变量";
    public string Usage => "/env [filter]";
    public string[] Aliases => [];
    public string ArgumentHint => "[filter]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var filter = ChatCommandBase.GetNormalizedArgs(context);
        var allVars = Environment.GetEnvironmentVariables();

        IEnumerable<System.Collections.DictionaryEntry> filtered;

        if (string.IsNullOrEmpty(filter))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Primary}环境变量 ({allVars.Count}){AnsiStyleConstants.Reset}");
            TerminalHelper.NewLine();
            filtered = allVars.Cast<System.Collections.DictionaryEntry>()
                .OrderBy(e => e.Key.ToString(), StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Primary}环境变量 (过滤: \"{filter}\"){AnsiStyleConstants.Reset}");
            TerminalHelper.NewLine();
            filtered = allVars.Cast<System.Collections.DictionaryEntry>()
                .Where(e => e.Key.ToString()?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
                         || e.Value?.ToString()?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(e => e.Key.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        var shown = 0;
        foreach (var entry in filtered)
        {
            var name = entry.Key.ToString() ?? "";
            var value = entry.Value?.ToString() ?? "";
            if (value.Length > 120)
                value = value[..117] + "...";

            TerminalHelper.WriteLine($"  {TerminalColors.Accent}{name}{AnsiStyleConstants.Reset}={value}");
            shown++;
        }

        if (shown == 0)
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}没有匹配的环境变量{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine($"{TerminalColors.Muted}共 {shown} 个环境变量{AnsiStyleConstants.Reset}");
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }
}