
namespace JoinCode.ChatCommands;

/// <summary>
/// /oauth-refresh 命令 — 刷新 OAuth Token
/// 使用已存储的 Refresh Token 获取新的 Access Token,延长登录会话有效期
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.OauthRefresh, Description = "刷新 OAuth Token", Usage = "/oauth-refresh [provider]", Category = ChatCommandCategory.Auth, ArgumentHint = "[provider]", IsHidden = true)]
[ChatCommandArg("provider", Type = "string", Description = "要刷新 Token 的供应商名称")]
public sealed class OauthRefreshCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /oauth-refresh 命令 — 刷新指定供应商的 OAuth Token
    /// 未指定供应商时刷新第一个已存储 Token 的供应商
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数、服务、取消令牌等</param>
    /// <returns>命令执行结果,始终返回 Continue 表示继续会话</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var services = context.GetCommandServices();
        if (services.TokenStorage is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}OAuth Token 存储不可用{AnsiStyleEnumConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var oauthClient = ChatCommandBase.GetService<IOAuthClient>(context, typeof(IOAuthClient));
        if (oauthClient is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}OAuth 客户端不可用{AnsiStyleEnumConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var optionsFactory = ChatCommandBase.GetService<IOptions<OAuthOptions>>(context, typeof(IOptions<OAuthOptions>));
        if (optionsFactory?.Value is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}OAuth 配置不可用{AnsiStyleEnumConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var provider = ChatCommandBase.GetNormalizedArgs(context);
        if (string.IsNullOrEmpty(provider)) {
            var providers = await services.TokenStorage.GetStoredProvidersAsync(context.CancellationToken).ConfigureAwait(false);
            if (providers.Count == 0) {
                TerminalHelper.WriteLine("无已存储的 OAuth Token，请先使用 /login --oauth 登录");
                return ChatCommandResult.Continue();
            }

            provider = providers[0];
            if (providers.Count > 1) {
                TerminalHelper.WriteLine($"发现 {providers.Count} 个 Provider，默认刷新: {provider}");
                TerminalHelper.WriteLine($"使用 /oauth-refresh <provider> 指定 Provider");
            }
        }

        try {
            var existingToken = await services.TokenStorage.LoadTokenAsync(provider, context.CancellationToken).ConfigureAwait(false);
            if (existingToken is null) {
                TerminalHelper.WriteLine($"Provider '{provider}' 无已存储的 Token");
                return ChatCommandResult.Continue();
            }

            if (string.IsNullOrEmpty(existingToken.RefreshToken)) {
                TerminalHelper.WriteLine($"{TerminalColors.Error}Provider '{provider}' 无 Refresh Token，无法刷新{AnsiStyleEnumConstants.Reset}");
                TerminalHelper.WriteLine("请使用 /login --oauth 重新登录");
                return ChatCommandResult.Continue();
            }

            TerminalHelper.WriteLine($"正在刷新 Provider '{provider}' 的 Token...");

            var config = optionsFactory.Value.ToOAuthConfig(provider);
            var newToken = await oauthClient.RefreshTokenAsync(config, existingToken.RefreshToken, context.CancellationToken).ConfigureAwait(false);

            await services.TokenStorage.SaveTokenAsync(provider, newToken, context.CancellationToken).ConfigureAwait(false);

            TerminalHelper.WriteLine($"{TerminalColors.Success}Token 刷新成功{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine($"  Provider: {provider}");
            TerminalHelper.WriteLine($"  过期时间: {newToken.ExpiresAt:yyyy-MM-dd HH:mm:ss UTC}");
        } catch (Exception ex) {
            ChatCommandBase.HandleError("Token刷新", ex);
            TerminalHelper.WriteLine("请使用 /login --oauth 重新登录");
        }

        return ChatCommandResult.Continue();
    }
}