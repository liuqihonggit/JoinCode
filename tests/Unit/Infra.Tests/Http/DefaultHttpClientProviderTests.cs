namespace Infra.Tests.Http;

using Infrastructure.Http;
using JoinCode.Abstractions.Http;

/// <summary>
/// DefaultHttpClientProvider 单元测试 — P1-5 推广 HttpClientFactory
/// 验证:
/// 1. IHttpClientFactory 可用时 → 优先使用 CreateClient() 创建 HttpClient（Handler 池化）
/// 2. IHttpClientFactory 不可用时 → fallback 到 new HttpClient()（向后兼容）
/// 3. 命名客户端支持
/// </summary>
public sealed class DefaultHttpClientProviderTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // 红测试 1: IHttpClientFactory 可用时，应通过 CreateClient() 创建 HttpClient
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证当注入 IHttpClientFactory 时，DefaultHttpClientProvider 应通过 factory.CreateClient() 创建 HttpClient
    /// 这是 P1-5 的核心: 让所有依赖 IHttpClientProvider 的服务间接受益于 Handler 池化
    /// </summary>
    [Fact]
    public void GetClient_WhenFactoryInjected_ShouldUseFactoryCreateClient()
    {
        // Arrange
        var spyFactory = new SpyHttpClientFactory();

        // Act
        var provider = new DefaultHttpClientProvider(spyFactory);
        var client = provider.GetClient();

        // Assert
        spyFactory.CreateClientCallCount.Should().Be(1,
            "GetClient() 应通过 IHttpClientFactory.CreateClient() 获取 HttpClient，而非直接 new HttpClient()");
        client.Should().NotBeNull("GetClient() 必须返回有效 HttpClient 实例");
    }

    /// <summary>
    /// 验证当注入 IHttpClientFactory 时，GetClient(string name) 应通过 factory.CreateClient(name) 创建命名客户端
    /// </summary>
    [Fact]
    public void GetClient_With_Name_WhenFactoryInjected_ShouldUseFactoryCreateClientWithName()
    {
        // Arrange
        var spyFactory = new SpyHttpClientFactory();

        // Act
        var provider = new DefaultHttpClientProvider(spyFactory);
        _ = provider.GetClient("PolicyClient");

        // Assert
        spyFactory.LastRequestedName.Should().Be("PolicyClient",
            "GetClient(name) 应将 name 传递给 IHttpClientFactory.CreateClient(name)");
        spyFactory.NamedCreateClientCallCount.Should().Be(1,
            "GetClient(name) 应通过 IHttpClientFactory.CreateClient(name) 获取 HttpClient");
    }

    /// <summary>
    /// 验证多次调用 GetClient() 应多次通过 factory 创建 HttpClient
    /// 决策: IHttpClientFactory.CreateClient() 每次返回新 HttpClient 实例（轻量对象），
    ///       但底层 HttpMessageHandler 由 IHttpClientFactory 池化管理
    /// </summary>
    [Fact]
    public void GetClient_WhenFactoryInjected_MultipleCalls_ShouldCreateMultipleClients()
    {
        // Arrange
        var spyFactory = new SpyHttpClientFactory();
        var provider = new DefaultHttpClientProvider(spyFactory);

        // Act
        var client1 = provider.GetClient();
        var client2 = provider.GetClient();

        // Assert
        spyFactory.CreateClientCallCount.Should().BeGreaterThanOrEqualTo(2,
            "每次 GetClient() 应通过 factory 创建 HttpClient，而非缓存共享实例");
        client1.Should().NotBeNull();
        client2.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 红测试 2: IHttpClientFactory 不可用时，fallback 到 new HttpClient()
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证未注入 IHttpClientFactory 时，DefaultHttpClientProvider 应 fallback 到共享 HttpClient 实例
    /// 这是 P1-5 的向后兼容: HttpClientProviderFactory.Create() 在 DI 容器构建前使用
    /// </summary>
    [Fact]
    public void GetClient_WhenFactoryNull_ShouldFallbackToSharedHttpClient()
    {
        // Arrange — 无参构造函数（HttpClientProviderFactory.Create() 路径）
        var provider = new DefaultHttpClientProvider();

        // Act
        var client1 = provider.GetClient();
        var client2 = provider.GetClient();

        // Assert — fallback 路径: 多次调用返回同一共享实例
        client1.Should().BeSameAs(client2,
            "无 IHttpClientFactory 时应 fallback 到共享 HttpClient 实例（向后兼容 HttpClientProviderFactory.Create()）");
    }

    /// <summary>
    /// 验证无参构造函数仍能正常工作（向后兼容 HttpClientProviderFactory.Create()）
    /// </summary>
    [Fact]
    public void DefaultHttpClientProvider_ParameterlessConstructor_ShouldNotThrow()
    {
        // Act
        var provider = new DefaultHttpClientProvider();

        // Assert
        provider.Should().NotBeNull("无参构造函数必须能正常工作 — HttpClientProviderFactory.Create() 依赖此路径");
        var client = provider.GetClient();
        client.Should().NotBeNull("无参构造后 GetClient() 必须返回有效 HttpClient 实例");
    }

    /// <summary>
    /// Spy HttpClientFactory — 记录 CreateClient 调用次数和命名参数
    /// </summary>
    private sealed class SpyHttpClientFactory : IHttpClientFactory
    {
        public int CreateClientCallCount;
        public int NamedCreateClientCallCount;
        public string? LastRequestedName;

        public HttpClient CreateClient(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                CreateClientCallCount++;
            }
            else
            {
                NamedCreateClientCallCount++;
                LastRequestedName = name;
            }
            return new HttpClient();
        }
    }
}
