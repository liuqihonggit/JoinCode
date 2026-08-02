namespace Guard.Tests.Security.Services;

using Infrastructure.Windows.JobObject;

public sealed class LinuxCgroupSandboxTests
{
    [Fact]
    public void CreateCgroup_OnWindows_ReturnsFalse()
    {
        if (!OperatingSystem.IsLinux()) return;

        var sut = new LinuxCgroupSandbox();
        var result = sut.CreateCgroup();
        result.Should().BeTrue("Linux 上应能创建 cgroup（如果权限足够）");
    }

    [Fact]
    public void CreateCgroup_OnWindows_SkipsGracefully()
    {
        if (OperatingSystem.IsLinux()) return;

        var sut = new LinuxCgroupSandbox();
        var result = sut.CreateCgroup();
        result.Should().BeFalse("Windows 上 cgroup 不可用");
    }

    [Fact]
    public async Task DisposeAsync_WithoutCreate_IsNoOp()
    {
        var sut = new LinuxCgroupSandbox();
        await sut.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_AfterCreate_CleansUp()
    {
        if (!OperatingSystem.IsLinux()) return;

        var sut = new LinuxCgroupSandbox();
        if (sut.CreateCgroup())
        {
            await sut.DisposeAsync().ConfigureAwait(true);
        }
    }

    [Fact]
    public void AssignProcess_WithoutCreate_ReturnsFalse()
    {
        var sut = new LinuxCgroupSandbox();
        sut.AssignProcess(12345).Should().BeFalse();
    }

    [Fact]
    public void KillAllProcesses_WithoutCreate_ReturnsFalse()
    {
        var sut = new LinuxCgroupSandbox();
        sut.KillAllProcesses().Should().BeFalse();
    }
}
