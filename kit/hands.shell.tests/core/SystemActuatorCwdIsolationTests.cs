namespace Hands.Shell.Tests;

/// <summary>
/// SystemActuatorBase cwd 隔离单元测试 — 验证 ResolveWorkingDirectoryCore 读取 SubAgentContext.GetEffectiveCwd
/// 缺陷修复验证(ADR 0115): shell 链路应优先使用 AsyncLocal CwdOverride 而非进程级 cwd
/// </summary>
public sealed class SystemActuatorCwdIsolationTests
{
    [Fact]
    public void ResolveWorkingDirectoryCore_NoCwdOverride_ReturnsProcessCwd()
    {
        var fs = new Mock<IFileSystem>();
        var processCwd = "/tmp/process-cwd";
        fs.Setup(x => x.GetCurrentDirectory()).Returns(processCwd);

        SubAgentContext.Current.Should().BeNull();
        var result = SystemActuatorBase.ResolveWorkingDirectoryCore(null, fs.Object, null, false);

        result.Should().Be(processCwd);
    }

    [Fact]
    public void ResolveWorkingDirectoryCore_WithCwdOverride_ReturnsOverride()
    {
        var fs = new Mock<IFileSystem>();
        var processCwd = "/tmp/process-cwd";
        var overrideCwd = "/tmp/worktree-override";
        fs.Setup(x => x.GetCurrentDirectory()).Returns(processCwd);

        var context = new SubAgentContext
        {
            AgentId = "agent-test",
            Role = AgentRole.Executor,
            Task = "test",
        };

        using (context.EnterScopeWithCwd(overrideCwd))
        {
            var result = SystemActuatorBase.ResolveWorkingDirectoryCore(null, fs.Object, null, false);
            result.Should().Be(overrideCwd, "应优先使用 SubAgentContext.CwdOverride 而非进程级 cwd");
        }
    }

    [Fact]
    public void ResolveWorkingDirectoryCore_ExplicitWorkingDirectory_OverridesEverything()
    {
        var fs = new Mock<IFileSystem>();
        var processCwd = "/tmp/process-cwd";
        var overrideCwd = "/tmp/worktree-override";
        var explicitCwd = "/tmp/explicit";
        fs.Setup(x => x.GetCurrentDirectory()).Returns(processCwd);

        var context = new SubAgentContext
        {
            AgentId = "agent-test",
            Role = AgentRole.Executor,
            Task = "test",
        };

        using (context.EnterScopeWithCwd(overrideCwd))
        {
            var result = SystemActuatorBase.ResolveWorkingDirectoryCore(explicitCwd, fs.Object, null, false);
            result.Should().Be(Path.GetFullPath(explicitCwd), "显式 workingDirectory 应优先于 CwdOverride 和进程级 cwd");
        }
    }

    [Fact]
    public void ResolveWorkingDirectoryCore_SandboxEnabled_ResolvesPath()
    {
        var fs = new Mock<IFileSystem>();
        var processCwd = "/tmp/process-cwd";
        var sandboxResolved = "/tmp/sandbox-resolved";
        fs.Setup(x => x.GetCurrentDirectory()).Returns(processCwd);

        var sandbox = new Mock<ISandboxManager>();
        sandbox.SetupGet(x => x.IsInSandbox).Returns(true);
        sandbox.Setup(x => x.ResolvePath(processCwd)).Returns(sandboxResolved);

        var result = SystemActuatorBase.ResolveWorkingDirectoryCore(null, fs.Object, sandbox.Object, false);

        result.Should().Be(sandboxResolved);
    }
}
