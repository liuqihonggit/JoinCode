
namespace JoinCode.ChatCommands;

/// <summary>
/// /login 命令 — 对齐 TS login.tsx
/// TS 使用 ConsoleOAuthFlow React 组件，C# 使用命令行交互
/// 对齐内容：API Key登录+OAuth登录+登录后刷新(成本重置+速率限制重置)
/// 架构差异：TS 有 trustedDevice/growthbook/policyLimits 等 Anthropic 专有刷新，C# 为多 Provider 架构
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Login, Description = "登录到 AI 服务", Usage = "/login [provider] [--oauth]", Category = ChatCommandCategory.Auth)]
public sealed class LoginCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Login;
    public string Description => "登录到 AI 服务";
    public string Usage => "/login [provider] [--oauth]";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    private static readonly string AuthPath = WorkflowConstants.Paths.AuthFilePath;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var providerName = args.Length > 0 ? args[0].ToLowerInvariant() : ProviderKind.OpenAI.ToValue();
        var useOAuth = args.Contains("--oauth") || args.Contains("-o");

        var definition = ProviderDefinitionRegistry.TryGet(providerName);
        if (definition is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}不支持的提供商: {providerName}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"支持的提供商: {string.Join(", ", ProviderDefinitionRegistry.RegisteredProviders)}");
            return ChatCommandResult.Continue();
        }

        TerminalHelper.WriteLine($"=== 登录到 {definition.DisplayName} ===\n");

        var loginSuccess = false;

        if (useOAuth && definition.SupportsOAuth)
        {
            loginSuccess = await LoginWithOAuthAsync(context, definition).ConfigureAwait(false);
        }
        else
        {
            loginSuccess = await LoginWithApiKeyAsync(context, definition).ConfigureAwait(false);
        }

        // 登录后刷新 — 对齐 TS login.tsx post-login refresh logic
        if (loginSuccess)
        {
            await PostLoginRefreshAsync(context).ConfigureAwait(false);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 登录后刷新 — 对齐 TS login.tsx 中的 post-login refresh
    /// TS: resetCostState + refreshRemoteManagedSettings + refreshPolicyLimits + resetUserCache + etc.
    /// C#: 成本重置 + 速率限制清除 + 服务刷新
    /// </summary>
    private static Task PostLoginRefreshAsync(ChatCommandContext context)
    {
        // 重置成本追踪 — 对齐 TS resetCostState
        var costTracker = context.Services!.CostTracker;
        if (costTracker is not null)
        {
            try
            {
                costTracker.Reset();
            }
            catch (Exception ex)
            {
                // 成本重置失败不影响登录
                System.Diagnostics.Trace.WriteLine($"成本重置失败: {ex.Message}");
            }
        }

        // 清除速率限制缓存 — 对齐 TS refreshPolicyLimits
        var rateLimitTracker = context.Services!.RateLimitTracker;
        rateLimitTracker?.Clear();

        TerminalHelper.WriteLine($"{TerminalColors.Muted}  已重置成本和速率限制数据{AnsiStyleConstants.Reset}");

        return Task.CompletedTask;
    }

    private async Task<bool> LoginWithApiKeyAsync(ChatCommandContext context, IProviderDefinition definition)
    {
        var fs = context.Services!.FileSystem;
        var apiKey = context.ReadPassword?.Invoke($"请输入 {definition.DisplayName} API Key:");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}API Key 不能为空{AnsiStyleConstants.Reset}");
            return false;
        }

        // 多态：通过 IProviderDefinition.RequiresInteractiveEndpoint + SerializeAuthCredentials 消除 `if (definition.Kind == ProviderKind.Azure)` 硬编码
        // Azure 覆写为收集 Endpoint + 返回 JSON 对象；其余 Provider 默认直接返回 apiKey
        string? endpoint = null;
        if (definition.RequiresInteractiveEndpoint)
        {
            endpoint = context.Prompt?.Invoke(definition.EndpointPromptText ?? "请输入 Endpoint:");
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}{definition.EndpointRequiredMessage ?? "Endpoint 不能为空"}{AnsiStyleConstants.Reset}");
                return false;
            }
        }

        var credentials = definition.SerializeAuthCredentials(apiKey, endpoint);
        await SaveAuthAsync(definition.ProviderName, credentials, fs).ConfigureAwait(false);

        TerminalHelper.WriteLine($"{TerminalColors.Success}{definition.DisplayName} 登录成功！{AnsiStyleConstants.Reset}");
        return true;
    }

    private async Task<bool> LoginWithOAuthAsync(ChatCommandContext context, IProviderDefinition definition)
    {
        if (context.Services!.PkceGenerator is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}PKCE 生成器未初始化，无法使用 OAuth 登录{AnsiStyleConstants.Reset}");
            return false;
        }

        if (context.Services!.TokenStorage is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}Token 存储未初始化{AnsiStyleConstants.Reset}");
            return false;
        }

        try
        {
            var config = definition.GetOAuthConfig();
            if (config is null)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}{definition.DisplayName} 不支持 OAuth 登录{AnsiStyleConstants.Reset}");
                return false;
            }

            var pkce = context.Services!.PkceGenerator.Generate();
            var state = Guid.NewGuid().ToString("N");

            using var httpClient = Infrastructure.Http.HttpClientProviderFactory.Create().GetClient();
            var oauthClient = new OAuthClient(httpClient, null);

            var authUrl = oauthClient.BuildAuthorizationUrl(config, state, pkce);

            TerminalHelper.WriteLine("请在浏览器中打开以下链接进行授权:");
            TerminalHelper.WriteLine(authUrl);
            TerminalHelper.WriteLine("\n授权完成后，请输入授权码:");

            var code = context.Prompt?.Invoke("")?.Trim();

            if (string.IsNullOrEmpty(code))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}授权码不能为空{AnsiStyleConstants.Reset}");
                return false;
            }

            TerminalHelper.WriteLine("正在获取访问令牌...");
            var token = await oauthClient.ExchangeCodeAsync(config, code, pkce, context.CancellationToken).ConfigureAwait(false);

            await context.Services!.TokenStorage.SaveTokenAsync(definition.ProviderName, token, context.CancellationToken).ConfigureAwait(false);

            TerminalHelper.WriteLine($"{TerminalColors.Success}{definition.DisplayName} OAuth 登录成功！{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"令牌过期时间: {token.ExpiresAt:yyyy-MM-dd HH:mm:ss}");
            return true;
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine("登录已取消。");
            return false;
        }
        catch (OAuthException ex)
        {
            ChatCommandBase.HandleError("OAuth登录", ex);
            return false;
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("登录", ex);
            return false;
        }
    }

    private async Task SaveAuthAsync(string provider, string credentials, IFileSystem fs)
    {
        var authData = await LoadAuthAsync(fs);
        authData[provider] = credentials;

        var directory = Path.GetDirectoryName(AuthPath);
        if (!string.IsNullOrEmpty(directory) && !fs.DirectoryExists(directory))
        {
            DirectoryHelper.EnsureDirectoryExists(fs, directory);
        }

        var json = JsonSerializer.Serialize(authData, CliIndentedJsonContext.Default.DictionaryStringString);
        await fs.WriteAllTextAsync(AuthPath, json).ConfigureAwait(false);
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
