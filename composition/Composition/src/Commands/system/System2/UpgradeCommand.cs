namespace JoinCode.ChatCommands;

/// <summary>
/// /upgrade 命令 — 对齐 TS upgrade.ts + ADR 0064 自动更新
/// 支持参数: check(默认) / force / download / apply / auto
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Upgrade, Description = "检查并执行自升级", Usage = "/upgrade [check|force|download|apply|auto]", Category = ChatCommandCategory.System, ArgumentHint = "[check|force|download|apply|auto]")]
public sealed class UpgradeCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var upgradeService = context.Services?.GetService<IUpgradeService>();
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

        switch (args)
        {
            case "":
            case "check":
                await CheckUpdateAsync(upgradeService, currentVersion, context.CancellationToken).ConfigureAwait(false);
                break;
            case "force":
                await ForceCheckAsync(upgradeService, context.CancellationToken).ConfigureAwait(false);
                break;
            case "download":
                await DownloadUpdateAsync(upgradeService, currentVersion, context.CancellationToken).ConfigureAwait(false);
                break;
            case "apply":
                await ApplyUpdateAsync(upgradeService, currentVersion, context.CancellationToken).ConfigureAwait(false);
                break;
            case "auto":
                await AutoUpdateAsync(upgradeService, currentVersion, context.CancellationToken).ConfigureAwait(false);
                break;
            default:
                TerminalHelper.WriteLine($"未知参数: {args}");
                TerminalHelper.WriteLine("用法: /upgrade [check|force|download|apply|auto]");
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task CheckUpdateAsync(IUpgradeService service, Version currentVersion, CancellationToken ct)
    {
        TerminalHelper.WriteLine("正在检查更新...");
        try
        {
            var latest = await service.GetLatestVersionAsync(ct).ConfigureAwait(false);
            if (latest is not null)
            {
                TerminalHelper.WriteLine($"最新版本: {latest}");
                if (latest > currentVersion)
                {
                    TerminalHelper.WriteLine("有新版本可用! 使用 /upgrade download 下载，/upgrade apply 安装");
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

    private static async Task ForceCheckAsync(IUpgradeService service, CancellationToken ct)
    {
        TerminalHelper.WriteLine("正在强制检查更新...");
        try
        {
            var isAvailable = await service.IsUpdateAvailableAsync(ct).ConfigureAwait(false);
            TerminalHelper.WriteLine(isAvailable ? "有新版本可用" : "已是最新版本");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("检查更新", ex);
        }
    }

    private static async Task DownloadUpdateAsync(IUpgradeService service, Version currentVersion, CancellationToken ct)
    {
        TerminalHelper.WriteLine("正在获取更新信息...");
        try
        {
            var entry = await service.GetUpdateEntryAsync(ct).ConfigureAwait(false);
            if (entry is null)
            {
                TerminalHelper.WriteLine("无可用更新或未配置更新源");
                return;
            }

            TerminalHelper.WriteLine($"发现更新: {currentVersion} → {entry.Version} ({entry.SizeBytes / 1024 / 1024.0:F1} MB)");
            TerminalHelper.WriteLine("正在下载...");

            var result = await service.DownloadUpdateAsync(entry, null, ct).ConfigureAwait(false);
            if (result.Success)
            {
                TerminalHelper.WriteLine($"下载完成: {result.DownloadedPath}");
                TerminalHelper.WriteLine("SHA256 校验通过");
                TerminalHelper.WriteLine("使用 /upgrade apply 安装更新");
            }
            else
            {
                TerminalHelper.WriteLine($"下载失败: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("下载更新", ex);
        }
    }

    private static async Task ApplyUpdateAsync(IUpgradeService service, Version currentVersion, CancellationToken ct)
    {
        TerminalHelper.WriteLine("正在获取并下载更新...");
        try
        {
            var entry = await service.GetUpdateEntryAsync(ct).ConfigureAwait(false);
            if (entry is null)
            {
                TerminalHelper.WriteLine("无可用更新或未配置更新源");
                return;
            }

            TerminalHelper.WriteLine($"发现更新: {currentVersion} → {entry.Version}");
            TerminalHelper.WriteLine("正在下载...");

            var downloadResult = await service.DownloadUpdateAsync(entry, null, ct).ConfigureAwait(false);
            if (!downloadResult.Success)
            {
                TerminalHelper.WriteLine($"下载失败: {downloadResult.ErrorMessage}");
                return;
            }

            TerminalHelper.WriteLine("下载完成，正在安装...");
            var applyResult = await service.ApplyUpdateAsync(downloadResult.DownloadedPath!, ct).ConfigureAwait(false);
            if (applyResult.Success)
            {
                TerminalHelper.WriteLine("更新安装成功! 请重启 jcc 生效");
            }
            else
            {
                TerminalHelper.WriteLine($"安装失败: {applyResult.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("应用更新", ex);
        }
    }

    private static async Task AutoUpdateAsync(IUpgradeService service, Version currentVersion, CancellationToken ct)
    {
        TerminalHelper.WriteLine("自动更新模式...");
        try
        {
            var isAvailable = await service.IsUpdateAvailableAsync(ct).ConfigureAwait(false);
            if (!isAvailable)
            {
                TerminalHelper.WriteLine("已是最新版本");
                return;
            }

            await ApplyUpdateAsync(service, currentVersion, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("自动更新", ex);
        }
    }

    private static Version GetFallbackVersion()
    {
        return typeof(UpgradeCommand).Assembly.GetName().Version ?? new Version(0, 1, 0);
    }
}
