namespace Abs.Tests.Security;

/// <summary>
/// BashSafeWrapperStripper 终止性测试 — 验证单独包装命令输入不会无限循环。
/// 用 Task.WhenAny + Delay 超时保护,属时序测试。从 BashSafeWrapperStripperTests.cs 迁移。
/// </summary>
[Trait("Category", "Timing")]
public sealed class BashSafeWrapperStripperTerminationTests {

    [Fact]
    public async Task Strip_TimeoutAlone_DoesNotInfiniteLoop() {
        // 验证 ["timeout"] 单独输入(无后续命令)不会无限循环
        // 用 WhenAny + Delay: 若 2s 未完成则判定为无限循环 bug
        var work = Task.Run(() => BashSafeWrapperStripper.StripSafeWrappers(["timeout"]));
        var winner = await Task.WhenAny(work, Task.Delay(2000));
        winner.Should().Be(work, "StripSafeWrappers([\"timeout\"]) 应在有限时间内返回,不应无限循环");
    }

    [Fact]
    public async Task Strip_NohupAlone_DoesNotInfiniteLoop() {
        var work = Task.Run(() => BashSafeWrapperStripper.StripSafeWrappers(["nohup"]));
        var winner = await Task.WhenAny(work, Task.Delay(2000));
        winner.Should().Be(work);
    }

    [Fact]
    public async Task Strip_TimeAlone_DoesNotInfiniteLoop() {
        var work = Task.Run(() => BashSafeWrapperStripper.StripSafeWrappers(["time"]));
        var winner = await Task.WhenAny(work, Task.Delay(2000));
        winner.Should().Be(work);
    }

    [Fact]
    public async Task Strip_NiceAlone_DoesNotInfiniteLoop() {
        var work = Task.Run(() => BashSafeWrapperStripper.StripSafeWrappers(["nice"]));
        var winner = await Task.WhenAny(work, Task.Delay(2000));
        winner.Should().Be(work);
    }

    [Fact]
    public async Task Strip_EnvAlone_DoesNotInfiniteLoop() {
        var work = Task.Run(() => BashSafeWrapperStripper.StripSafeWrappers(["env"]));
        var winner = await Task.WhenAny(work, Task.Delay(2000));
        winner.Should().Be(work);
    }

    [Fact]
    public async Task Strip_StdbufAlone_DoesNotInfiniteLoop() {
        var work = Task.Run(() => BashSafeWrapperStripper.StripSafeWrappers(["stdbuf"]));
        var winner = await Task.WhenAny(work, Task.Delay(2000));
        winner.Should().Be(work);
    }
}
