
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.ResetConfig, Description = "重置配置文件到默认状态", Usage = "/reset-config [all|auth|settings|trust|onboarding]", Category = ChatCommandCategory.Config)]
public sealed class ResetConfigCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.ResetConfig;
    public string Description => "重置配置文件到默认状态";
    public string Usage => "/reset-config [all|auth|settings|trust|onboarding]";
    public string[] Aliases => [];
    public string ArgumentHint => "[all|auth|settings|trust|onboarding]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();
        var fs = context.Services.FileSystem;
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var jccDir = Path.Combine(appDataRoot, AppDataConstants.AppDataFolder);

        if (args == "all" || string.IsNullOrEmpty(args))
        {
            await ResetAllAsync(jccDir, fs, context.CancellationToken);
        }
        else
        {
            switch (args)
            {
                case "auth":
                    await ResetAuthAsync(jccDir, fs, context.CancellationToken);
                    break;
                case "settings":
                    await ResetSettingsAsync(jccDir, fs, context.CancellationToken);
                    break;
                case "trust":
                    await ResetTrustAsync(jccDir, fs, context.CancellationToken);
                    break;
                case "onboarding":
                    await ResetOnboardingAsync(jccDir, fs, context.CancellationToken);
                    break;
                default:
                    TerminalHelper.WriteLine($"未知选项: {args}");
                    TerminalHelper.WriteLine("可用选项: all, auth, settings, trust, onboarding");
                    break;
            }
        }

        return ChatCommandResult.Continue();
    }

    private static async Task ResetAllAsync(string jccDir, IFileSystem fs, CancellationToken ct)
    {
        TerminalHelper.WriteLine("重置所有配置文件...");
        TerminalHelper.NewLine();

        await ResetAuthAsync(jccDir, fs, ct);
        await ResetSettingsAsync(jccDir, fs, ct);
        await ResetTrustAsync(jccDir, fs, ct);
        await ResetOnboardingAsync(jccDir, fs, ct);

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Success}✓ 所有配置已重置{AnsiStyleConstants.Reset}");
    }

    private static async Task ResetAuthAsync(string jccDir, IFileSystem fs, CancellationToken ct)
    {
        var authFile = Path.Combine(jccDir, AppDataConstants.AuthFileName);
        if (fs.FileExists(authFile))
        {
            await fs.WriteAllTextAsync(authFile, "{}", ct).ConfigureAwait(false);
            TerminalHelper.WriteLine("  ✓ auth.json 已清空");
        }
        else
        {
            TerminalHelper.WriteLine("  · auth.json 不存在");
        }
    }

    private static async Task ResetSettingsAsync(string jccDir, IFileSystem fs, CancellationToken ct)
    {
        var settingsFile = Path.Combine(jccDir, AppDataConstants.SettingsFileName);
        if (fs.FileExists(settingsFile))
        {
            await fs.WriteAllTextAsync(settingsFile, "{}", ct).ConfigureAwait(false);
            TerminalHelper.WriteLine("  ✓ settings.json 已重置");
        }
        else
        {
            TerminalHelper.WriteLine("  · settings.json 不存在");
        }
    }

    private static async Task ResetTrustAsync(string jccDir, IFileSystem fs, CancellationToken ct)
    {
        var trustFile = Path.Combine(jccDir, AppDataConstants.TrustedFoldersFileName);
        if (fs.FileExists(trustFile))
        {
            await fs.WriteAllTextAsync(trustFile, "{\"folders\":[]}", ct).ConfigureAwait(false);
            TerminalHelper.WriteLine("  ✓ trusted_folders.json 已清空");
        }
        else
        {
            TerminalHelper.WriteLine("  · trusted_folders.json 不存在");
        }
    }

    private static async Task ResetOnboardingAsync(string jccDir, IFileSystem fs, CancellationToken ct)
    {
        var onboardingFile = Path.Combine(jccDir, "onboarding_complete.json");
        if (fs.FileExists(onboardingFile))
        {
            fs.DeleteFile(onboardingFile);
            TerminalHelper.WriteLine("  ✓ onboarding_complete.json 已删除");
        }
        else
        {
            TerminalHelper.WriteLine("  · onboarding_complete.json 不存在");
        }
    }
}
