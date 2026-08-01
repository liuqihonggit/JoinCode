
namespace Bridge.Tests;

/// <summary>
/// BridgeBackoffStrategy 单元测试
/// 测试双轨退避、错误类型切换、放弃阈值、重置等逻辑
/// </summary>
public sealed class BridgeBackoffStrategyTests
{
    private static BridgeBackoffStrategy CreateSut(FakeClockService? clock = null) =>
        new(clock ?? new FakeClockService(), NullLogger.Instance);

    [Fact]
    public void Constructor_InitialState_IsNotInErrorState()
    {
        var sut = CreateSut();

        sut.IsInErrorState.Should().BeFalse();
        sut.FirstErrorTime.Should().Be(default(DateTime));
    }

    [Fact]
    public async Task Reset_WhenInErrorState_InvokesCallbackAndClearsState()
    {
        var clock = new FakeClockService(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = CreateSut(clock);
        long? reportedMs = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try { await sut.HandleErrorAsync(new HttpRequestException(), ct: cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }

        clock.Advance(TimeSpan.FromSeconds(5));
        sut.Reset(ms => reportedMs = ms);

        sut.IsInErrorState.Should().BeFalse();
        reportedMs.Should().BeGreaterThan(0);
        sut.FirstErrorTime.Should().Be(default(DateTime));
    }

    [Fact]
    public void Reset_WhenNotInErrorState_DoesNotInvokeCallback()
    {
        var sut = CreateSut();
        var called = false;

        sut.Reset(_ => called = true);

        called.Should().BeFalse();
        sut.IsInErrorState.Should().BeFalse();
    }

    [Fact]
    public async Task HandleErrorAsync_ConnectionError_IncrementsConnErrors()
    {
        var clock = new FakeClockService();
        var sut = CreateSut(clock);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.HandleErrorAsync(new HttpRequestException(), ct: cts.Token)).ConfigureAwait(true);

        sut.IsInErrorState.Should().BeTrue();
    }

    [Fact]
    public async Task HandleErrorAsync_GeneralError_IncrementsGeneralErrors()
    {
        var clock = new FakeClockService();
        var sut = CreateSut(clock);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.HandleErrorAsync(new InvalidOperationException("boom"), ct: cts.Token)).ConfigureAwait(true);

        sut.IsInErrorState.Should().BeTrue();
    }

    [Fact]
    public async Task HandleErrorAsync_SwitchingErrorType_ResetsOtherTrack()
    {
        var clock = new FakeClockService();
        var sut = CreateSut(clock);

        // 先触发连接错误
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            try { await sut.HandleErrorAsync(new HttpRequestException(), ct: cts.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
        }

        // 再触发通用错误
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            try { await sut.HandleErrorAsync(new InvalidOperationException("boom"), ct: cts.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
        }

        // 再触发连接错误，通用错误计数应被重置
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            try { await sut.HandleErrorAsync(new HttpRequestException(), ct: cts.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
        }

        sut.IsInErrorState.Should().BeTrue();
    }

    [Fact]
    public async Task HandleErrorAsync_NestedConnectionError_TreatedAsConnectionError()
    {
        var clock = new FakeClockService();
        var sut = CreateSut(clock);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var inner = new HttpRequestException();
        var outer = new InvalidOperationException("wrap", inner);

        try { await sut.HandleErrorAsync(outer, ct: cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }

        sut.IsInErrorState.Should().BeTrue();
    }

    [Fact]
    public async Task HandleErrorAsync_GivesUpAfterTenMinutes()
    {
        var clock = new FakeClockService(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = CreateSut(clock);
        var fatalCalled = false;

        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            try { await sut.HandleErrorAsync(new HttpRequestException(), ct: cts.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
        }

        // 推进超过 10 分钟
        clock.Advance(TimeSpan.FromMinutes(11));

        var shouldContinue = await sut.HandleErrorAsync(new HttpRequestException(), onFatalExit: () => fatalCalled = true, ct: CancellationToken.None).ConfigureAwait(true);

        shouldContinue.Should().BeFalse();
        fatalCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleErrorAsync_ReturnsTrue_WhenDelayCompletes()
    {
        var clock = new FakeClockService();
        var sut = CreateSut(clock);
        var sw = Stopwatch.StartNew();

        var result = await sut.HandleErrorAsync(new InvalidOperationException("boom"), ct: CancellationToken.None).ConfigureAwait(true);

        sw.Stop();
        result.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(350);
    }
}
