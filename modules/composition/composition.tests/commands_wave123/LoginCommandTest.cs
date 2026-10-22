namespace Composition.Tests.Commands;

/// <summary>
/// /login 命令单元测试 — 覆盖 provider 解析(默认 openai/大小写归一)/不支持 provider 提示与引导/
/// API Key 空输入与 null 回调降级/API Key 成功登录(auth.json 写入+登录后刷新缓存)/
/// Endpoint 交互式收集(Azure 形态两分支)/OAuth 前置缺失三级降级/OAuth 授权码为空
/// <para>auth.json 写入经 InMemoryFileSystem(0磁盘IO),路径与命令侧 AppDataConstants.Paths.AuthFilePath 同源</para>
/// </summary>
[Collection("command-output")]
public sealed class LoginCommandTest {
    private static readonly string AuthPath = AppDataConstants.Paths.AuthFilePath;

    /// <summary>
    /// 假 Provider 定义 — 实现 IProviderDefinition 抽象成员,OAuth/Endpoint 行为由构造参数控制,
    /// SerializeAuthCredentials 覆写为复合凭证格式以便断言 endpoint 收集链路
    /// </summary>
    private sealed class FakeProviderDefinition : IProviderDefinition {
        private readonly bool _supportsOAuth;
        private readonly OAuthConfig? _oauthConfig;
        private readonly bool _requiresEndpoint;

        public FakeProviderDefinition(
            bool supportsOAuth = false,
            OAuthConfig? oauthConfig = null,
            bool requiresEndpoint = false) {
            _supportsOAuth = supportsOAuth;
            _oauthConfig = oauthConfig;
            _requiresEndpoint = requiresEndpoint;
        }

        public VendorKind Vendor => VendorKind.OpenAi;
        public ProtocolKind Protocol => ProtocolKind.OpenAiCompatible;
        public string ProviderName => "openai";
        public string DisplayName => "OpenAI";
        public string DefaultModelId => "gpt-4o-mini";
        public string DefaultFastModelId => "gpt-4o-mini";
        public string? DefaultEndpoint => null;
        public IEnumerable<ModelEntry> AvailableModels => [];
        public string? ApiKeyEnvironmentVariable => "OPENAI_API_KEY";
        public string? EndpointEnvironmentVariable => null;
        public string? ResolveApiKeyFromEnv() => null;
        public bool IsValid(ProviderConfig config) => true;
        public string GetBaseUrl(ProviderConfig config) => "https://api.openai.com/v1";
        public string GetChatEndpoint(ProviderConfig config) => "chat/completions";
        public void ConfigureHttpClient(HttpClient client, ProviderConfig config) { }

        public bool SupportsOAuth => _supportsOAuth;
        public OAuthConfig? GetOAuthConfig() => _oauthConfig;
        public bool RequiresInteractiveEndpoint => _requiresEndpoint;
        public string? EndpointPromptText => "请输入 Endpoint:";
        public string? EndpointRequiredMessage => "Endpoint 不能为空";
        public string SerializeAuthCredentials(string apiKey, string? endpoint)
            => _requiresEndpoint && endpoint is not null
                ? $"endpoint={endpoint};key={apiKey}"
                : apiKey;
    }

    private static Mock<IProviderDefinitionRegistry> CreateRegistry(
        Func<string, IProviderDefinition?> tryGet,
        params string[] registeredProviders) {
        var mock = new Mock<IProviderDefinitionRegistry>();
        mock.Setup(r => r.TryGet(It.IsAny<string>())).Returns(tryGet);
        mock.Setup(r => r.GetRegisteredProviders()).Returns(registeredProviders);
        return mock;
    }

