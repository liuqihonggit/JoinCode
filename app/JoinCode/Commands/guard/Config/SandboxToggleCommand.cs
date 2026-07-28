
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.SandboxToggle, Description = "切换沙箱模式", Usage = "/sandbox-toggle [on|off|status|exclude]", Category = ChatCommandCategory.Config, Aliases = ["sandbox"], ArgumentHint = "[on|off|status|exclude]", IsHidden = true)]
public sealed class SandboxToggleCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var sandboxService = ChatCommandBase.GetService<ISandboxModeService>(context);
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();
        var subCommand = args.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? args;

        if (subCommand is "exclude")
        {
            await HandleExcludeAsync(context, args).ConfigureAwait(false);
        }
        else if (sandboxService is null)
        {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostSandboxServiceNotInitialized));
            }
        }
        else if (subCommand is "on" or "enable")
        {
            await EnableSandboxAsync(sandboxService, context).ConfigureAwait(false);
        }
        else if (subCommand is "off" or "disable")
        {
            await DisableSandboxAsync(sandboxService, context).ConfigureAwait(false);
        }
        else
        {
            ShowStatus(sandboxService);
        }

        return ChatCommandResult.Continue();
    }

    private static async Task EnableSandboxAsync(ISandboxModeService sandboxService, ChatCommandContext context)
    {
        // 对齐 TS: 启用沙箱前确认
        var confirmed = await Confirmation.ConfirmAsync("确定要启用沙箱模式吗？启用后将限制文件系统和网络访问。", context.CancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxCancelled));
            return;
        }

        try
        {
            var excludedPaths = await GetExcludedPathsAsync(context).ConfigureAwait(false);
            var options = new SandboxOptions
            {
                Type = SandboxType.Process,
                RestrictNetwork = true,
                RestrictFileSystem = true,
                AllowedPaths = excludedPaths
            };
            var info = await sandboxService.EnterSandboxAsync(options, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxEnabled), info.Type));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxRootPath), info.RootPath));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxNetworkRestricted), info.IsRestricted ? "是" : "否"));
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("启用沙箱模式", ex);
        }
    }

    private static async Task DisableSandboxAsync(ISandboxModeService sandboxService, ChatCommandContext context)
    {
        // 对齐 TS: 禁用沙箱前确认
        var confirmed = await Confirmation.ConfirmAsync("确定要禁用沙箱模式吗？", context.CancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxCancelled));
            return;
        }

        try
        {
            await sandboxService.ExitSandboxAsync(context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxDisabled));
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("禁用沙箱模式", ex);
        }
    }

    private static void ShowStatus(ISandboxModeService sandboxService)
    {
        var isInSandbox = sandboxService.IsInSandbox;
        var current = sandboxService.CurrentSandbox;
        TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxStatusHeader), isInSandbox ? "已启用" : "已禁用"));

        if (current is not null)
        {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxTypeLabel), current.Type));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxRootPath), current.RootPath));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxEnteredAtLabel), current.EnteredAt));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxRestrictedLabel), current.IsRestricted ? "是" : "否"));
        }

        var platform = Environment.OSVersion.Platform;
        var isSupported = platform == PlatformID.Win32NT || platform == PlatformID.Unix || platform == PlatformID.MacOSX;
        TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxStatusPlatformSupported), isSupported ? "是" : "未知"));

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(L.T(StringKey.HostSandboxUsageEnable));
        TerminalHelper.WriteLine(L.T(StringKey.HostSandboxUsageDisable));
        TerminalHelper.WriteLine(L.T(StringKey.HostSandboxUsageExclude));
    }

    private static async Task HandleExcludeAsync(ChatCommandContext context, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxExcludePathsHeader));
            var excluded = await GetExcludedPathsAsync(context).ConfigureAwait(false);
            if (excluded.Count == 0)
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostSandboxExcludeNone));
            }
            else
            {
                foreach (var path in excluded)
                {
                    TerminalHelper.WriteLine($"  {path}");
                }
            }
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxExcludeAddUsage));
            return;
        }

        var pathToAdd = string.Join(' ', parts[1..]);
        if (!context.Services.FileSystem.DirectoryExists(pathToAdd) && !context.Services.FileSystem.FileExists(pathToAdd))
        {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxPathNotFound), pathToAdd));
            return;
        }

        var fullPath = Path.GetFullPath(pathToAdd);
        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxConfigUnavailable));
            return;
        }

        try
        {
            var existing = await configService.GetAsync("sandbox.excludedPaths", context.CancellationToken).ConfigureAwait(false);
            var paths = string.IsNullOrEmpty(existing) ? [] : existing.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!paths.Contains(fullPath))
            {
                paths.Add(fullPath);
                await configService.SetAsync("sandbox.excludedPaths", string.Join(";", paths), context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxPathExcluded), fullPath));
            }
            else
            {
                TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxPathAlreadyExcluded), fullPath));
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("添加排除路径", ex);
        }
    }

    private static async Task<List<string>> GetExcludedPathsAsync(ChatCommandContext context)
    {
        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is null) return [];

        try
        {
            var existing = await configService.GetAsync("sandbox.excludedPaths", CancellationToken.None).ConfigureAwait(false);
            return string.IsNullOrEmpty(existing) ? [] : existing.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch
        {
            return [];
        }
    }
}
