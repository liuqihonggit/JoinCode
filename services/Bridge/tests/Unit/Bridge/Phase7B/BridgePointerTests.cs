
// Bridge.Tests 独立组件测试，使用 InMemoryFileSystem 替代 PhysicalFileSystem
#pragma warning disable JCC9001, JCC9002
namespace Bridge.Tests.Phase7B;

public sealed class BridgePointerTests : IDisposable
{
    private readonly BridgePointerService _service;

    public BridgePointerTests()
    {
        _service = new BridgePointerService(new InMemoryFileSystem());
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task WriteAsync_ReadAsync_RoundTrip()
    {
        var testDir = "/bridge_pointer_test";
        var pointer = new BridgePointer
        {
            SessionId = "cse_test123",
            EnvironmentId = "env_456",
            Source = "standalone",
        };

        await _service.WriteAsync(testDir, pointer).ConfigureAwait(true);

        var read = await _service.ReadAsync(testDir).ConfigureAwait(true);
        Assert.NotNull(read);
        Assert.Equal("cse_test123", read.Pointer.SessionId);
        Assert.Equal("env_456", read.Pointer.EnvironmentId);
    }

    [Fact]
    public async Task ReadAsync_NoFile_ReturnsNull()
    {
        var testDir = "/bridge_pointer_test_empty";
        var read = await _service.ReadAsync(testDir).ConfigureAwait(true);
        Assert.Null(read);
    }

    [Fact]
    public async Task ClearAsync_RemovesPointer()
    {
        var testDir = "/bridge_pointer_test_clear";
        var pointer = new BridgePointer
        {
            SessionId = "cse_test",
            EnvironmentId = "env_test",
            Source = "REPL",
        };

        await _service.WriteAsync(testDir, pointer).ConfigureAwait(true);
        await _service.ClearAsync(testDir).ConfigureAwait(true);

        var read = await _service.ReadAsync(testDir).ConfigureAwait(true);
        Assert.Null(read);
    }

    // Note: TTL 过期测试依赖 PhysicalFileSystem 的 File.SetLastWriteTimeUtc，
    // InMemoryFileSystem 不支持修改文件时间戳，该测试移回 Sync.Tests
}
