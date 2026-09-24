namespace Core.Tests.Services.SystemPower;

public sealed class PreventSleepServiceTests : IAsyncDisposable {
    private readonly PreventSleepService _service;
    private bool _disposed;

    public PreventSleepServiceTests() {
        _service = new PreventSleepService();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;
        await _service.DisposeSafeAsync();
    }

    [Fact]
    public async Task PreventSleep_Continuous_SetsIsSleepPrevented() {
        var result = await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        result.Should().BeTrue();
        _service.IsSleepPrevented.Should().BeTrue();
    }

    [Fact]
    public async Task PreventSleep_OneTime_ReturnsBooleanWithoutThrowing() {
        var result = await _service.PreventSleepAsync(SleepPreventionType.OneTime).ConfigureAwait(true);

        if (result) {
            _service.IsSleepPrevented.Should().BeTrue();
        }
    }

    [Fact]
    public async Task AllowSleep_RestoresNormalState() {
        await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        var result = await _service.AllowSleepAsync().ConfigureAwait(true);

        result.Should().BeTrue();
        _service.IsSleepPrevented.Should().BeFalse();
    }

    [Fact]
    public async Task AllowSleep_WhenNotPrevented_ReturnsTrue() {
        var result = await _service.AllowSleepAsync().ConfigureAwait(true);

        result.Should().BeTrue();
        _service.IsSleepPrevented.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_AllowsSleep() {
        await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        await _service.DisposeSafeAsync();

        _service.IsSleepPrevented.Should().BeFalse();
    }

    [Fact]
    public async Task PreventSleep_WhenAlreadyPrevented_ReturnsTrue() {
        await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        var result = await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        result.Should().BeTrue();
    }
}