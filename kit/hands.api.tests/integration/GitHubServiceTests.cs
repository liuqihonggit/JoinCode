namespace Hands.Tests.Integration;

public sealed class GitHubServiceTests
{
    private readonly FakeHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IConfigurationService> _configMock;
    private readonly GitHubService _service;

    public GitHubServiceTests()
    {
        _handler = new FakeHttpMessageHandler();
        _httpClient = new HttpClient(_handler);
        _configMock = new Mock<IConfigurationService>();
        _service = new GitHubService(_httpClient, _configMock.Object);
    }

    [Fact]
    public async Task SubscribeAsync_EmptyPrRef_ThrowsArgumentException()
    {
        var act = async () => await _service.SubscribeAsync(" ").ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentException>().Where(ex => ex.Message.Contains("[HND003]")).ConfigureAwait(true);
    }

    [Fact]
    public async Task SubscribeAsync_NewSubscription_AddsToList()
    {
        _configMock.Setup(c => c.GetAsync("github.pr_subscriptions", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var sub = await _service.SubscribeAsync("owner/repo/1").ConfigureAwait(true);

        sub.PrRef.Should().Be("owner/repo/1");
        sub.Events.Should().Be("all");
        var subs = await _service.ListSubscriptionsAsync().ConfigureAwait(true);
        subs.Should().ContainSingle(s => s.PrRef == "owner/repo/1");
    }

    [Fact]
    public async Task SubscribeAsync_ExistingSubscription_UpdatesIt()
    {
        _configMock.Setup(c => c.GetAsync("github.pr_subscriptions", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        await _service.SubscribeAsync("owner/repo/1", "opened").ConfigureAwait(true);

        var sub = await _service.SubscribeAsync("owner/repo/1", "all").ConfigureAwait(true);

        sub.Events.Should().Be("all");
        var subs = await _service.ListSubscriptionsAsync().ConfigureAwait(true);
        subs.Should().HaveCount(1);
    }

    [Fact]
    public async Task UnsubscribeAsync_EmptyPrRef_ThrowsArgumentException()
    {
        var act = async () => await _service.UnsubscribeAsync(" ").ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentException>().Where(ex => ex.Message.Contains("[HND004]")).ConfigureAwait(true);
    }

    [Fact]
    public async Task UnsubscribeAsync_Existing_RemovesSubscription()
    {
        _configMock.Setup(c => c.GetAsync("github.pr_subscriptions", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        await _service.SubscribeAsync("owner/repo/1").ConfigureAwait(true);

        await _service.UnsubscribeAsync("owner/repo/1").ConfigureAwait(true);

        var subs = await _service.ListSubscriptionsAsync().ConfigureAwait(true);
        subs.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSubscriptionsAsync_LoadsFromConfig()
    {
        var saved = JsonSerializer.Serialize(new List<PRSubscription>
        {
            new() { PrRef = "owner/repo/2", Events = "closed", SubscribedAt = DateTime.UtcNow }
        }, GitHubSubscriptionContext.Default.ListPRSubscription);
        _configMock.Setup(c => c.GetAsync("github.pr_subscriptions", It.IsAny<CancellationToken>())).ReturnsAsync(saved);

        var subs = await _service.ListSubscriptionsAsync().ConfigureAwait(true);

        subs.Should().ContainSingle(s => s.PrRef == "owner/repo/2");
    }

    [Fact]
    public async Task ListSubscriptionsAsync_NoConfig_ReturnsEmpty()
    {
        var service = new GitHubService(_httpClient);

        var subs = await service.ListSubscriptionsAsync().ConfigureAwait(true);

        subs.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveSubscriptionsAsync_PersistsToConfig()
    {
        _configMock.Setup(c => c.GetAsync("github.pr_subscriptions", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        await _service.SubscribeAsync("owner/repo/3").ConfigureAwait(true);

        _configMock.Verify(c => c.SetAsync("github.pr_subscriptions", It.Is<string>(s => s.Contains("owner/repo/3")), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
