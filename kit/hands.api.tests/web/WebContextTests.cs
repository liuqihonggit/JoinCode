namespace Core.Tests.Web;

public sealed class WebContextTests
{
    [Fact]
    public void MetricsProperties_ShouldReturnExpectedValues()
    {
        var context = new WebContext
        {
            Url = "https://example.com"
        };

        context.MetricsPrefix.Should().Be("web.operation");
        context.IsMetricsSuccess.Should().BeFalse();
        context.MetricsDurationMs.Should().BeNull();
        context.BuildMetricsTags().Should().Contain("operation", "fetch");
    }

    [Fact]
    public void MetricsProperties_WithSuccessfulResult_ShouldReturnSuccess()
    {
        var context = new WebContext
        {
            Url = "https://example.com",
            Result = new WebFetchResult(true, "https://example.com")
        };

        context.IsMetricsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        var cached = new WebFetchResult(true, "https://example.com");
        var fetchResult = new WebFetchResult(true, "https://example.com");
        var result = new WebFetchResult(true, "https://example.com");

        var context = new WebContext
        {
            Url = "https://example.com",
            CancellationToken = CancellationToken.None,
            UpgradedUrl = "https://example.com/upgraded",
            CachedResult = cached,
            DomainCheckResult = DomainCheckResult.Allowed,
            Host = "example.com",
            FetchResult = fetchResult,
            ProcessedContent = "markdown",
            ContentType = "text/html",
            ContentBytes = 100,
            Truncated = true,
            PersistedPath = "/tmp/file",
            PersistedSize = 50,
            Result = result
        };

        context.Url.Should().Be("https://example.com");
        context.UpgradedUrl.Should().Be("https://example.com/upgraded");
        context.CachedResult.Should().BeSameAs(cached);
        context.DomainCheckResult.Should().Be(DomainCheckResult.Allowed);
        context.Host.Should().Be("example.com");
        context.FetchResult.Should().BeSameAs(fetchResult);
        context.ProcessedContent.Should().Be("markdown");
        context.ContentType.Should().Be("text/html");
        context.ContentBytes.Should().Be(100);
        context.Truncated.Should().BeTrue();
        context.PersistedPath.Should().Be("/tmp/file");
        context.PersistedSize.Should().Be(50);
        context.Result.Should().BeSameAs(result);
    }
}
