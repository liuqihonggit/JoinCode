namespace Mcp.Tests;

using McpProtocol;

/// <summary>
/// McpServer 防御性编程测试
/// 验证并发注册工具处理器不抛异常
/// </summary>
public sealed class McpServerDefensiveTests
{
    private sealed class FakeToolHandler : IMcpProtocolHandler
    {
        public string Name { get; }
        public string Description => "fake";
        public JsonElement InputSchema => default;
        public Task<object> ExecuteAsync(Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult<object>(null!);

        public FakeToolHandler(string name) => Name = name;
    }

    [Fact]
    public async Task ConcurrentRegisterToolHandler_DoesNotThrow()
    {
        var server = new McpServer("test");
        var exceptions = new ConcurrentQueue<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var tasks = Enumerable.Range(0, 4).Select(i => Task.Run(() =>
        {
            try
            {
                var n = i * 1000;
                while (!cts.IsCancellationRequested)
                    server.RegisterToolHandler(new FakeToolHandler($"tool-{n++}"));
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        }));

        await Task.WhenAll(tasks);
        exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentRegisterResourceHandler_DoesNotThrow()
    {
        var server = new McpServer("test");
        var exceptions = new ConcurrentQueue<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var tasks = Enumerable.Range(0, 4).Select(i => Task.Run(() =>
        {
            try
            {
                var n = i * 1000;
                while (!cts.IsCancellationRequested)
                    server.RegisterResourceHandler(new FakeResourceHandler($"res://{n++}"));
            }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        }));

        await Task.WhenAll(tasks);
        exceptions.Should().BeEmpty();
    }

    private sealed class FakeResourceHandler : IResourceHandler
    {
        public string Uri { get; }
        public string Name => Uri;
        public string? Description => "fake";
        public string? MimeType => null;
        public Task<McpResourceContent> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new McpResourceContent());

        public FakeResourceHandler(string uri) => Uri = uri;
    }
}
