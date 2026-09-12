namespace Hands.Tests.Integration;

public sealed class UpgradeServiceTests
{
    private readonly FakeHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly UpgradeService _service;

    public UpgradeServiceTests()
    {
        _handler = new FakeHttpMessageHandler();
        _httpClient = new HttpClient(_handler);
        _service = new UpgradeService(_httpClient, TestFileSystem.Current);
    }

    [Fact]
    public void GetCurrentVersion_ReturnsAssemblyVersion()
    {
        var version = _service.GetCurrentVersion();

        version.Should().NotBeNull();
        version.Major.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetLatestVersionAsync_ParsesVersionFromTagName()
    {
        _handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"v1.2.3"}""")
        });

        var version = await _service.GetLatestVersionAsync().ConfigureAwait(true);

        version.Should().Be(new Version(1, 2, 3));
    }

    [Fact]
    public async Task GetLatestVersionAsync_WithoutPrefix_ParsesVersion()
    {
        _handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"2.0.0"}""")
        });

        var version = await _service.GetLatestVersionAsync().ConfigureAwait(true);

        version.Should().Be(new Version(2, 0, 0));
    }

    [Fact]
    public async Task GetLatestVersionAsync_InvalidVersion_ReturnsNull()
    {
        _handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"not-a-version"}""")
        });

        var version = await _service.GetLatestVersionAsync().ConfigureAwait(true);

        version.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestVersionAsync_HttpError_ReturnsNull()
    {
        _handler.SetResponse(_ => throw new HttpRequestException("network"));

        var version = await _service.GetLatestVersionAsync().ConfigureAwait(true);

        version.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestVersionAsync_CachesResult()
    {
        var callCount = 0;
        _handler.SetResponse(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v1.0.0"}""")
            };
        });

        var first = await _service.GetLatestVersionAsync().ConfigureAwait(true);
        var second = await _service.GetLatestVersionAsync().ConfigureAwait(true);

        first.Should().Be(second);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task IsUpdateAvailableAsync_LatestGreaterThanCurrent_ReturnsTrue()
    {
        _handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"v999.0.0"}""")
        });

        var result = await _service.IsUpdateAvailableAsync().ConfigureAwait(true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsUpdateAvailableAsync_LatestNull_ReturnsFalse()
    {
        _handler.SetResponse(_ => throw new HttpRequestException("network"));

        var result = await _service.IsUpdateAvailableAsync().ConfigureAwait(true);

        result.Should().BeFalse();
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private Func<HttpRequestMessage, HttpResponseMessage>? _responseFactory;

        public void SetResponse(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _responseFactory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responseFactory?.Invoke(request)
                ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            return Task.FromResult(response);
        }
    }
}
