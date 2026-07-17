namespace JoinCode.Abstractions.Services;

public static class OAuth2TokenExchange
{
    public static async Task<OAuth2TokenResponse> ExchangeTokenAsync(
        HttpClient httpClient,
        string tokenEndpoint,
        Dictionary<string, string> parameters,
        JsonTypeInfo<OAuth2TokenResponse> jsonTypeInfo,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrEmpty(tokenEndpoint);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var content = new FormUrlEncodedContent(parameters);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        logger?.LogDebug("请求令牌: {Endpoint}", tokenEndpoint);

        var response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            logger?.LogError("令牌请求失败: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new OAuthException($"Token request failed: {response.StatusCode}", response.StatusCode, responseBody);
        }

        var tokenResponse = JsonSerializer.Deserialize(responseBody, jsonTypeInfo);

        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new OAuthException("Invalid token response");
        }

        logger?.LogInformation("令牌获取成功");
        return tokenResponse;
    }
}
