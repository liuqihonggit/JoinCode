namespace Abs.Tests.Security;

/// <summary>
/// BashSafeWrapperStripper 确定性单元测试 — 覆盖 StripSafeWrappers(public)。
/// 纯数组剥离逻辑,不依赖时序/IO。包装命令: time/nohup/timeout/nice/env/stdbuf。
/// </summary>
public sealed class BashSafeWrapperStripperTests {

    [Fact]
    public void Strip_Time_Prefix_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["time", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_Nohup_Prefix_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["nohup", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_Time_Nohup_Nested_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["time", "nohup", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_TimeoutWithNumericDuration_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["timeout", "5", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_TimeoutWithDurationSuffix_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["timeout", "5s", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_TimeoutWithMinutesSuffix_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["timeout", "10m", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_TimeoutVerboseFlag_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["timeout", "--verbose", "5", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_TimeoutForegroundFlag_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["timeout", "--foreground", "5", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_TimeoutSignalOption_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["timeout", "-s", "TERM", "5", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_NiceWithNFlag_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["nice", "-n", "5", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_NiceAlone_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["nice", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_NiceLegacyForm_Stripped() {
        // nice -5 ls (legacy 形式)
        BashSafeWrapperStripper.StripSafeWrappers(["nice", "-5", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_EnvWithAssignment_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["env", "FOO=bar", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_EnvAlone_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["env", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_EnvWithMultipleAssignments_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["env", "FOO=bar", "BAZ=qux", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_EnvWithUnsetFlag_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["env", "-u", "FOO", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_StdbufWithOutputFlag_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["stdbuf", "-oL", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_StdbufWithLongFlag_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["stdbuf", "--output=L", "ls"]).Should().Equal(["ls"]);
    }

    [Fact]
    public void Strip_NoWrapper_ReturnsAsIs() {
        BashSafeWrapperStripper.StripSafeWrappers(["ls", "-l"]).Should().Equal(["ls", "-l"]);
    }

    [Fact]
    public void Strip_UnknownCommand_ReturnsAsIs() {
        BashSafeWrapperStripper.StripSafeWrappers(["unknown", "arg"]).Should().Equal(["unknown", "arg"]);
    }

    [Fact]
    public void Strip_EmptyArray_ReturnsEmpty() {
        BashSafeWrapperStripper.StripSafeWrappers([]).Should().BeEmpty();
    }

    [Fact]
    public void Strip_PreservesTrailingArgs() {
        BashSafeWrapperStripper.StripSafeWrappers(["time", "ls", "-l", "-a"]).Should().Equal(["ls", "-l", "-a"]);
    }

    [Fact]
    public void Strip_DeepNesting_Stripped() {
        BashSafeWrapperStripper.StripSafeWrappers(["time", "nohup", "env", "FOO=1", "ls"]).Should().Equal(["ls"]);
    }

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

    [Fact]
    public void Strip_TimeoutAlone_ReturnsItself() {
        // 修复后: ["timeout"] 单独 → 返回 ["timeout"](无可剥离的后续命令)
        BashSafeWrapperStripper.StripSafeWrappers(["timeout"]).Should().Equal(["timeout"]);
    }

    [Fact]
    public void Strip_NiceAlone_ReturnsItself() {
        BashSafeWrapperStripper.StripSafeWrappers(["nice"]).Should().Equal(["nice"]);
    }

    [Fact]
    public void Strip_EnvAlone_ReturnsItself() {
        BashSafeWrapperStripper.StripSafeWrappers(["env"]).Should().Equal(["env"]);
    }

    [Fact]
    public void Strip_StdbufAlone_ReturnsItself() {
        BashSafeWrapperStripper.StripSafeWrappers(["stdbuf"]).Should().Equal(["stdbuf"]);
    }
}
