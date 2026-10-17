namespace Core.Tests.Web;

/// <summary>
/// SubAgent 兜底搜索端到端测试 — 真实网络调用 DuckDuckGo，验证无 API key 时兜底链路可用（ADR 0138）。
/// 标记 Integration 类别，需网络访问 html.duckduckgo.com。
/// 网络不可达时用 SkippableFact 跳过（环境问题非代码问题），不掩盖——明确报告跳过原因。
/// </summary>
[Trait("Category", "Integration")]
public sealed class SubAgentSearchProviderE2ETests {
    private sealed class SimpleApiClient : IApiClient, IAsyncDisposable {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private int _disposed;

        public async Task<HttpResponseMessage> SendAsync(ApiRequest request, CancellationToken cancellationToken = default) {
            using var message = new HttpRequestMessage(request.Method, request.Path);
            if (request.Headers is not null) {
                foreach (var (key, value) in request.Headers) {
                    message.Headers.TryAddWithoutValidation(key, value);
                }
            }
            if (request.Body is not null) {
                message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
            }
            return await _http.SendAsync(message, cancellationToken);
        }

        public Task<ApiResponse<T>> RequestAsync<T>(ApiRequest request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T> RequestOrThrowAsync<T>(ApiRequest request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<T>> GetAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<T>> PostAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<T>> PutAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<T>> PatchAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<T>> DeleteAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void SetDefaultHeader(string name, string value) => throw new NotSupportedException();
        public void RemoveDefaultHeader(string name) => throw new NotSupportedException();
        public void SetAuthorizationToken(string token, string scheme = "Bearer") => throw new NotSupportedException();
        public void ClearAuthorization() => throw new NotSupportedException();
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _http.Dispose();
        }
        public ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
            _http.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 检查 DuckDuckGo 网络可达性 — 不可达时返回 false。
    /// </summary>
    private static async Task<bool> IsNetworkReachableAsync() {
        try {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync("https://html.duckduckgo.com/html/?q=test");
            return response.IsSuccessStatusCode;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// 真实网络调用 DuckDuckGo 验证 SubAgent 兜底链路：无 API key → 抓取 HTML → 正则提取结果。
    /// 网络不可达时跳过测试（环境问题非代码问题），网络可达时验证完整链路。
    /// </summary>
    [SkippableFact]
    public async Task SubAgent_E2E_RealDuckDuckGo_FetchesAndExtractsResults() {
        var networkOk = await IsNetworkReachableAsync();
        Skip.IfNot(networkOk, "网络不可达: html.duckduckgo.com 无法连接，跳过 E2E 测试（环境问题非代码问题）");

        await using var apiClient = new SimpleApiClient();
        await using var provider = new SubAgentSearchProvider(apiClient);

        provider.IsAvailable.Should().BeTrue("SubAgent 兜底应永远可用");
        provider.Priority.Should().Be(999, "兜底优先级最低");
        provider.Name.Should().Be("SubAgent");

        SearchProviderResult result = new(false, "SubAgent", [], 0, ErrorMessage: "not attempted");
        for (var attempt = 0; attempt < 3; attempt++) {
            result = await provider.SearchAsync(new SearchRequest("dotnet documentation", MaxResults: 5)).ConfigureAwait(true);
            if (result.Success && result.Results.Count > 0) break;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        result.Success.Should().BeTrue($"3 次重试后仍失败, 最后错误={result.ErrorMessage}");
        result.ProviderName.Should().Be("SubAgent");
        result.Results.Should().NotBeEmpty("搜索 dotnet documentation 应返回结果");
        result.DurationSeconds.Should().BeGreaterThan(0);

        foreach (var item in result.Results) {
            item.Url.Should().StartWith("http", "结果 URL 应为有效 HTTP 链接");
            item.Title.Should().NotBeNullOrEmpty("结果标题不应为空");
        }
    }
}
