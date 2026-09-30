namespace Core.Tests.Scheduling;

/// <summary>
/// TaskService(内存实现)守卫确定性测试 — offset/limit 范围(TASK031)
/// <para>确定性:不依赖时序/IO,给定非法输入 → 断言抛 ArgumentOutOfRangeException</para>
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class TaskServiceTest {
    [Fact]
    public async Task ListTasksAsync_NegativeOffset_ThrowsArgumentOutOfRangeException() {
        await using var svc = new TaskService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.ListTasksAsync(null, null, null, 10, -1)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ListTasksAsync_NegativeLimit_ThrowsArgumentOutOfRangeException() {
        await using var svc = new TaskService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.ListTasksAsync(null, null, null, -1, 0)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ListTasksAsync_ZeroOffsetAndLimit_ReturnsEmpty() {
        // 边界:offset=0, limit=0 合法,返回空列表
        await using var svc = new TaskService();
        var result = await svc.ListTasksAsync(null, null, null, 0, 0).ConfigureAwait(true);
        Assert.True(result.Success);
        Assert.Empty(result.Tasks);
    }
}
