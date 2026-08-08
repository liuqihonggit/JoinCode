
namespace JoinCode.ChatCommands;

using JoinCode.Abstractions.Security.Sandbox;

[ChatCommand(Name = ChatCommandNameConstants.SandboxToggle, Description = "切换沙箱模式", Usage = "/sandbox-toggle [on|off|status|exclude|switch]", Category = ChatCommandCategory.Config, Aliases = ["sandbox"], ArgumentHint = "[on|off|status|exclude|switch]", IsHidden = true)]
public sealed class SandboxToggleCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var sandboxManager = ChatCommandBase.GetService<ISandboxManager>(context);
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();
        var subCommand = args.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? args;

        if (subCommand is "exclude")
        {
            await HandleExcludeAsync(context, args).ConfigureAwait(false);
        }
        else if (subCommand is "switch")
        {
            await HandleSwitchAsync(sandboxManager, context, args).ConfigureAwait(false);
        }
        else if (sandboxManager is null)
        {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostSandboxServiceNotInitialized));
            }
        }
        else if (subCommand is "on" or "enable")
        {
            await EnableSandboxAsync(sandboxManager, context).ConfigureAwait(false);
        }
        else if (subCommand is "off" or "disable")
        {
            await DisableSandboxAsync(sandboxManager, context).ConfigureAwait(false);
        }
        else
        {
            ShowStatus(sandboxManager);
        }

        return ChatCommandResult.Continue();
    }

    private static async Task EnableSandboxAsync(ISandboxManager sandboxManager, ChatCommandContext context)
    {
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
                Type = SandboxType.Soft,
                RestrictNetwork = true,
                RestrictFileSystem = true,
                AllowedPaths = excludedPaths
            };
            var info = await sandboxManager.EnterSandboxAsync(options, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxEnabled), info.Type));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxRootPath), info.RootPath));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxNetworkRestricted), info.IsRestricted ? "是" : "否"));
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("启用沙箱模式", ex);
        }
    }

    private static async Task DisableSandboxAsync(ISandboxManager sandboxManager, ChatCommandContext context)
    {
        var confirmed = await Confirmation.ConfirmAsync("确定要禁用沙箱模式吗？", context.CancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxCancelled));
            return;
        }

        try
        {
            await sandboxManager.ExitSandboxAsync(context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxDisabled));
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("禁用沙箱模式", ex);
        }
    }

    private static void ShowStatus(ISandboxManager sandboxManager)
    {
        var isInSandbox = sandboxManager.IsInSandbox;
        var current = sandboxManager.CurrentSandbox;
        TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxStatusHeader), isInSandbox ? "已启用" : "已禁用"));

        if (current is not null)
        {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxTypeLabel), current.Type));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxRootPath), current.RootPath));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxEnteredAtLabel), current.EnteredAt));
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxRestrictedLabel), current.IsRestricted ? "是" : "否"));
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"可用沙箱类型: {string.Join(", ", sandboxManager.AvailableTypes.Select(t => t.ToValue()))}");

        var platform = Environment.OSVersion.Platform;
        var isSupported = platform == PlatformID.Win32NT || platform == PlatformID.Unix || platform == PlatformID.MacOSX;
        TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostSandboxStatusPlatformSupported), isSupported ? "是" : "未知"));

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(L.T(StringKey.HostSandboxUsageEnable));
        TerminalHelper.WriteLine(L.T(StringKey.HostSandboxUsageDisable));
        TerminalHelper.WriteLine(L.T(StringKey.HostSandboxUsageExclude));
        TerminalHelper.WriteLine("  /sandbox-toggle switch <soft|process|docker|bubblewrap>  — 切换沙箱类型");
    }

    private static async Task HandleSwitchAsync(ISandboxManager? sandboxManager, ChatCommandContext context, string args)
    {
        if (sandboxManager is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostSandboxServiceNotInitialized));
            return;
        }

        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            TerminalHelper.WriteLine($"当前沙箱类型: {sandboxManager.ActiveSandboxType.ToValue()}");
            TerminalHelper.WriteLine($"可用类型: {string.Join(", ", sandboxManager.AvailableTypes.Select(t => t.ToValue()))}");
            TerminalHelper.WriteLine("用法: /sandbox-toggle switch <soft|process|docker|bubblewrap>");
            return;
        }

        var targetType = SandboxTypeExtensions.FromValue(parts[1]);
        if (targetType is null)
        {
            TerminalHelper.WriteLine($"未知沙箱类型: {parts[1]}");
            TerminalHelper.WriteLine($"可用类型: {string.Join(", ", sandboxManager.AvailableTypes.Select(t => t.ToValue()))}");
            return;
        }

        try
        {
            await sandboxManager.SwitchProviderAsync(targetType.Value, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"沙箱已切换到: {targetType.Value.ToValue()}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("切换沙箱类型", ex);
        }
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
            var pathSet = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            if (!pathSet.Contains(fullPath))
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