    [Fact]
    public async Task Execute_UnknownProvider_ShowsErrorWithSupportedList() {
        var registry = CreateRegistry(_ => null, "openai", "anthropic");
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(registry.Object));
        var readPasswordCalled = false;
        var context = CommandTestHarness.CreateContext(
            services, arguments: "bogus",
            readPassword: _ => { readPasswordCalled = true; return "sk-key"; });

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("不支持的提供商: bogus");
        output.Should().Contain("openai, anthropic");
        readPasswordCalled.Should().BeFalse("未知 provider 不应进入密钥输入环节");
    }

    [Fact]
    public async Task Execute_EmptyArgs_ResolvesDefaultOpenAiProvider() {
        string? resolved = null;
        var registry = CreateRegistry(name => {
            resolved = name;
            return null;
        }, "openai");
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: "");

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        resolved.Should().Be("openai", "空参默认 provider 应为 VendorKind.OpenAi 的枚举值");
    }

    [Fact]
    public async Task Execute_ProviderName_IsNormalizedToLowerCase() {
        var requested = string.Empty;
        var registry = CreateRegistry(name => {
            requested = name;
            return null;
        });
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: "OpenAI");

        await CommandTestHarness.CaptureOutputAsync(() => new LoginCommand().ExecuteAsync(context));

        requested.Should().Be("openai", "provider 参数应 ToLowerInvariant 归一后再查注册表");
    }

    [Fact]
    public async Task Execute_EmptyApiKey_ShowsErrorWithoutAuthWrite() {
        var definition = new FakeProviderDefinition();
        var registry = CreateRegistry(_ => definition, "openai");
        var fs = TestFileSystem.Current;
        var services = CommandTestHarness.CreateServices(
            fileSystem: fs,
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(
            services, arguments: "openai",
            readPassword: _ => "   ");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("API Key 不能为空");
        output.Should().NotContain("登录成功");
        output.Should().NotContain("已重置成本和速率限制数据", "登录失败不应触发登录后刷新");
        fs.FileExists(AuthPath).Should().BeFalse("失败的登录不应写入 auth.json");
    }

    [Fact]
    public async Task Execute_NullReadPasswordCallback_DegradesToFailedLogin() {
        var definition = new FakeProviderDefinition();
        var registry = CreateRegistry(_ => definition, "openai");
        var fs = TestFileSystem.Current;
        var services = CommandTestHarness.CreateServices(
            fileSystem: fs,
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: "openai", readPassword: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("API Key 不能为空", "ReadPassword 回调缺失(非交互环境)应等价空密钥降级");
        fs.FileExists(AuthPath).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_ValidApiKey_PersistsAuthFileAndRefreshes() {
        var definition = new FakeProviderDefinition();
        var registry = CreateRegistry(_ => definition, "openai");
        var fs = TestFileSystem.Current;
        var rateLimitTracker = new Mock<IRateLimitTracker>();
        var services = CommandTestHarness.CreateServices(
            fileSystem: fs,
            rateLimitTracker: rateLimitTracker.Object,
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(
            services, arguments: "openai",
            readPassword: _ => "sk-test-123");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 登录到 OpenAI ===");
        output.Should().Contain("OpenAI 登录成功！");
        output.Should().Contain("已重置成本和速率限制数据", "登录成功后应刷新成本与速率限制");
        rateLimitTracker.Verify(t => t.Clear(), Times.Once, "登录后刷新应清除速率限制缓存");
        fs.FileExists(AuthPath).Should().BeTrue("成功登录应写入 auth.json");
        var authContent = await fs.ReadAllTextAsync(AuthPath);
        authContent.Should().Contain("\"openai\"", "auth.json 的键应为 provider 小写标识");
        authContent.Should().Contain("sk-test-123", "auth.json 应存储 SerializeAuthCredentials 序列化的凭证");
    }

    [Fact]
    public async Task Execute_EndpointProvider_EmptyEndpoint_ShowsError() {
        var definition = new FakeProviderDefinition(requiresEndpoint: true);
        var registry = CreateRegistry(_ => definition, "openai");
        var fs = TestFileSystem.Current;
        var services = CommandTestHarness.CreateServices(
            fileSystem: fs,
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(
            services, arguments: "openai",
            readPassword: _ => "sk-key",
            prompt: _ => "  ");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Endpoint 不能为空", "RequiresInteractiveEndpoint 供应商空 endpoint 应拒绝");
        fs.FileExists(AuthPath).Should().BeFalse("endpoint 校验失败不应写 auth.json");
    }

    [Fact]
    public async Task Execute_EndpointProvider_ValidEndpoint_SerializesCompoundCredentials() {
        var definition = new FakeProviderDefinition(requiresEndpoint: true);
        var registry = CreateRegistry(_ => definition, "openai");
        var fs = TestFileSystem.Current;
        var services = CommandTestHarness.CreateServices(
            fileSystem: fs,
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(
            services, arguments: "openai",
            readPassword: _ => "sk-key",
            prompt: _ => "https://my-resource.openai.azure.com");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("登录成功");
        fs.FileExists(AuthPath).Should().BeTrue();
        var authContent = await fs.ReadAllTextAsync(AuthPath);
        authContent.Should().Contain("endpoint=https://my-resource.openai.azure.com",
            "复合凭证应含交互收集的 endpoint");
        authContent.Should().Contain("key=sk-key", "复合凭证应含 API Key");
    }

    [Fact]
    public async Task Execute_OAuthWithoutPkceGenerator_ShowsError() {
        var definition = new FakeProviderDefinition(supportsOAuth: true);
        var registry = CreateRegistry(_ => definition, "openai");
        var fs = TestFileSystem.Current;
        var services = CommandTestHarness.CreateServices(
            fileSystem: fs,
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: "openai --oauth");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("PKCE 生成器未初始化");
        output.Should().NotContain("登录成功");
        fs.FileExists(AuthPath).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_OAuthWithoutTokenStorage_ShowsError() {
        var definition = new FakeProviderDefinition(supportsOAuth: true);
        var registry = CreateRegistry(_ => definition, "openai");
        var pkce = new Mock<IPkceGenerator>();
        pkce.Setup(g => g.Generate()).Returns(new PkceParameters {
            CodeVerifier = "verifier-0123456789012345678901234567890123456789",
            CodeChallenge = "challenge",
            CodeChallengeMethod = "S256",
        });
        var services = CommandTestHarness.CreateServices(
            pkceGenerator: pkce.Object,
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: "openai --oauth");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Token 存储未初始化");
    }

    [Fact]
    public async Task Execute_OAuthWithoutConfig_ShowsError() {
        var definition = new FakeProviderDefinition(supportsOAuth: true, oauthConfig: null);
        var registry = CreateRegistry(_ => definition, "openai");
        var pkce = new Mock<IPkceGenerator>();
        pkce.Setup(g => g.Generate()).Returns(new PkceParameters {
            CodeVerifier = "verifier-0123456789012345678901234567890123456789",
            CodeChallenge = "challenge",
            CodeChallengeMethod = "S256",
        });
        var tokenStorage = new Mock<ITokenStorage>();
        var services = CommandTestHarness.CreateServices(
            pkceGenerator: pkce.Object,
            tokenStorage: tokenStorage.Object,
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: "openai --oauth");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("OpenAI 不支持 OAuth 登录", "GetOAuthConfig 为 null 时应提示供应商不支持");
    }

    [Fact]
    public async Task Execute_OAuthEmptyAuthCode_ShowsErrorWithoutTokenSave() {
        var oauthConfig = new OAuthConfig {
            Provider = "openai",
            ClientId = "client-id",
            AuthorizationEndpoint = "https://auth.example.com/authorize",
            TokenEndpoint = "https://auth.example.com/token",
            RedirectUri = "http://localhost:8913/callback",
        };
        var definition = new FakeProviderDefinition(supportsOAuth: true, oauthConfig: oauthConfig);
        var registry = CreateRegistry(_ => definition, "openai");
        var pkce = new Mock<IPkceGenerator>();
        pkce.Setup(g => g.Generate()).Returns(new PkceParameters {
            CodeVerifier = "verifier-0123456789012345678901234567890123456789",
            CodeChallenge = "challenge",
            CodeChallengeMethod = "S256",
        });
        var tokenStorage = new Mock<ITokenStorage>();
        var services = CommandTestHarness.CreateServices(
            pkceGenerator: pkce.Object,
            tokenStorage: tokenStorage.Object,
            configureDi: di => di.AddSingleton(registry.Object));
        var context = CommandTestHarness.CreateContext(
            services, arguments: "openai --oauth",
            prompt: _ => "   ");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LoginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("请在浏览器中打开以下链接进行授权", "应输出授权 URL 引导用户");
        output.Should().Contain("https://auth.example.com/authorize");
        output.Should().Contain("授权码不能为空", "空授权码应拒绝,不得发起令牌交换");
        tokenStorage.Verify(t => t.SaveTokenAsync(It.IsAny<string>(), It.IsAny<OAuthToken>(), It.IsAny<CancellationToken>()),
            Times.Never, "空授权码不应保存任何令牌");
    }
}
