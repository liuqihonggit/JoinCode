namespace JoinCode.Tests.Entry;

public sealed class ProactiveTickSchedulerTests
{
    private readonly FakeProactiveStateService _state = new();
    private readonly TerminalFocusDetector _focus = new();
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    private ProactiveTickScheduler CreateScheduler(TimeSpan? tickInterval = null, TimeSpan? blurredInterval = null)
    {
        return new ProactiveTickScheduler(_state, _focus, tickInterval: tickInterval, blurredTickInterval: blurredInterval, clock: () => _now);
    }

    [Fact]
    public void ShouldTick_InactiveState_ReturnsFalse()
    {
        var scheduler = CreateScheduler();
        _state.SetActive(false);

        Assert.False(scheduler.ShouldTick());
    }

    [Fact]
    public void ShouldTick_ActiveAndNotPaused_ReturnsTrue()
    {
        var scheduler = CreateScheduler();
        _state.SetActive(true);

        Assert.True(scheduler.ShouldTick());
    }

    [Fact]
    public void ShouldTick_Paused_ReturnsFalse()
    {
        var scheduler = CreateScheduler();
        _state.SetActive(true);
        _state.SetPaused(true);

        Assert.False(scheduler.ShouldTick());
    }

    [Fact]
    public void ShouldTick_ContextBlocked_ReturnsFalse()
    {
        var scheduler = CreateScheduler();
        _state.SetActive(true);
        _state.SetContextBlocked(true);

        Assert.False(scheduler.ShouldTick());
    }

    [Fact]
    public void GenerateTick_ReturnsTickContent()
    {
        var scheduler = CreateScheduler();
        _state.SetActive(true);

        var content = scheduler.GenerateTick();

        Assert.NotNull(content);
        Assert.Contains("<tick>", content);
        Assert.Contains("</tick>", content);
    }

    [Fact]
    public void GenerateTick_IncrementsTickCount()
    {
        var scheduler = CreateScheduler(tickInterval: TimeSpan.FromSeconds(1));
        _state.SetActive(true);

        scheduler.GenerateTick();
        _now = _now.AddSeconds(2);
        scheduler.GenerateTick();

        Assert.Equal(2, scheduler.TickCount);
    }

    [Fact]
    public void GenerateTick_SchedulesNextTickAtInterval()
    {
        var interval = TimeSpan.FromSeconds(5);
        var scheduler = CreateScheduler(tickInterval: interval);
        _state.SetActive(true);
        _focus.SetFocused();

        _now = new DateTimeOffset(2026, 9, 2, 14, 30, 0, TimeSpan.Zero);
        scheduler.GenerateTick();

        Assert.Equal(_now + interval, scheduler.NextTickAt);
    }

    [Fact]
    public void GenerateTick_UsesBlurredIntervalWhenNotFocused()
    {
        var focusedInterval = TimeSpan.FromSeconds(5);
        var blurredInterval = TimeSpan.FromSeconds(30);
        var scheduler = CreateScheduler(tickInterval: focusedInterval, blurredInterval: blurredInterval);
        _state.SetActive(true);
        _focus.SetBlurred();

        _now = new DateTimeOffset(2026, 9, 2, 14, 30, 0, TimeSpan.Zero);
        scheduler.GenerateTick();

        Assert.Equal(_now + blurredInterval, scheduler.NextTickAt);
    }

    [Fact]
    public void ShouldTick_BeforeNextTickAt_ReturnsFalse()
    {
        var interval = TimeSpan.FromSeconds(5);
        var scheduler = CreateScheduler(tickInterval: interval);
        _state.SetActive(true);

        _now = new DateTimeOffset(2026, 9, 2, 14, 30, 0, TimeSpan.Zero);
        scheduler.GenerateTick();

        _now = _now.AddSeconds(3);
        Assert.False(scheduler.ShouldTick());
    }

