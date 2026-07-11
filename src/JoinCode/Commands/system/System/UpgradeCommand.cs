
namespace JoinCode.ChatCommands;

/// <summary>
/// /upgrade 命令 — 对齐 TS upgrade.ts
/// TS 使用 autoUpdater + npm/native 安装模式，C# 使用 IUpgradeService
/// 对齐内容：check+force 版本检查
/// 架构差异：TS 有自动下载安装，C# 为手动下载（NativeAOT 单文件发布）
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Upgrade, Description = "检查并执行自升级", Usage = "/upgrade [check|force]", Category = ChatCommandCategory.System)]
public sealed class UpgradeCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Upgrade;
    public string Description => "检查并执行自升级";
    public string Usage => "/upgrade [check|force]";
    public string[] Aliases => [];
    public string ArgumentHint => "[check|force]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var upgradeService = context.Services!.ServiceProvider?.GetService<IUpgradeService>();
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();

        var currentVersion = upgradeService?.GetCurrentVersion() ?? GetFallbackVersion();
        TerminalHelper.WriteLine($"当前版本: {currentVersion}");

        if (upgradeService is null)
        {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine("升级服务未初始化");
            }
            TerminalHelper.WriteLine("请手动访问 GitHub Releases 获取最新版本");
            return ChatCommandResult.Continue();
        }

        if (args is "check" or "")
        {
            TerminalHelper.WriteLine("正在检查更新...");
            try
            {
                var latest = await upgradeService.GetLatestVersionAsync(context.CancellationToken).ConfigureAwait(false);
                if (latest is not null)
                {
                    TerminalHelper.WriteLine($"最新版本: {latest}");
                    if (latest > currentVersion)
                    {
                        TerminalHelper.WriteLine("有新版本可用!");
                        // 对齐 TS: 升级确认框
                        var confirmed = await Confirmation.ConfirmAsync($"是否下载更新 {currentVersion} → {latest}？", context.CancellationToken).ConfigureAwait(false);
                        if (confirmed)
                        {
                            TerminalHelper.WriteLine("请访问 GitHub Releases 下载更新");
                        }
                    }
                    else
                    {
                        TerminalHelper.WriteLine("已是最新版本");
                    }
                }
                else
                {
                    TerminalHelper.WriteLine("无法获取最新版本信息");
                    TerminalHelper.WriteLine("请手动访问 GitHub Releases 获取最新版本");
                }
            }
            catch (Exception ex)
            {
                ChatCommandBase.HandleError("检查更新", ex);
            }
        }
        else if (args is "force")
        {
            TerminalHelper.WriteLine("正在检查更新...");
            try
            {
                var isAvailable = await upgradeService.IsUpdateAvailableAsync(context.CancellationToken).ConfigureAwait(false);
                if (isAvailable)
                {
                    TerminalHelper.WriteLine("有新版本可用，请手动下载更新");
                }
                else
                {
                    TerminalHelper.WriteLine("已是最新版本");
                }
            }
            catch (Exception ex)
            {
                ChatCommandBase.HandleError("检查更新", ex);
            }
        }

        return ChatCommandResult.Continue();
    }

    private static Version GetFallbackVersion()
    {
        return typeof(UpgradeCommand).Assembly.GetName().Version ?? new Version(0, 1, 0);
    }
}
