namespace Hands.Tests.Api;

public sealed class ApiSettingsTests
{
    [Fact]
    public void Defaults_AreExpectedValues()
    {
        var settings = new ApiSettings();

        settings.BaseUrl.Should().BeEmpty();
        settings.TimeoutSeconds.Should().Be(30);
        settings.MaxRetryCount.Should().Be(3);
        settings.InitialDelayMs.Should().Be(1000);
        settings.MaxDelayMs.Should().Be(30000);
        settings.BackoffMultiplier.Should().Be(2.0);
        settings.EnableJitter.Should().BeTrue();
        settings.JitterFactor.Should().Be(0.1);
        settings.UserAgent.Should().Be("JoinCode/1.0");
        settings.EnableLogging.Should().BeTrue();
        settings.LoggingLevel.Should().Be(ApiLoggingLevel.Basic);
        settings.AuthToken.Should().BeNull();
        settings.AuthScheme.Should().Be("Bearer");
        settings.DefaultHeaders.Should().BeEmpty();
    }

    [Fact]
    public void ToRetryPolicyOptions_MapsValuesCorrectly()
    {
        var settings = new ApiSettings
        {
            MaxRetryCount = 5,
            InitialDelayMs = 500,
            MaxDelayMs = 20000,
            BackoffMultiplier = 3.0,
            EnableJitter = false,
            JitterFactor = 0.5
        };

        var options = settings.ToRetryPolicyOptions();

        options.MaxRetryCount.Should().Be(5);
        options.InitialDelay.Should().Be(TimeSpan.FromMilliseconds(500));
        options.MaxDelay.Should().Be(TimeSpan.FromMilliseconds(20000));
        options.BackoffMultiplier.Should().Be(3.0);
        options.EnableJitter.Should().BeFalse();
        options.JitterFactor.Should().Be(0.5);
    }

    [Fact]
    public void ToApiClientOptions_MapsValuesCorrectly()
    {
        var settings = new ApiSettings
        {
            BaseUrl = "http://localhost:8080",
            TimeoutSeconds = 60,
            DefaultHeaders = new Dictionary<string, string> { ["X-Custom"] = "value" },
            UserAgent = "TestAgent/1.0"
        };

        var options = settings.ToApiClientOptions();

        options.BaseUrl.Should().Be("http://localhost:8080");
        options.Timeout.Should().Be(TimeSpan.FromSeconds(60));
        options.UserAgent.Should().Be("TestAgent/1.0");
        options.DefaultHeaders.Should().ContainKey("X-Custom").WhoseValue.Should().Be("value");
        options.RetryOptions.Should().NotBeNull();
    }

    [Theory]
    [InlineData(ApiLoggingLevel.None, nameof(ApiLoggingOptions.ErrorsOnly))]
    [InlineData(ApiLoggingLevel.Basic, nameof(ApiLoggingOptions.Default))]
    [InlineData(ApiLoggingLevel.Verbose, nameof(ApiLoggingOptions.Verbose))]
    public void ToLoggingOptions_MapsLevelsCorrectly(ApiLoggingLevel level, string expectedPropertyName)
    {
        var settings = new ApiSettings { LoggingLevel = level };

        var options = settings.ToLoggingOptions();

        var expected = typeof(ApiLoggingOptions).GetProperty(expectedPropertyName)!.GetValue(null);
        options.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void ToLoggingOptions_UnknownLevel_FallsBackToDefault()
    {
        var settings = new ApiSettings { LoggingLevel = (ApiLoggingLevel)999 };

        var options = settings.ToLoggingOptions();

        options.Should().BeEquivalentTo(ApiLoggingOptions.Default);
    }
}
