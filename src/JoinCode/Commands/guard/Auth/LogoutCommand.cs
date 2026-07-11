namespace JoinCode.ChatCommands;

/// <summary>
/// /logout 命令 — 对齐 TS logout.tsx
/// TS 使用 performLogout + clearAuthRelatedCaches + gracefulShutdownSync
/// 对齐内容：API Key删除+Token清除+认证缓存清除+登出后退出
/// 架构差异：TS 有 trustedDevice/growthbook/policyLimits 等 Anthropic 专有缓存，C# 为多 Provider 架构
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Logout, Description = "登出 AI 服务", Usage = "/logout [provider]", Category = ChatCommandCategory.Auth)]
public sealed class LogoutCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Logout;
    public string Description => "登出 AI 服务";
    public string Usage => "/logout [provider]";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    private static readonly string AuthPath = WorkflowConstants.Paths.AuthFilePath;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var provider = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

        if (provider == "all")
        {
            // 对齐 TS: Dialog 确认框 — 登出前确认
            var confirmed = await Confirmation.ConfirmAsync("确定要登出所有服务吗？", context.CancellationToken).ConfigureAwait(false);
            if (confirmed)
            {
                // 删除 API Key 文件 — 对齐 TS removeApiKey
                if (context.Services!.FileSystem.FileExists(AuthPath))
                {
                    context.Services!.FileSystem.DeleteFile(AuthPath);
                }

                // 清除 OAuth Token 存储 — 对齐 TS secureStorage.delete
                if (context.Services!.TokenStorage is not null)
                {
                    var providers = await context.Services!.TokenStorage.GetStoredProvidersAsync(context.CancellationToken).ConfigureAwait(false);
                    foreach (var p in providers)
                    {
                        await context.Services!.TokenStorage.DeleteTokenAsync(p, context.CancellationToken).ConfigureAwait(false);
                    }
                }

                // 清除认证相关缓存 — 对齐 TS clearAuthRelatedCaches
                await PostLogoutRefreshAsync(context).ConfigureAwait(false);

                TerminalHelper.WriteLine($"{TerminalColors.Success}已登出所有服务{AnsiStyleConstants.Reset}");

                // 登出后退出 — 对齐 TS gracefulShutdownSync(0, 'logout')
                TerminalHelper.WriteLine($"{TerminalColors.Muted}登出后将退出应用...{AnsiStyleConstants.Reset}");
                return ChatCommandResult.Exit();
            }
        }
        else
        {
            // 清除指定 Provider 的 OAuth Token
            if (context.Services!.TokenStorage is not null && await context.Services!.TokenStorage.HasTokenAsync(provider, context.CancellationToken).ConfigureAwait(false))
            {
                await context.Services!.TokenStorage.DeleteTokenAsync(provider, context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Success}已登出 {provider} (OAuth){AnsiStyleConstants.Reset}");

                await PostLogoutRefreshAsync(context).ConfigureAwait(false);
                return ChatCommandResult.Continue();
            }

            // 清除指定 Provider 的 API Key
            if (context.Services!.FileSystem.FileExists(AuthPath))
            {
                var authData = await LoadAuthAsync(context.Services!.FileSystem).ConfigureAwait(false);
                if (authData.Remove(provider))
                {
                    var json = JsonSerializer.Serialize(authData, CliIndentedJsonContext.Default.DictionaryStringString);
                    await context.Services!.FileSystem.WriteAllTextAsync(AuthPath, json, context.CancellationToken).ConfigureAwait(false);
                    TerminalHelper.WriteLine($"{TerminalColors.Success}已登出 {provider}{AnsiStyleConstants.Reset}");

                    await PostLogoutRefreshAsync(context).ConfigureAwait(false);
                    return ChatCommandResult.Continue();
                }
            }

            TerminalHelper.WriteLine($"未登录 {provider}");
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 登出后刷新 — 对齐 TS logout.tsx 中的 clearAuthRelatedCaches
    /// TS: clearOAuthTokenCache + clearTrustedDeviceTokenCache + clearBetasCaches + clearToolSchemaCache
    ///     + resetUserCache + refreshGrowthBook + clearRemoteManagedSettings + clearPolicyLimitsCache
    /// C#: 成本重置 + 速率限制清除（多 Provider 架构下的等价操作）
    /// </summary>
    private static Task PostLogoutRefreshAsync(ChatCommandContext context)
    {
        // 重置成本追踪 — 对齐 TS clearAuthRelatedCaches 中的成本相关缓存
        var costTracker = context.Services!.CostTracker;
        if (costTracker is not null)
        {
            try
            {
                costTracker.Reset();
            }
            catch (Exception ex)
            {
                // 成本重置失败不影响登出
                System.Diagnostics.Trace.WriteLine($"成本重置失败: {ex.Message}");
            }
        }

        // 清除速率限制缓存 — 对齐 TS clearPolicyLimitsCache
        var rateLimitTracker = context.Services!.RateLimitTracker;
        rateLimitTracker?.Clear();

        TerminalHelper.WriteLine($"{TerminalColors.Muted}  已清除认证相关缓存{AnsiStyleConstants.Reset}");

        return Task.CompletedTask;
    }

    private static async Task<Dictionary<string, string>> LoadAuthAsync(IFileSystem fs)
    {
        try
        {
            if (!fs.FileExists(AuthPath))
            {
                return new Dictionary<string, string>();
            }

            var json = await fs.ReadAllTextAsync(AuthPath).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, CliJsonContext.Default.DictionaryStringString) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
