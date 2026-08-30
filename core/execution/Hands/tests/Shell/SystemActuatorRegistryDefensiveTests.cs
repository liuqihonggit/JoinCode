namespace Hands.Shell.Tests;


/// <summary>
/// SystemActuatorRegistry 防御性编程测试
/// 验证 CancelTasksForAgentAsync 全异步化 + 部分成功机制
/// </summary>
public sealed class SystemActuatorRegistryDefensiveTests
{
    private static SystemActuatorRegistry CreateSut()
        => new(TestFileSystem.Current);

    [Fact]
    public async Task CancelTasksForAgentAsync_EmptyAgent_ReturnsZeroWithoutBlocking()
    {
        var sut = CreateSut();
        var completed = false;
        var task = Task.Run(async () =>
        {
            var result = await sut.CancelTasksForAgentAsync("agent-1");
            result.Should().Be(0);
            completed = true;
        });

        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeTrue("CancelTasksForAgentAsync 不应阻塞线程");
    }

    [Fact]
    public async Task CancelTasksForAgentAsync_UnknownAgent_ReturnsZero()
    {
        var sut = CreateSut();
        var result = await sut.CancelTasksForAgentAsync("unknown-agent");
        result.Should().Be(0);
    }

    [Fact]
    public async Task CancelTasksForAgentAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Invoking(() => sut.CancelTasksForAgentAsync("agent-1", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
