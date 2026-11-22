namespace Hands.ToolHandlers.Tests;

// JCC11003 豁免: 测试代码中 null-forgiving 运算符常见（Should().NotBeNull() 后访问）
#pragma warning disable JCC11003

/// <summary>
/// SessionToolMap 单元测试 — 验证 session 级别工具生命周期管理（ADR 0152）
/// </summary>
public sealed class SessionToolMapTests {
    private static ToolLifecycleInfo CreateInfo(string name, int pid, TimeSpan? warning = null, TimeSpan? kill = null, string? agentId = null) =>
        new(name, ProcessId: pid, StartTime: DateTime.UtcNow,
            TimeoutDuration: TimeSpan.FromMinutes(10),
            WarningThreshold: warning ?? TimeSpan.FromSeconds(30),
            KillThreshold: kill ?? TimeSpan.FromMinutes(5),
            AgentId: agentId);

    [Fact]
    public void Register_And_Get_Should_Work() {
        var map = new SessionToolMap();
        var info = CreateInfo("bash:ls", 12345, agentId: "agent-1");

        map.Register(info);

        var retrieved = map.Get("bash:ls");
        retrieved.Should().NotBeNull();
        retrieved!.ToolName.Should().Be("bash:ls");
        retrieved.ProcessId.Should().Be(12345);
        retrieved.AgentId.Should().Be("agent-1");
    }

    [Fact]
    public void Unregister_Should_Remove_Tool() {
        var map = new SessionToolMap();
        map.Register(CreateInfo("bash:ls", 12345));

        map.Unregister("bash:ls").Should().BeTrue();
        map.Get("bash:ls").Should().BeNull();
    }

    [Fact]
    public void GetByAgent_Should_Return_Only_Agent_Tools() {
        var map = new SessionToolMap();
        map.Register(CreateInfo("bash:ls", 1, agentId: "agent-1"));
        map.Register(CreateInfo("bash:pwd", 2, agentId: "agent-1"));
        map.Register(CreateInfo("bash:cat", 3, agentId: "agent-2"));

        var agent1Tools = map.GetByAgent("agent-1");
        agent1Tools.Should().HaveCount(2);
        agent1Tools.Select(t => t.ToolName).Should().Contain(["bash:ls", "bash:pwd"]);
    }

