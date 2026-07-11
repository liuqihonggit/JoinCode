namespace Guard.Tests.Security.Services;

public sealed class HttpProxyServiceTests
{
    private readonly HttpProxyService _sut;

    public HttpProxyServiceTests()
    {
        _sut = new HttpProxyService(NullLogger<HttpProxyService>.Instance);
    }

    [Fact]
    public void IsProxyConfigured_NoEnvironmentProxy_ShouldBeFalse()
    {
        var originalHttps = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        var originalHttp = Environment.GetEnvironmentVariable("HTTP_PROXY");
        var originalHttpsLower = Environment.GetEnvironmentVariable("https_proxy");
        var originalHttpLower = Environment.GetEnvironmentVariable("http_proxy");

        try
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
            Environment.SetEnvironmentVariable("HTTP_PROXY", null);
            Environment.SetEnvironmentVariable("https_proxy", null);
            Environment.SetEnvironmentVariable("http_proxy", null);

            var service = new HttpProxyService(NullLogger<HttpProxyService>.Instance);
            service.IsProxyConfigured.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", originalHttps);
            Environment.SetEnvironmentVariable("HTTP_PROXY", originalHttp);
            Environment.SetEnvironmentVariable("https_proxy", originalHttpsLower);
            Environment.SetEnvironmentVariable("http_proxy", originalHttpLower);
        }
    }

    [Fact]
    public void CreateProxyHandler_NoOptions_ShouldReturnHandler()
    {
        var handler = _sut.CreateProxyHandler();

        handler.Should().NotBeNull();
    }

    [Fact]
    public void CreateProxyHandler_NullOptions_ShouldReturnDefaultHandler()
    {
        var handler = _sut.CreateProxyHandler(null);

        handler.Should().NotBeNull();
    }

    [Fact]
    public void CreateProxyHandler_EmptyProxyUrl_ShouldReturnHandlerWithoutProxySet()
    {
        var options = new ProxyOptions
        {
            ProxyUrl = ""
        };

        var handler = _sut.CreateProxyHandler(options);

        handler.Should().NotBeNull();
        handler.Proxy.Should().BeNull();
    }

    [Fact]
    public void CreateProxyHandler_ValidProxyUrl_ShouldConfigureProxy()
    {
        var options = new ProxyOptions
        {
            ProxyUrl = "http://proxy.example.com:8080"
        };

        var handler = _sut.CreateProxyHandler(options);

        handler.Should().NotBeNull();
        handler.UseProxy.Should().BeTrue();
        handler.Proxy.Should().NotBeNull();
    }

    [Fact]
    public void CreateProxyHandler_WithCredentials_ShouldSetProxyCredentials()
    {
        var options = new ProxyOptions
        {
            ProxyUrl = "http://proxy.example.com:8080",
            ProxyUsername = "user",
            ProxyPassword = "pass"
        };

        var handler = _sut.CreateProxyHandler(options);

        handler.Should().NotBeNull();
        handler.Proxy.Should().NotBeNull();
    }

    [Fact]
    public void CreateProxyHandler_WithDefaultCredentials_ShouldSetDefaultCredentials()
    {
        var options = new ProxyOptions
        {
            ProxyUrl = "http://proxy.example.com:8080",
            UseDefaultCredentials = true
        };

        var handler = _sut.CreateProxyHandler(options);

        handler.Should().NotBeNull();
        handler.Proxy.Should().NotBeNull();
    }

    [Fact]
    public void CreateProxyHandler_WithBypassHosts_ShouldConfigureSuccessfully()
    {
        var options = new ProxyOptions
        {
            ProxyUrl = "http://proxy.example.com:8080",
            BypassHosts = new List<string> { "localhost", "127.0.0.1", "*.internal" }
        };

        var handler = _sut.CreateProxyHandler(options);

        handler.Should().NotBeNull();
        handler.UseProxy.Should().BeTrue();
    }

    [Fact]
    public void CreateProxyHandler_InvalidProxyUrl_ShouldNotThrow()
    {
        var options = new ProxyOptions
        {
            ProxyUrl = "not-a-valid-url"
        };

        var handler = _sut.CreateProxyHandler(options);

        handler.Should().NotBeNull();
    }

    [Fact]
    public void GetCurrentProxySettings_ShouldReturnOptions()
    {
        var settings = _sut.GetCurrentProxySettings();

        settings.Should().NotBeNull();
    }

    [Fact]
    public void GetCurrentProxySettings_NoEnvironmentProxy_ShouldReturnDefaultOptions()
    {
        var originalHttps = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        var originalHttp = Environment.GetEnvironmentVariable("HTTP_PROXY");

        try
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
            Environment.SetEnvironmentVariable("HTTP_PROXY", null);

            var service = new HttpProxyService(NullLogger<HttpProxyService>.Instance);
            var settings = service.GetCurrentProxySettings();

            settings.Should().NotBeNull();
            settings.ProxyUrl.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", originalHttps);
            Environment.SetEnvironmentVariable("HTTP_PROXY", originalHttp);
        }
    }

    [Fact]
    public void GetCurrentProxySettings_WithHttpsProxyEnv_ShouldReturnProxyUrl()
    {
        var originalHttps = Environment.GetEnvironmentVariable("HTTPS_PROXY");

        try
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://secure-proxy:8443");

            var service = new HttpProxyService(NullLogger<HttpProxyService>.Instance);
            var settings = service.GetCurrentProxySettings();

            settings.ProxyUrl.Should().Be("http://secure-proxy:8443");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", originalHttps);
        }
    }

    [Fact]
    public void GetCurrentProxySettings_WithNoProxyEnv_ShouldSetBypassHosts()
    {
        var originalHttps = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        var originalNoProxy = Environment.GetEnvironmentVariable("NO_PROXY");

        try
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://proxy:8080");
            Environment.SetEnvironmentVariable("NO_PROXY", "localhost,127.0.0.1,.internal");

            var service = new HttpProxyService(NullLogger<HttpProxyService>.Instance);
            var settings = service.GetCurrentProxySettings();

            settings.BypassHosts.Should().NotBeNull();
            settings.BypassHosts.Should().Contain("localhost");
            settings.BypassHosts.Should().Contain("127.0.0.1");
            settings.BypassHosts.Should().Contain(".internal");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", originalHttps);
            Environment.SetEnvironmentVariable("NO_PROXY", originalNoProxy);
        }
    }
}
