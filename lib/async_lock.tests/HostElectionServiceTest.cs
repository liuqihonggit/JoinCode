namespace Core.Utils;

/// <summary>
/// HostElectionService 单元测试 — 验证主机选举、角色判定、心跳监控。
/// </summary>
public class HostElectionServiceTest {
    [Fact]
    public async Task ProcessId_ReturnsCurrentPid() {
        await using var service = new HostElectionService(pipeName: $"test-pipe-{Guid.NewGuid():N}");
        service.ProcessId.Should().Be(Environment.ProcessId.ToString());
    }

    [Fact]
    public async Task ElectAsync_NoExistingHost_BecomesHost() {
        var uniquePipe = $"test-pipe-{Guid.NewGuid():N}";
        await using var service = new HostElectionService(pipeName: uniquePipe);

        var result = await service.ElectAsync();

        result.Role.Should().Be(ProcessRole.Host);
        result.IsNewlyElected.Should().BeTrue();
        result.HostProcessId.Should().Be(service.ProcessId);
    }

    [Fact]
    public async Task ElectAsync_CalledTwice_ReturnsConsistentRole() {
        var uniquePipe = $"test-pipe-{Guid.NewGuid():N}";
        await using var service = new HostElectionService(pipeName: uniquePipe);

        var first = await service.ElectAsync();
        var second = await service.ElectAsync();

        second.Role.Should().Be(first.Role);
        second.HostProcessId.Should().Be(first.HostProcessId);
    }

    [Fact]
    public async Task CurrentRole_SetAfterElection() {
        var uniquePipe = $"test-pipe-{Guid.NewGuid():N}";
        await using var service = new HostElectionService(pipeName: uniquePipe);

        service.CurrentRole.Should().BeNull();

        await service.ElectAsync();

        service.CurrentRole.Should().NotBeNull();
        service.CurrentRole!.ProcessId.Should().Be(service.ProcessId);
    }

    [Fact]
    public async Task ElectionChangesAsync_ProducesRoleChange() {
        var uniquePipe = $"test-pipe-{Guid.NewGuid():N}";
        await using var service = new HostElectionService(pipeName: uniquePipe);

        var cts = new CancellationTokenSource();
        var results = new List<HostElectionResult>();
        var consumeTask = Task.Run(async () => {
            await foreach (var r in service.ElectionChangesAsync(cts.Token))
                results.Add(r);
        }, cts.Token);

        await service.ElectAsync();
        await WaitUntilAsync(() => results.Count > 0, TimeSpan.FromSeconds(2));

        cts.Cancel();
        await Task.WhenAny(consumeTask, Task.Delay(1000));

        results.Should().NotBeEmpty();
        results[0].Role.Should().Be(ProcessRole.Host);
    }

    [Fact]
    public async Task UpdateSnapshot_StoresLastSnapshot() {
        var uniquePipe = $"test-pipe-{Guid.NewGuid():N}";
        await using var service = new HostElectionService(pipeName: uniquePipe);

        var snapshot = new HostContextSnapshot {
            Timestamp = DateTimeOffset.UtcNow,
            HostProcessId = "12345",
            RoutingTable = new Dictionary<string, string> { ["agent-1"] = "12345" },
            PendingMessages = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(),
            BuildQueue = new BuildQueueState {
                PendingCount = 0,
                RunningCount = 0,
                PendingTasks = Array.Empty<string>()
            }
        };

        service.UpdateSnapshot(snapshot);
        service.LastSnapshot.Should().NotBeNull();
        service.LastSnapshot!.HostProcessId.Should().Be("12345");
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes() {
        var service = new HostElectionService(pipeName: $"test-pipe-{Guid.NewGuid():N}");
        await service.DisposeAsync();
        await service.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout) {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) {
            if (predicate()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Condition not met within {timeout.TotalSeconds}s");
    }
}