    [Fact]
    public void GetAll_Should_Return_All_Tools() {
        var map = new SessionToolMap();
        map.Register(CreateInfo("bash:ls", 1));
        map.Register(CreateInfo("bash:pwd", 2));

        map.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void CheckTimeouts_Should_Return_Empty_When_No_Timeout() {
        var map = new SessionToolMap();
        var startTime = DateTime.UtcNow;
        var info = new ToolLifecycleInfo("bash:ls", 0, startTime,
            TimeoutDuration: TimeSpan.FromMinutes(10),
            WarningThreshold: TimeSpan.FromSeconds(30),
            KillThreshold: TimeSpan.FromMinutes(5));
        map.Register(info);

        var (toWarn, toKill) = map.CheckTimeouts(startTime.AddSeconds(5));

        toWarn.Should().BeEmpty();
        toKill.Should().BeEmpty();
    }

    [Fact]
    public void CheckTimeouts_Should_Return_Warn_When_Past_WarningThreshold() {
        var map = new SessionToolMap();
        var startTime = DateTime.UtcNow;
        var info = new ToolLifecycleInfo("bash:ls", 0, startTime,
            TimeoutDuration: TimeSpan.FromMinutes(10),
            WarningThreshold: TimeSpan.FromSeconds(30),
            KillThreshold: TimeSpan.FromMinutes(5));
        map.Register(info);

        var (toWarn, toKill) = map.CheckTimeouts(startTime.AddSeconds(40));

        toWarn.Should().ContainSingle();
        toKill.Should().BeEmpty();
    }

    [Fact]
    public void CheckTimeouts_Should_Return_Kill_When_Past_KillThreshold() {
        var map = new SessionToolMap();
        var startTime = DateTime.UtcNow;
        var info = new ToolLifecycleInfo("bash:ls", 0, startTime,
            TimeoutDuration: TimeSpan.FromMinutes(10),
            WarningThreshold: TimeSpan.FromSeconds(30),
            KillThreshold: TimeSpan.FromMinutes(5));
        map.Register(info);

        var (toWarn, toKill) = map.CheckTimeouts(startTime.AddMinutes(6));

        toKill.Should().ContainSingle();
        toWarn.Should().BeEmpty();
    }

    [Fact]
    public void ToolLifecycleInfo_ShouldWarn_Should_Be_True_After_WarningThreshold() {
        var startTime = DateTime.UtcNow;
        var info = new ToolLifecycleInfo("bash:ls", 0, startTime,
            TimeoutDuration: TimeSpan.FromMinutes(10),
            WarningThreshold: TimeSpan.FromSeconds(30),
            KillThreshold: TimeSpan.FromMinutes(5));

        info.ShouldWarn(startTime.AddSeconds(29)).Should().BeFalse();
        info.ShouldWarn(startTime.AddSeconds(30)).Should().BeTrue();
        info.ShouldWarn(startTime.AddSeconds(31)).Should().BeTrue();
    }

    [Fact]
    public void ToolLifecycleInfo_ShouldKill_Should_Be_True_After_KillThreshold() {
        var startTime = DateTime.UtcNow;
        var info = new ToolLifecycleInfo("bash:ls", 0, startTime,
            TimeoutDuration: TimeSpan.FromMinutes(10),
            WarningThreshold: TimeSpan.FromSeconds(30),
            KillThreshold: TimeSpan.FromMinutes(5));

        info.ShouldKill(startTime.AddMinutes(4)).Should().BeFalse();
        info.ShouldKill(startTime.AddMinutes(5)).Should().BeTrue();
    }

    [Fact]
    public void ToolLifecycleInfo_IsTimedOut_Should_Be_True_After_TimeoutDuration() {
        var startTime = DateTime.UtcNow;
        var info = new ToolLifecycleInfo("bash:ls", 0, startTime,
            TimeoutDuration: TimeSpan.FromMinutes(10),
            WarningThreshold: TimeSpan.FromSeconds(30),
            KillThreshold: TimeSpan.FromMinutes(5));

        info.IsTimedOut(startTime.AddMinutes(9)).Should().BeFalse();
        info.IsTimedOut(startTime.AddMinutes(10)).Should().BeTrue();
    }

    [Fact]
    public void KillByAgent_Should_Remove_Agent_Tools_From_Map() {
        var map = new SessionToolMap();
        map.Register(CreateInfo("bash:ls", 99999, agentId: "agent-1"));
        map.Register(CreateInfo("bash:pwd", 99998, agentId: "agent-1"));
        map.Register(CreateInfo("bash:cat", 99997, agentId: "agent-2"));

        map.KillByAgent("agent-1");

        map.GetByAgent("agent-1").Should().BeEmpty();
        map.GetByAgent("agent-2").Should().HaveCount(1);
    }

    [Fact]
    public void KillAll_Should_Clear_Map() {
        var map = new SessionToolMap();
        map.Register(CreateInfo("bash:ls", 99999));
        map.Register(CreateInfo("bash:pwd", 99998));

        map.KillAll();

        map.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void BuildToolName_Should_Prefix_With_Bash_And_Truncate_To_50() {
        SessionToolMap.BuildToolName("ls").Should().Be("bash:ls");
        SessionToolMap.BuildToolName("dotnet build").Should().Be("bash:dotnet build");

        var longCmd = new string('a', 60);
        var name = SessionToolMap.BuildToolName(longCmd);
        name.Should().Be($"bash:{new string('a', 50)}");
        name.Length.Should().Be(55);
    }

    [Fact]
    public void KillByCommand_Should_Return_Zero_When_Not_Registered() {
        var map = new SessionToolMap();
        map.KillByCommand("nonexistent-command").Should().Be(0);
    }

    [Fact]
    public void KillByCommand_Should_Return_Zero_When_Process_Already_Exited() {
        var map = new SessionToolMap();
        map.Register(CreateInfo(SessionToolMap.BuildToolName("test-cmd"), 999999));

        map.KillByCommand("test-cmd").Should().Be(0);
        map.Get(SessionToolMap.BuildToolName("test-cmd")).Should().BeNull();
    }
}
