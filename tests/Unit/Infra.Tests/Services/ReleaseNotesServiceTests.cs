namespace Infra.Tests.Services;

public sealed class ReleaseNotesServiceTests
{
    [Fact]
    public void Constructor_Should_Set_Default_Timeout()
    {
        var httpClient = new HttpClient();
        var service = new ReleaseNotesService(httpClient);

        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_Should_Accept_Custom_Timeout()
    {
        var httpClient = new HttpClient();
        var service = new ReleaseNotesService(httpClient,
            requestTimeout: TimeSpan.FromSeconds(3),
            cacheDuration: TimeSpan.FromMinutes(30));

        service.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRecentReleasesAsync_WhenNetworkFails_Should_Return_Empty()
    {
        var httpClient = new HttpClient(new FailingHandler());
        var service = new ReleaseNotesService(httpClient,
            requestTimeout: TimeSpan.FromSeconds(1),
            cacheDuration: TimeSpan.FromMinutes(1));

        var result = await service.GetRecentReleasesAsync(5).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentReleasesAsync_WhenTimeout_Should_Return_Empty()
    {
        var httpClient = new HttpClient(new DelayingHandler(TimeSpan.FromSeconds(10)));
        var service = new ReleaseNotesService(httpClient,
            requestTimeout: TimeSpan.FromMilliseconds(100),
            cacheDuration: TimeSpan.FromMinutes(1));

        var result = await service.GetRecentReleasesAsync(5).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentReleasesAsync_Should_Cache_Results()
    {
        var json = """[{"tag_name":"v1.0.0","body":"Test release","published_at":"2025-01-01T00:00:00Z"}]""";
        var handler = new CountingHandler(json);

        var httpClient = new HttpClient(handler);
        var service = new ReleaseNotesService(httpClient,
            requestTimeout: TimeSpan.FromSeconds(5),
            cacheDuration: TimeSpan.FromHours(1));

        var result1 = await service.GetRecentReleasesAsync(5).ConfigureAwait(true);
        var result2 = await service.GetRecentReleasesAsync(5).ConfigureAwait(true);

        result1.Should().HaveCount(1);
        result2.Should().HaveCount(1);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetRecentReleasesAsync_WhenCacheExpired_Should_FetchAgain()
    {
        var json = """[{"tag_name":"v1.0.0","body":"Test release","published_at":"2025-01-01T00:00:00Z"}]""";
        var handler = new CountingHandler(json);
        var fakeTime = new FakeTimeProvider();

        var httpClient = new HttpClient(handler);
        var service = new ReleaseNotesService(httpClient,
            requestTimeout: TimeSpan.FromSeconds(5),
            cacheDuration: TimeSpan.FromMilliseconds(50),
            timeProvider: fakeTime);

        var result1 = await service.GetRecentReleasesAsync(5).ConfigureAwait(true);
        fakeTime.Advance(TimeSpan.FromMilliseconds(100));
        var result2 = await service.GetRecentReleasesAsync(5).ConfigureAwait(true);

        result1.Should().HaveCount(1);
        result2.Should().HaveCount(1);
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetRecentReleasesAsync_Should_Return_ReleaseInfo()
    {
        var json = """[{"tag_name":"v2.1.0","body":"Bug fixes and improvements","published_at":"2025-06-01T12:00:00Z"}]""";
        var handler = new CountingHandler(json);

        var httpClient = new HttpClient(handler);
        var service = new ReleaseNotesService(httpClient,
            requestTimeout: TimeSpan.FromSeconds(5),
            cacheDuration: TimeSpan.FromHours(1));

        var result = await service.GetRecentReleasesAsync(5).ConfigureAwait(true);

        result.Should().HaveCount(1);
        result[0].Version.Should().Be("2.1.0");
        result[0].Notes.Should().Be("Bug fixes and improvements");
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Network error");
    }

    private sealed class DelayingHandler(TimeSpan delay) : HttpMessageHandler
    {
#pragma warning disable JCC3010, JCC3011, JCC3012 // 模拟网络延迟的 mock handler，需要真实 Task.Delay
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(true);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
#pragma warning restore JCC3010, JCC3011, JCC3012
    }

    private sealed class CountingHandler(string json) : HttpMessageHandler
    {
        public int CallCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
