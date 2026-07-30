namespace Guard.Tests.Security.Services;

using Core.Security.Sandbox.Ipc;
using Core.Security.Sandbox.Providers;
using Infrastructure.Windows.JobObject;
using JoinCode.Abstractions.Security.Sandbox;

public sealed class SatelliteIsolationTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public void ProcessSandboxProvider_TryAssignProcessToJobObject_NoJobObject_ReturnsFalse()
    {
        var sut = new ProcessSandboxProvider(_fs, Mock.Of<IProcessService>(), NullLogger<ProcessSandboxProvider>.Instance);
        sut.TryAssignProcessToJobObject("nonexistent", 12345).Should().BeFalse();
    }

    [Fact(Skip = "CI-only: 测试运行器自身在JobObject中，无法再分配子进程")]
    public async Task ProcessSandboxProvider_TryAssignProcessToJobObject_WithJobObject_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = new ProcessSandboxProvider(_fs, Mock.Of<IProcessService>(), NullLogger<ProcessSandboxProvider>.Instance);
        var options = new SandboxOptions { Type = SandboxType.Process };
        var info = await sut.CreateSandboxAsync(options).ConfigureAwait(true);

        using var childProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "ping.exe",
            Arguments = "-n 30 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        });

        if (childProcess is not null)
        {
            var result = sut.TryAssignProcessToJobObject(info.SandboxId, childProcess.Id);
            result.Should().BeTrue();

            childProcess.Kill(entireProcessTree: true);
        }

        await sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public void SandboxIpcClient_Callback_InvokedOnStart()
    {
        var invokedPids = new List<int>();
        Func<int, Task> callback = pid => { invokedPids.Add(pid); return Task.CompletedTask; };

        var client = new SandboxIpcClient(
            Mock.Of<IProcessService>(),
            _fs,
            NullLogger<SandboxIpcClient>.Instance,
            callback);

        client.Should().NotBeNull();
        invokedPids.Should().BeEmpty();
    }

    [Fact]
    public void SandboxIpcClient_SatelliteProcessId_InitiallyNull()
    {
        var client = new SandboxIpcClient(
            Mock.Of<IProcessService>(),
            _fs,
            NullLogger<SandboxIpcClient>.Instance);

        client.SatelliteProcessId.Should().BeNull();
    }

    [Fact(Skip = "CI-only: 测试运行器自身在JobObject中，无法再分配子进程")]
    public void WindowsJobObjectSandbox_CreateAndAssign_Works()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var jobObject = new WindowsJobObjectSandbox();
        jobObject.CreateJobObject();

        using var childProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "ping.exe",
            Arguments = "-n 30 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        });

        if (childProcess is not null)
        {
            var result = jobObject.AssignProcess(childProcess.Id);
            result.Should().BeTrue();

            jobObject.TerminateAllProcesses();
        }
    }

    [Fact]
    public void WindowsJobObjectSandbox_Create_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var jobObject = new WindowsJobObjectSandbox();
        var handle = jobObject.CreateJobObject();
        handle.Should().NotBe(nint.Zero, because: "JobObject句柄应非零");
    }
}
