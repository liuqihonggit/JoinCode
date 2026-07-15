
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.OauthRefresh, Description = "刷新 OAuth Token", Usage = "/oauth-refresh [provider]", Category = ChatCommandCategory.Auth)]
public sealed class OauthRefreshCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.OauthRefresh;
    public string Description => "刷新 OAuth Token";
    public string Usage => "/oauth-refresh [provider]";
    public string[] Aliases => [];
    public string ArgumentHint => "[provider]";
    public bool IsHidden => true;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        if (context.Services.TokenStorage is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}OAuth Token 存储不可用{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var oauthClient = ChatCommandBase.GetService<IOAuthClient>(context, typeof(IOAuthClient));
        if (oauthClient is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}OAuth 客户端不可用{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var optionsFactory = ChatCommandBase.GetService<IOptions<OAuthOptions>>(context, typeof(IOptions<OAuthOptions>));
        if (optionsFactory?.Value is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}OAuth 配置不可用{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var provider = ChatCommandBase.GetNormalizedArgs(context);
        if (string.IsNullOrEmpty(provider))
        {
            var providers = await context.Services.TokenStorage.GetStoredProvidersAsync(context.CancellationToken).ConfigureAwait(false);
            if (providers.Count == 0)
            {
                TerminalHelper.WriteLine("无已存储的 OAuth Token，请先使用 /login --oauth 登录");
                return ChatCommandResult.Continue();
            }

            provider = providers[0];
            if (providers.Count > 1)
            {
                TerminalHelper.WriteLine($"发现 {providers.Count} 个 Provider，默认刷新: {provider}");
                TerminalHelper.WriteLine($"使用 /oauth-refresh <provider> 指定 Provider");
            }
        }

        try
        {
            var existingToken = await context.Services.TokenStorage.LoadTokenAsync(provider, context.CancellationToken).ConfigureAwait(false);
            if (existingToken is null)
            {
                TerminalHelper.WriteLine($"Provider '{provider}' 无已存储的 Token");
                return ChatCommandResult.Continue();
            }

            if (string.IsNullOrEmpty(existingToken.RefreshToken))
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}Provider '{provider}' 无 Refresh Token，无法刷新{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine("请使用 /login --oauth 重新登录");
                return ChatCommandResult.Continue();
            }

            TerminalHelper.WriteLine($"正在刷新 Provider '{provider}' 的 Token...");

            var config = optionsFactory.Value.ToOAuthConfig(provider);
            var newToken = await oauthClient.RefreshTokenAsync(config, existingToken.RefreshToken, context.CancellationToken).ConfigureAwait(false);

            await context.Services.TokenStorage.SaveTokenAsync(provider, newToken, context.CancellationToken).ConfigureAwait(false);

            TerminalHelper.WriteLine($"{TerminalColors.Success}Token 刷新成功{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"  Provider: {provider}");
            TerminalHelper.WriteLine($"  过期时间: {newToken.ExpiresAt:yyyy-MM-dd HH:mm:ss UTC}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("Token刷新", ex);
            TerminalHelper.WriteLine("请使用 /login --oauth 重新登录");
        }

        return ChatCommandResult.Continue();
    }
}
