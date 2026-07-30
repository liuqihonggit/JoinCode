namespace Guard.Tests.Security.Services;

using Core.Security.Sandbox.Native;
using JoinCode.Abstractions.Security.Sandbox;

public sealed class ProcessSandboxJobObjectTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public void WindowsJobObject_CreateAndDispose_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var jobObject = new WindowsJobObjectSandbox();
        var handle = jobObject.CreateJobObject();

        handle.Should().NotBe(nint.Zero, because: "JobObject句柄应非零");
    }

    [Fact]
    public void WindowsJobObject_StructureSize_Correct()
    {
        if (!OperatingSystem.IsWindows()) return;

        var basicSize = System.Runtime.InteropServices.Marshal.SizeOf<JobObjectNative.JOBOBJECT_BASIC_LIMIT_INFORMATION>();
        var ioSize = System.Runtime.InteropServices.Marshal.SizeOf<JobObjectNative.IO_COUNTERS>();
        var extendedSize = System.Runtime.InteropServices.Marshal.SizeOf<JobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();

        Console.WriteLine($"JOBOBJECT_BASIC_LIMIT_INFORMATION size: {basicSize} (expected 64 on x64)");
        Console.WriteLine($"IO_COUNTERS size: {ioSize} (expected 48)");
        Console.WriteLine($"JOBOBJECT_EXTENDED_LIMIT_INFORMATION size: {extendedSize} (expected 144 on x64)");

        basicSize.Should().Be(64, because: "x64上JOBOBJECT_BASIC_LIMIT_INFORMATION应为64字节");
        extendedSize.Should().Be(144, because: "x64上JOBOBJECT_EXTENDED_LIMIT_INFORMATION应为144字节");
    }

    [Fact]
    public void WindowsJobObject_CreateWithMemoryLimit_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var jobObject = new WindowsJobObjectSandbox();
        var handle = jobObject.CreateJobObject(memoryLimitBytes: 100 * 1024 * 1024);

        handle.Should().NotBe(nint.Zero, because: "100MB内存限制的JobObject应创建成功");
    }

    [Fact(Skip = "CI-only: 测试运行器自身在JobObject中，无法再分配子进程")]
    public void WindowsJobObject_AssignProcess_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var jobObject = new WindowsJobObjectSandbox();
        jobObject.CreateJobObject();

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping -n 2 127.0.0.1 >nul 2>&1",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull(because: "测试进程应启动成功");

        var assigned = jobObject.AssignProcess(process!.Id);
        assigned.Should().BeTrue(because: "进程应成功分配到JobObject");

        process.WaitForExit(10000);
    }

    [Fact]
    public async Task ProcessSandboxProvider_CreateAndDestroy_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        var provider = new ProcessSandboxProvider(_fs, Mock.Of<IProcessService>(), NullLogger<ProcessSandboxProvider>.Instance);
        provider.IsAvailable.Should().BeTrue(because: "Windows平台Process沙箱应可用");

        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            RestrictFileSystem = true,
            RestrictNetwork = true
        };

        var info = await provider.CreateSandboxAsync(options).ConfigureAwait(true);

        info.Should().NotBeNull();
        info.Type.Should().Be(SandboxType.Process);
        info.SandboxId.Should().NotBeNullOrEmpty();
        info.IsRestricted.Should().BeTrue();

        await provider.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task ProcessSandboxProvider_HighRiskPath_Redirected()
    {
        if (!OperatingSystem.IsWindows()) return;

        var provider = new ProcessSandboxProvider(_fs, Mock.Of<IProcessService>(), NullLogger<ProcessSandboxProvider>.Instance);
        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            RestrictFileSystem = true
        };

        var info = await provider.CreateSandboxAsync(options).ConfigureAwait(true);

        var highRiskPath = @"C:\Windows\System32\config\SAM";
        var resolvedPath = provider.ResolvePath(highRiskPath, info.SandboxId);

        var sandboxRoot = Path.GetFullPath(info.RootPath);
        resolvedPath.Should().StartWith(sandboxRoot, because: "高危路径应被重定向到沙箱内");

        await provider.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact(Skip = "CI-only: 需要真实进程长时间等待，IDE环境不支持")]
    public async Task WindowsJobObject_KillOnClose_TerminatesChildProcess()
    {
        if (!OperatingSystem.IsWindows()) return;

        int processId;
        Process? process;

        using (var jobObject = new WindowsJobObjectSandbox())
        {
            jobObject.CreateJobObject();

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c timeout /t 300 /nobreak >nul 2>&1",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process = Process.Start(startInfo);
            processId = process!.Id;

            var assigned = jobObject.AssignProcess(processId);
            assigned.Should().BeTrue(because: "进程应分配到JobObject");

            process.HasExited.Should().BeFalse(because: "进程应正在运行");
        }

        await Task.Delay(1000).ConfigureAwait(true);

        try
        {
            var exited = Process.GetProcessById(processId).HasExited;
            exited.Should().BeTrue(because: "JobObject关闭(KILL_ON_JOB_CLOSE)时应自动终止子进程");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"进程 {processId} 已退出: {ex.Message}");
        }
        finally
        {
            try { process!.Kill(entireProcessTree: true); } catch (InvalidOperationException ex) { Console.WriteLine($"进程已终止: {ex.Message}"); }
        }
    }

    [Fact(Skip = "CI-only: 需要真实进程长时间等待，IDE环境不支持")]
    public async Task WindowsJobObject_TerminateAll_KillsAssignedProcesses()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var jobObject = new WindowsJobObjectSandbox();
        jobObject.CreateJobObject();

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c timeout /t 300 /nobreak >nul 2>&1",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        var processId = process!.Id;

        var assigned = jobObject.AssignProcess(processId);
        assigned.Should().BeTrue(because: "进程应分配到JobObject");

        var terminated = jobObject.TerminateAllProcesses();
        terminated.Should().BeTrue(because: "TerminateAll应成功");

        await Task.Delay(1000).ConfigureAwait(true);

        try
        {
            var exited = Process.GetProcessById(processId).HasExited;
            exited.Should().BeTrue(because: "TerminateAll应终止所有分配的进程");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"进程 {processId} 已退出: {ex.Message}");
        }
    }
}
