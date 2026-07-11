using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// Shell信息部分 - 关于当前Shell的说明
/// </summary>
[PromptSection(Name = "shell_info", Order = 69, IsDynamic = true)]
public static class ShellInfoSection
{
    public static SystemPromptSection Create()
    {
        return SystemPromptSection.Dynamic("shell_info", () =>
        {
            var shell = Environment.GetEnvironmentVariable("SHELL") ?? "unknown";
            var shellName = shell.Contains("zsh") ? "zsh" :
                           shell.Contains("bash") ? "bash" :
                           shell.Contains("powershell") ? "powershell" :
                           shell.Contains("cmd") ? "cmd" : shell;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return $"""
                    Shell: {shellName} (在Windows上使用PowerShell语法)
                    """;
            }

            return $"""
                Shell: {shellName}
                """;
        });
    }
}
