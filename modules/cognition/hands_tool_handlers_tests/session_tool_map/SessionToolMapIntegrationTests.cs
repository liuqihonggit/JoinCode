namespace Hands.ToolHandlers.Tests;

#pragma warning disable JCC11003

/// <summary>
/// SessionToolMap 集成测试 — 验证 KillByAgent 端到端链路（真实进程，非 mock）（ADR 0152）
/// </summary>
public sealed class SessionToolMapIntegrationTests {
    private static (Process process, string command) StartLongRunningProcess() {
        var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        var psi = new ProcessStartInfo {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (isWindows) {
            psi.FileName = "ping";
            psi.Arguments = "-n 30 127.0.0.1";
        } else {
            psi.FileName = "sleep";
            psi.Arguments = "30";
        }
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
        return (process, isWindows ? "ping" : "sleep");
    }

    [Fact]
    public void KillByAgent_KillsRealProcess_EndToEnd() {
        var map = new SessionToolMap();
        var (process, command) = StartLongRunningProcess();

        try {
            process.HasExited.Should().BeFalse("进程应仍在运行");

            var info = new ToolLifecycleInfo(
                ToolName: $"bash:{command}",
                ProcessId: process.Id,
                StartTime: DateTime.UtcNow,
                TimeoutDuration: TimeSpan.FromMinutes(10),
                WarningThreshold: TimeSpan.FromSeconds(30),
                KillThreshold: TimeSpan.FromMinutes(5),
                AgentId: "agent-test-1");

            map.Register(info);

            map.GetByAgent("agent-test-1").Should().HaveCount(1);

            var killed = map.KillByAgent("agent-test-1");

            killed.Should().Be(1, "应杀死 1 个进程");

            process.WaitForExit(5000).Should().BeTrue("进程应在 5s 内退出");
            process.HasExited.Should().BeTrue("进程已退出");

            map.GetByAgent("agent-test-1").Should().BeEmpty("KillByAgent 后 map 中该 agent 的工具应被移除");
        } finally {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
        }
    }

    [Fact]
    public void KillByAgent_KillsMultipleProcesses_ForSameAgent() {
        var map = new SessionToolMap();
        var processes = new List<Process>();

        try {
            for (var i = 0; i < 3; i++) {
                var (process, command) = StartLongRunningProcess();
                processes.Add(process);
                map.Register(new ToolLifecycleInfo(
                    ToolName: $"bash:{command}-{i}",
                    ProcessId: process.Id,
                    StartTime: DateTime.UtcNow,
                    TimeoutDuration: TimeSpan.FromMinutes(10),
                    WarningThreshold: TimeSpan.FromSeconds(30),
                    KillThreshold: TimeSpan.FromMinutes(5),
                    AgentId: "agent-multi"));
            }

            map.GetByAgent("agent-multi").Should().HaveCount(3);

            var killed = map.KillByAgent("agent-multi");

            killed.Should().Be(3, "应杀死 3 个进程");

            foreach (var process in processes) {
                process.WaitForExit(5000).Should().BeTrue("进程应在 5s 内退出");
                process.HasExited.Should().BeTrue("进程已退出");
            }

            map.GetByAgent("agent-multi").Should().BeEmpty("所有工具应被移除");
        } finally {
            foreach (var process in processes) {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                process.Dispose();
            }
        }
    }

    [Fact]
    public void KillByAgent_OnlyKillsSpecifiedAgent_NotOthers() {
        var map = new SessionToolMap();
        var (processA, commandA) = StartLongRunningProcess();
        var (processB, commandB) = StartLongRunningProcess();

        try {
            map.Register(new ToolLifecycleInfo(
                ToolName: $"bash:{commandA}-a",
                ProcessId: processA.Id,
                StartTime: DateTime.UtcNow,
                TimeoutDuration: TimeSpan.FromMinutes(10),
                WarningThreshold: TimeSpan.FromSeconds(30),
                KillThreshold: TimeSpan.FromMinutes(5),
                AgentId: "agent-a"));

            map.Register(new ToolLifecycleInfo(
                ToolName: $"bash:{commandB}-b",
                ProcessId: processB.Id,
                StartTime: DateTime.UtcNow,
                TimeoutDuration: TimeSpan.FromMinutes(10),
                WarningThreshold: TimeSpan.FromSeconds(30),
                KillThreshold: TimeSpan.FromMinutes(5),
                AgentId: "agent-b"));

            var killed = map.KillByAgent("agent-a");

            killed.Should().Be(1, "只杀死 agent-a 的 1 个进程");

            processA.WaitForExit(5000).Should().BeTrue("agent-a 的进程应退出");
            processA.HasExited.Should().BeTrue("agent-a 进程已退出");

            processB.HasExited.Should().BeFalse("agent-b 的进程应仍在运行");

            map.GetByAgent("agent-a").Should().BeEmpty("agent-a 的工具应被移除");
            map.GetByAgent("agent-b").Should().HaveCount(1, "agent-b 的工具应仍存在");
        } finally {
            if (!processA.HasExited)
                processA.Kill(entireProcessTree: true);
            if (!processB.HasExited)
                processB.Kill(entireProcessTree: true);
            processA.Dispose();
            processB.Dispose();
        }
    }

    [Fact]
    public void IToolLifecycleKiller_Interface_Dispatches_To_KillByAgent() {
        IToolLifecycleKiller killer = new SessionToolMap();
        var (process, command) = StartLongRunningProcess();

        try {
            ((SessionToolMap)killer).Register(new ToolLifecycleInfo(
                ToolName: $"bash:{command}",
                ProcessId: process.Id,
                StartTime: DateTime.UtcNow,
                TimeoutDuration: TimeSpan.FromMinutes(10),
                WarningThreshold: TimeSpan.FromSeconds(30),
                KillThreshold: TimeSpan.FromMinutes(5),
                AgentId: "agent-iface"));

            var killed = killer.KillByAgent("agent-iface");

            killed.Should().Be(1);
            process.WaitForExit(5000).Should().BeTrue();
            process.HasExited.Should().BeTrue();
        } finally {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
        }
    }
}
