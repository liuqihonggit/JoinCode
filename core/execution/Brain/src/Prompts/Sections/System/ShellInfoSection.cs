namespace Core.Prompts.Sections;

/// <summary>
/// Shell信息部分 - 关于当前Shell的说明
/// 从 SystemActuatorInfos 通用集合遍历，新增 SystemActuatorKind 无需改此代码
/// </summary>
[PromptSection(Name = "shell_info", Order = 69, IsDynamic = true)]
public static class ShellInfoSection
{
    public static SystemPromptSection Create()
    {
        return SystemPromptSection.Dynamic("shell_info", () =>
        {
            var config = PromptConfigSnapshot.Current;
            var lines = new List<string>();

            var shellInfos = config.ShellInfos;

            if (shellInfos is not null && shellInfos.Count > 0)
            {
                foreach (var kvp in shellInfos)
                {
                    var info = kvp.Value;
                    var line = info.Kind == SystemActuatorKind.Bash && info.Version == "cmd-fallback"
                        ? "Bash: 不可用（未找到 Git Bash，回退到 cmd.exe — 仅支持 CMD 语法）"
                        : info.Kind == SystemActuatorKind.PowerShell
                            ? FormatPowerShellEntry(info)
                            : FormatEntry(info);
                    lines.Add(line);
                }

                if (shellInfos.TryGetValue(SystemActuatorKind.PowerShell, out var psInfo) && !psInfo.DisplayName.Contains("Core"))
                {
                    lines.Add("注意: Windows PowerShell 5.1 不支持 &&、||、三元运算符 ?:、空合并 ?? — 使用 ; if ($?) { } 替代链式命令");
                }
            }
            else
            {
                var shell = Environment.GetEnvironmentVariable("SHELL");
                if (!string.IsNullOrEmpty(shell))
                {
                    var shellName = shell.Contains("zsh") ? "zsh" :
                                    shell.Contains("bash") ? "bash" :
                                    shell.Contains("powershell") ? "powershell" :
                                    shell.Contains("cmd") ? "cmd" : shell;
                    lines.Add($"Shell: {shellName}");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var comspec = Environment.GetEnvironmentVariable("COMSPEC");
                    lines.Add(string.IsNullOrEmpty(comspec)
                        ? "Shell: cmd.exe (Windows 默认)"
                        : $"Shell: {Path.GetFileName(comspec)} (Windows 默认)");
                }
                else
                {
                    lines.Add("Shell: unknown");
                }
            }

            return string.Join(Environment.NewLine, lines);
        });
    }

    private static string FormatEntry(SystemActuatorInfo info)
        => string.IsNullOrEmpty(info.ShellPath)
            ? $"{info.DisplayName}"
            : $"{info.DisplayName} ({info.ShellPath})";

    private static string FormatPowerShellEntry(SystemActuatorInfo info)
        => string.IsNullOrEmpty(info.ShellPath)
            ? $"{info.DisplayName}"
            : $"{info.DisplayName} ({info.ShellPath})";
}
