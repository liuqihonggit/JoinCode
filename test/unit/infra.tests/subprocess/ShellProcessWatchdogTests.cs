using Infrastructure.Shell;

namespace Infra.Tests.Subprocess;

public sealed class ShellProcessWatchdogTests
{
    [Fact]
    public async Task NotifySystemResumed_DeadProcess_TriggersCallback()
    {
        using var watchdog = new ShellProcessWatchdog();
        var tcs = new TaskCompletionSource<int>();

        watchdog.Register(-99999, pid => tcs.TrySetResult(pid));
        watchdog.NotifySystemResumed();

        var pid = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        pid.Should().Be(-99999);

        watchdog.Dispose();
    }

    [Fact]
    public async Task Unregister_PreventsCallback()
    {
        using var watchdog = new ShellProcessWatchdog();
        var triggered = false;

        watchdog.Register(-99998, _ => triggered = true);
        watchdog.Unregister(-99998);
        watchdog.NotifySystemResumed();

        await Task.Delay(4000);

        triggered.Should().BeFalse();
    }
}
