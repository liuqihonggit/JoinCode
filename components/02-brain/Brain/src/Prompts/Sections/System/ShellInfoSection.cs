namespace Core.Prompts.Sections;

/// <summary>
/// Shell信息部分 - 关于当前Shell的说明
/// 从 IShellProvider.Version 检测结果注入，让 LLM 知道 Shell 版本以生成正确的命令语法
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

            var bashVer = config.BashVersion;
            var psVer = config.PowerShellVersion;
            var psEdition = config.PowerShellEdition;

            if (!string.IsNullOrEmpty(bashVer) && bashVer != "cmd-fallback")
            {
                lines.Add($"Bash: {bashVer}");
            }
            else if (bashVer == "cmd-fallback")
            {
                lines.Add("Bash: 不可用（未找到 Git Bash，回退到 cmd.exe — 仅支持 CMD 语法）");
            }

            if (!string.IsNullOrEmpty(psVer))
            {
                var editionLabel = psEdition?.ToLower() switch
                {
                    "core" => "PowerShell Core 7+",
                    "desktop" => "Windows PowerShell 5.1",
                    _ => "PowerShell"
                };
                lines.Add($"{editionLabel}: {psVer}");
            }

            if (lines.Count == 0)
            {
                var shell = Environment.GetEnvironmentVariable("SHELL") ?? "unknown";
                var shellName = shell.Contains("zsh") ? "zsh" :
                                shell.Contains("bash") ? "bash" :
                                shell.Contains("powershell") ? "powershell" :
                                shell.Contains("cmd") ? "cmd" : shell;
                lines.Add($"Shell: {shellName}");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (psEdition?.ToLower() == "desktop")
                {
                    lines.Add("注意: Windows PowerShell 5.1 不支持 &&、||、三元运算符 ?:、空合并 ?? — 使用 ; if ($?) { } 替代链式命令");
                }
            }

            return string.Join(Environment.NewLine, lines);
        });
    }
}
