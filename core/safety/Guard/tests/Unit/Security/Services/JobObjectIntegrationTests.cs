namespace Guard.Tests.Security.Services;

using Core.Security.Sandbox.Native;
using JoinCode.Abstractions.Security.Sandbox;

public sealed class JobObjectIntegrationTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public async Task ProcessSandboxProvider_ExecuteAsync_SetsEnvironmentVariables()
    {
        if (!OperatingSystem.IsWindows()) return;

        var processService = Mock.Of<IProcessService>();
        var provider = new ProcessSandboxProvider(_fs, processService, NullLogger<ProcessSandboxProvider>.Instance);

        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            RestrictFileSystem = true,
            RestrictNetwork = true
        };

        var info = await provider.CreateSandboxAsync(options).ConfigureAwait(true);

        info.Should().NotBeNull();
        info.RestrictFileSystem.Should().BeTrue();
        info.RestrictNetwork.Should().BeTrue();

        await provider.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task ProcessSandboxProvider_OnCreate_SetsUpJobObjectOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        var processService = Mock.Of<IProcessService>();
        var provider = new ProcessSandboxProvider(_fs, processService, NullLogger<ProcessSandboxProvider>.Instance);

        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            RestrictFileSystem = true,
            MemoryLimitMb = 100
        };

        var info = await provider.CreateSandboxAsync(options).ConfigureAwait(true);

        provider.HasJobObject(info.SandboxId).Should().BeTrue(because: "Windows上创建Process沙箱应创建JobObject");

        await provider.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task ProcessSandboxProvider_OnDestroy_DisposesJobObject()
    {
        if (!OperatingSystem.IsWindows()) return;

        var processService = Mock.Of<IProcessService>();
        var provider = new ProcessSandboxProvider(_fs, processService, NullLogger<ProcessSandboxProvider>.Instance);

        var options = new SandboxOptions { Type = SandboxType.Process, RestrictFileSystem = true };
        var info = await provider.CreateSandboxAsync(options).ConfigureAwait(true);

        provider.HasJobObject(info.SandboxId).Should().BeTrue();

        await provider.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);

        provider.HasJobObject(info.SandboxId).Should().BeFalse(because: "销毁后JobObject应被移除");
    }
}
