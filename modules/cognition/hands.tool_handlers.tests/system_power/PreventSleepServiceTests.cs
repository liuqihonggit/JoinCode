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

    [SkippableFact]
    public async Task PreventSleep_Continuous_SetsIsSleepPrevented() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PreventSleepService 依赖 kernel32.dll SetThreadExecutionState");
        var result = await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        result.Should().BeTrue();
        _service.IsSleepPrevented.Should().BeTrue();
    }

    [SkippableFact]
    public async Task PreventSleep_OneTime_ReturnsBooleanWithoutThrowing() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PreventSleepService 依赖 kernel32.dll SetThreadExecutionState");
        var result = await _service.PreventSleepAsync(SleepPreventionType.OneTime).ConfigureAwait(true);

        if (result) {
            _service.IsSleepPrevented.Should().BeTrue();
        }
    }

    [SkippableFact]
    public async Task AllowSleep_RestoresNormalState() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PreventSleepService 依赖 kernel32.dll SetThreadExecutionState");
        await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        var result = await _service.AllowSleepAsync().ConfigureAwait(true);

        result.Should().BeTrue();
        _service.IsSleepPrevented.Should().BeFalse();
    }

    [SkippableFact]
    public async Task AllowSleep_WhenNotPrevented_ReturnsTrue() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PreventSleepService 依赖 kernel32.dll SetThreadExecutionState");
        var result = await _service.AllowSleepAsync().ConfigureAwait(true);

        result.Should().BeTrue();
        _service.IsSleepPrevented.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Dispose_AllowsSleep() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PreventSleepService 依赖 kernel32.dll SetThreadExecutionState");
        await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        await _service.DisposeSafeAsync();

        _service.IsSleepPrevented.Should().BeFalse();
    }

    [SkippableFact]
    public async Task PreventSleep_WhenAlreadyPrevented_ReturnsTrue() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PreventSleepService 依赖 kernel32.dll SetThreadExecutionState");
        await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        var result = await _service.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(true);

        result.Should().BeTrue();
    }
}