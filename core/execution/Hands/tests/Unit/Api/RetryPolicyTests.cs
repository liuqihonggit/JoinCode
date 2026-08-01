namespace Hands.Tests.Api;

public sealed class RetryPolicyTests
{
    private static RetryPolicyOptions FastOptions(int maxRetryCount = 2) => new()
    {
        MaxRetryCount = maxRetryCount,
        InitialDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero,
        EnableJitter = false,
        BackoffMultiplier = 1.0
    };

    [Fact]
    public void DefaultOptions_AreExpectedValues()
    {
        var defaults = RetryPolicyOptions.Default;

        defaults.MaxRetryCount.Should().Be(3);
        defaults.InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
        defaults.MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
        defaults.BackoffMultiplier.Should().Be(2.0);
        defaults.EnableJitter.Should().BeTrue();
        defaults.JitterFactor.Should().Be(0.1);
        defaults.RetryableStatusCodes.Should().BeEquivalentTo(new[] { 408, 429, 500, 502, 503, 504 });
        defaults.RetryableExceptions.Should().BeEquivalentTo(new[] { typeof(HttpRequestException), typeof(TaskCanceledException), typeof(TimeoutException), typeof(IOException) });
    }

    [Fact]
    public void AggressiveOptions_HigherRetryCountAndJitter()
    {
        var options = RetryPolicyOptions.Aggressive;

        options.MaxRetryCount.Should().Be(5);
        options.InitialDelay.Should().Be(TimeSpan.FromMilliseconds(500));
        options.MaxDelay.Should().Be(TimeSpan.FromSeconds(60));
        options.JitterFactor.Should().Be(0.2);
    }

    [Fact]
    public void ConservativeOptions_LowerRetryCountAndJitter()
    {
        var options = RetryPolicyOptions.Conservative;

        options.MaxRetryCount.Should().Be(2);
        options.InitialDelay.Should().Be(TimeSpan.FromSeconds(2));
        options.MaxDelay.Should().Be(TimeSpan.FromSeconds(10));
        options.JitterFactor.Should().Be(0.05);
    }

    [Fact]
    public void Constructor_WithNullOptions_UsesDefaults()
    {
        var policy = new RetryPolicy(null);

        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_SucceedsFirstTime_ReturnsResult()
    {
        var policy = new RetryPolicy(FastOptions());

        var result = await policy.ExecuteAsync(_ => Task.FromResult(42)).ConfigureAwait(true);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_NonRetryableException_PropagatesImmediately()
    {
        var policy = new RetryPolicy(FastOptions(3));
        var attempts = 0;

        var act = async () => await policy.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException("fail");
        }).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_RetryableException_EventuallySucceeds()
    {
        var policy = new RetryPolicy(FastOptions(3));
        var attempts = 0;

        var result = await policy.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new HttpRequestException("network");
            }

            return Task.FromResult(42);
        }).ConfigureAwait(true);

        result.Should().Be(42);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_RetryExhausted_ThrowsRetryExhaustedException()
    {
        var policy = new RetryPolicy(FastOptions(1));

        var act = async () => await policy.ExecuteAsync(_ => throw new HttpRequestException("network")).ConfigureAwait(true);

        await act.Should().ThrowAsync<RetryExhaustedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteAsync_CustomPredicate_RetriesOnMatchedException()
    {
        var policy = new RetryPolicy(FastOptions(2));
        var attempts = 0;

        var result = await policy.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts < 2)
                {
                    throw new InvalidOperationException("retry me");
                }

                return Task.FromResult(42);
            },
            ex => ex is InvalidOperationException io && io.Message == "retry me").ConfigureAwait(true);

        result.Should().Be(42);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsRetrying()
    {
        var policy = new RetryPolicy(FastOptions(10));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await policy.ExecuteAsync(
            _ => throw new HttpRequestException("network"),
            cancellationToken: cts.Token).ConfigureAwait(true);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteAsync_VoidOverload_Works()
    {
        var policy = new RetryPolicy(FastOptions());
        var executed = false;

        await policy.ExecuteAsync(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_OnRetryCallback_Invoked()
    {
        var policy = new RetryPolicy(FastOptions(1));
        var callbacks = 0;

        var act = async () => await policy.ExecuteAsync(
            _ => throw new HttpRequestException("network"),
            onRetry: (attempt, delay, ex) => callbacks++,
            cancellationToken: default).ConfigureAwait(true);

        await act.Should().ThrowAsync<RetryExhaustedException>().ConfigureAwait(true);
        callbacks.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ApiExceptionRetryable_Retries()
    {
        var policy = new RetryPolicy(FastOptions(1));
        var attempts = 0;

        var result = await policy.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new ServerErrorException("/api/test", 503);
            }

            return Task.FromResult(42);
        }).ConfigureAwait(true);

        result.Should().Be(42);
        attempts.Should().Be(2);
    }

    [Fact]
    public void RetryPolicyOptions_FromApiSettings_MapsCorrectly()
    {
        var settings = new ApiSettings
        {
            MaxRetryCount = 7,
            InitialDelayMs = 250,
            MaxDelayMs = 5000,
            BackoffMultiplier = 1.5,
            EnableJitter = false,
            JitterFactor = 0.25
        };
        var options = new RetryPolicyOptions(Options.Create(settings));

        options.MaxRetryCount.Should().Be(7);
        options.InitialDelay.Should().Be(TimeSpan.FromMilliseconds(250));
        options.MaxDelay.Should().Be(TimeSpan.FromMilliseconds(5000));
        options.BackoffMultiplier.Should().Be(1.5);
        options.EnableJitter.Should().BeFalse();
        options.JitterFactor.Should().Be(0.25);
    }
}