    [Fact]
    public void ShouldTick_AfterNextTickAt_ReturnsTrue()
    {
        var interval = TimeSpan.FromSeconds(5);
        var scheduler = CreateScheduler(tickInterval: interval);
        _state.SetActive(true);

        _now = new DateTimeOffset(2026, 9, 2, 14, 30, 0, TimeSpan.Zero);
        scheduler.GenerateTick();

        _now = _now.AddSeconds(6);
        Assert.True(scheduler.ShouldTick());
    }

    [Fact]
    public void Reset_ClearsNextTickAt()
    {
        var scheduler = CreateScheduler();
        _state.SetActive(true);
        scheduler.GenerateTick();

        scheduler.Reset();

        Assert.Null(scheduler.NextTickAt);
    }

    [Fact]
    public void ScheduleImmediate_SetsNextTickToNow()
    {
        var scheduler = CreateScheduler();
        _now = new DateTimeOffset(2026, 9, 2, 14, 30, 0, TimeSpan.Zero);

        scheduler.ScheduleImmediate();

        Assert.Equal(_now, scheduler.NextTickAt);
    }

    [Fact]
    public void GenerateTick_Inactive_ReturnsNull()
    {
        var scheduler = CreateScheduler();
        _state.SetActive(false);

        Assert.Null(scheduler.GenerateTick());
    }
}

public sealed class TerminalFocusDetectorTests
{
    [Fact]
    public void DefaultState_IsUnknown()
    {
        var detector = new TerminalFocusDetector();
        Assert.Equal(TerminalFocusState.Unknown, detector.State);
    }

    [Fact]
    public void IsFocused_Unknown_ReturnsTrue()
    {
        var detector = new TerminalFocusDetector();
        Assert.True(detector.IsFocused);
    }

    [Fact]
    public void SetFocused_ChangesStateToFocused()
    {
        var detector = new TerminalFocusDetector();
        detector.SetFocused();
        Assert.Equal(TerminalFocusState.Focused, detector.State);
        Assert.True(detector.IsFocused);
    }

    [Fact]
    public void SetBlurred_ChangesStateToBlurred()
    {
        var detector = new TerminalFocusDetector();
        detector.SetBlurred();
        Assert.Equal(TerminalFocusState.Blurred, detector.State);
        Assert.False(detector.IsFocused);
    }

    [Fact]
    public void FocusChanged_EventFiresOnStateChange()
    {
        var detector = new TerminalFocusDetector();
        TerminalFocusState? received = null;
        detector.FocusChanged += (_, state) => received = state;

        detector.SetBlurred();
        Assert.Equal(TerminalFocusState.Blurred, received);

        detector.SetFocused();
        Assert.Equal(TerminalFocusState.Focused, received);
    }

    [Fact]
    public void FocusChanged_NoEventForSameState()
    {
        var detector = new TerminalFocusDetector();
        var count = 0;
        detector.FocusChanged += (_, _) => count++;

        detector.SetFocused();
        detector.SetFocused();

        Assert.Equal(1, count);
    }

    [Fact]
    public void Reset_SetsStateToUnknown()
    {
        var detector = new TerminalFocusDetector();
        detector.SetBlurred();
        detector.Reset();
        Assert.Equal(TerminalFocusState.Unknown, detector.State);
    }
}

internal sealed class FakeProactiveStateService : IProactiveStateService
{
    private bool _active;
    private bool _paused;
    private bool _contextBlocked;

    public bool IsActive => _active;
    public bool IsPaused => _paused;
    public bool IsContextBlocked => _contextBlocked;

    public event EventHandler? StateChanged;

    public void Activate(string? source = null) { _active = true; _paused = false; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void Deactivate() { _active = false; _paused = false; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void Pause() { _paused = true; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void Resume() { _paused = false; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void SetContextBlocked(bool blocked) { _contextBlocked = blocked; StateChanged?.Invoke(this, EventArgs.Empty); }

    internal void SetActive(bool value) { _active = value; StateChanged?.Invoke(this, EventArgs.Empty); }
    internal void SetPaused(bool value) { _paused = value; StateChanged?.Invoke(this, EventArgs.Empty); }
}
