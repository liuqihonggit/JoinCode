
namespace Bridge.Tests;

/// <summary>
/// BridgeDeviceTokenService 单元测试 — 对齐 TS 端 trustedDevice.ts
/// 测试令牌获取优先级、缓存、清除、注册
/// </summary>
public sealed class BridgeDeviceTokenServiceTests : IDisposable
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly string _authPath = "/test/auth.json";
    private readonly string? _originalEnvToken;

    public BridgeDeviceTokenServiceTests()
    {
        _fs.CreateDirectory("/test/");

        // 保存原始环境变量
        _originalEnvToken = Environment.GetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN");
        Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", _originalEnvToken);
    }

    private BridgeDeviceTokenService CreateSut(HttpResponseMessage? enrollResponse = null)
    {
        var handler = new MockHttpMessageHandler(enrollResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        return new BridgeDeviceTokenService(httpClient, _fs, logger: null, authFilePath: _authPath);
    }

    /// <summary>写入 auth.json 到临时目录</summary>
    private void WriteAuthJson(string json) => _fs.WriteAllText(_authPath, json);

    #region GetTrustedDeviceTokenAsync

    [Fact]
    public async Task GetToken_ReturnsNull_WhenNoSource()
    {
        var sut = CreateSut();
        var result = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetToken_ReturnsEnvVar_WhenSet()
    {
        Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", "env-token-123");
        try
        {
            var sut = CreateSut();
            var result = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
            result.Should().Be("env-token-123");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", null);
        }
    }

    [Fact]
    public async Task GetToken_ReturnsCachedValue_WhenAlreadyCached()
    {
        Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", "cached-token");
        var sut = CreateSut();
        var first = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        // 清除环境变量
        Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", null);
        // 第二次应返回缓存
        var second = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        second.Should().Be("cached-token");
    }

    [Fact]
    public async Task GetToken_EnvVarTakesPrecedenceOverStorage()
    {
        WriteAuthJson("""{"device_token": "storage-token", "api_key": "key123"}""");
        Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", "env-override");
        try
        {
            var sut = CreateSut();
            var result = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
            result.Should().Be("env-override");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", null);
        }
    }

    [Fact]
    public async Task GetToken_ReadsFromStorage_WhenNoCacheAndNoEnvVar()
    {
        WriteAuthJson("""{"device_token": "storage-token-xyz", "api_key": "key123"}""");
        var sut = CreateSut();
        var result = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        result.Should().Be("storage-token-xyz");
    }

    #endregion

    #region ClearCache

    [Fact]
    public async Task ClearCache_ForcesReReadFromEnvVar()
    {
        Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", "first-token");
        var sut = CreateSut();
        var first = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        first.Should().Be("first-token");

        // 改变环境变量
        Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", "second-token");

        // 未清除缓存，仍返回旧值
        var cached = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        cached.Should().Be("first-token");

        // 清除缓存后，重新读取
        sut.ClearCache();
        var refreshed = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        refreshed.Should().Be("second-token");

        Environment.SetEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN", null);
    }

    #endregion

    #region ClearTokenAsync

    [Fact]
    public async Task ClearToken_ClearsCacheAndDeletesFromStorage()
    {
        WriteAuthJson("""{"device_token": "old-token", "api_key": "key123"}""");
        var sut = CreateSut();

        // 先通过存储读取缓存
        var cached = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        cached.Should().Be("old-token");

        // 清除
        await sut.ClearTokenAsync().ConfigureAwait(true);

        // 验证 auth.json 中 device_token 已被删除
        var content = _fs.ReadAllText(_authPath);
        content.Should().NotContain("old-token", "device_token值应已从文件中删除");
        content.Should().Contain("api_key");

        // 缓存已清除，存储中 device_token 已删除
        var result = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        result.Should().BeNull();
    }

    #endregion

    #region EnrollTrustedDeviceAsync

    [Fact]
    public async Task Enroll_SetsCacheAndPersistsToken()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{"device_token": "enrolled-token-abc", "device_id": "dev-001"}""")
        };
        var sut = CreateSut(response);

        await sut.EnrollTrustedDeviceAsync("access-token-123").ConfigureAwait(true);

        // 验证缓存
        var token = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        token.Should().Be("enrolled-token-abc");

        // 验证持久化
        var content = _fs.ReadAllText(_authPath);
        content.Should().Contain("enrolled-token-abc");
    }

    [Fact]
    public async Task Enroll_DoesNotThrow_OnFailure()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
        var sut = CreateSut(response);

        var act = async () => await sut.EnrollTrustedDeviceAsync("access-token").ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Enroll_DoesNotThrow_OnNetworkError()
    {
        var handler = new MockFailingHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        var sut = new BridgeDeviceTokenService(httpClient, TestFileSystem.Current, logger: null, authFilePath: _authPath);

        var act = async () => await sut.EnrollTrustedDeviceAsync("access-token").ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task Enroll_SendsBearerToken()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockCapturingHttpMessageHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"device_token": "tok", "device_id": "d1"}""")
            },
            req => capturedRequest = req);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        var sut = new BridgeDeviceTokenService(httpClient, TestFileSystem.Current, logger: null, authFilePath: _authPath);

        await sut.EnrollTrustedDeviceAsync("my-access-token").ConfigureAwait(true);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("my-access-token");
    }

    [Fact]
    public async Task Enroll_SendsDisplayNameInRequestBody()
    {
        string? requestBody = null;
        var handler = new MockCapturingHttpMessageHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"device_token": "tok", "device_id": "d1"}""")
            },
            async req => requestBody = await req.Content!.ReadAsStringAsync().ConfigureAwait(true));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        var sut = new BridgeDeviceTokenService(httpClient, TestFileSystem.Current, logger: null, authFilePath: _authPath);

        await sut.EnrollTrustedDeviceAsync("access-token").ConfigureAwait(true);

        requestBody.Should().NotBeNull();
        requestBody.Should().Contain("display_name");
    }

    [Fact]
    public async Task Enroll_PreservesExistingAuthJsonFields()
    {
        WriteAuthJson("""{"api_key": "existing-key", "org_id": "org-123"}""");
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{"device_token": "new-tok", "device_id": "d1"}""")
        };
        var sut = CreateSut(response);

        await sut.EnrollTrustedDeviceAsync("access-token").ConfigureAwait(true);

        var content = _fs.ReadAllText(_authPath);
        content.Should().Contain("new-tok");
        content.Should().Contain("api_key");
        content.Should().Contain("org_id");
    }

    #endregion

    #region Best-effort behavior

    [Fact]
    public async Task GetToken_DoesNotThrow_WhenAuthJsonMissing()
    {
        var sut = CreateSut();
        var result = await sut.GetTrustedDeviceTokenAsync().ConfigureAwait(true);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ClearToken_DoesNotThrow_WhenAuthJsonMissing()
    {
        var sut = CreateSut();
        var act = async () => await sut.ClearTokenAsync().ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    #endregion

    #region Mock Helpers

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;

        public MockHttpMessageHandler(HttpResponseMessage? response = null) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response
                ?? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    private sealed class MockFailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Connection refused");
    }

    private sealed class MockCapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        private readonly Action<HttpRequestMessage>? _captureRequest;
        private readonly Func<HttpRequestMessage, Task>? _captureRequestAsync;

        public MockCapturingHttpMessageHandler(
            HttpResponseMessage response,
            Action<HttpRequestMessage>? captureRequest = null,
            Func<HttpRequestMessage, Task>? captureRequestAsync = null)
        {
            _response = response;
            _captureRequest = captureRequest;
            _captureRequestAsync = captureRequestAsync;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _captureRequest?.Invoke(request);
            if (_captureRequestAsync is not null)
                await _captureRequestAsync(request).ConfigureAwait(true);
            return _response;
        }
    }

    #endregion
}
