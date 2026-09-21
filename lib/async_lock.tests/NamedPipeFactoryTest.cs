namespace Core.Utils;

/// <summary>
/// NamedPipeFactory 单元测试 — 验证工厂创建的管道属性正确,防止缓冲区大小回归为0。
/// <para>回归场景: 曾因默认缓冲区为0导致写操作永久阻塞(见 docs/task/管道缓冲区零导致写阻塞-bug修复记录.md)。</para>
/// </summary>
public class NamedPipeFactoryTest {
    private static void ConnectPair(NamedPipeServerStream server, NamedPipeClientStream client) {
        var acceptTask = Task.Run(() => server.WaitForConnection());
        client.Connect(5000);
        acceptTask.Wait(5000);
    }

    /// <summary>验证管道缓冲区大小为 65536</summary>
    [Fact]
    public void PipeBufferSize_应为65536() {
        NamedPipeFactory.PipeBufferSize.Should().Be(65536, "65536字节缓冲区足够容纳多条消息,避免写阻塞");
    }

    /// <summary>验证创建服务端返回未连接的管道</summary>
    [Fact]
    public void CreateServer_应返回未连接的管道() {
        var pipeName = $"test-factory-{Guid.NewGuid():N}";
        using var server = NamedPipeFactory.CreateServer(pipeName);
        server.Should().NotBeNull();
        server.IsConnected.Should().BeFalse("刚创建尚未接受连接");
    }

    /// <summary>验证客户端写入服务端应立即收到不阻塞</summary>
    [Fact]
    public async Task CreateServer_客户端写入服务端应立即收到_不阻塞() {
        var pipeName = $"test-factory-recv-{Guid.NewGuid():N}";
        using var server = NamedPipeFactory.CreateServer(pipeName);
        using var client = NamedPipeFactory.CreateClient(pipeName);
        ConnectPair(server, client);

        var data = Encoding.UTF8.GetBytes("factory-test-msg");
        await client.WriteAsync(data);
        await client.FlushAsync();

        var buf = new byte[data.Length];
        var read = 0;
        while (read < buf.Length) {
            var n = await server.ReadAsync(buf.AsMemory(read));
            if (n == 0) break;
            read += n;
        }
        read.Should().Be(data.Length, "应完整读取客户端写入的数据");
        Encoding.UTF8.GetString(buf).Should().Be("factory-test-msg");
    }

    /// <summary>验证服务端写入客户端应立即收到不阻塞</summary>
    [Fact]
    public async Task CreateServer_服务端写入客户端应立即收到_不阻塞() {
        var pipeName = $"test-factory-write-{Guid.NewGuid():N}";
        using var server = NamedPipeFactory.CreateServer(pipeName);
        using var client = NamedPipeFactory.CreateClient(pipeName);
        ConnectPair(server, client);

        var data = Encoding.UTF8.GetBytes("server-to-client");
        await server.WriteAsync(data);
        await server.FlushAsync();

        var buf = new byte[data.Length];
        var read = 0;
        while (read < buf.Length) {
            var n = await client.ReadAsync(buf.AsMemory(read));
            if (n == 0) break;
            read += n;
        }
        read.Should().Be(data.Length, "客户端应完整读取服务端写入的数据");
        Encoding.UTF8.GetString(buf).Should().Be("server-to-client");
    }

    /// <summary>
    /// 核心回归测试 — 缓冲区为0时,服务端写入而客户端不读会导致永久阻塞。
    /// 此测试验证工厂创建的管道缓冲区足够大,写入不阻塞即使对端不读。
    /// </summary>
    [Fact]
    public async Task 回归_服务端写入客户端不读_不阻塞_缓冲区足够() {
        var pipeName = $"test-factory-noread-{Guid.NewGuid():N}";
        using var server = NamedPipeFactory.CreateServer(pipeName);
        using var client = NamedPipeFactory.CreateClient(pipeName);
        ConnectPair(server, client);

        var data = Encoding.UTF8.GetBytes("client-never-reads-this");
        var writeTask = server.WriteAsync(data).AsTask();

        await writeTask.WaitAsync(TimeSpan.FromSeconds(2));

        await server.FlushAsync();
    }

    /// <summary>验证客户端能连接到 CreateServer 创建的管道</summary>
    [Fact]
    public void CreateClient_应能连接到CreateServer创建的管道() {
        var pipeName = $"test-factory-connect-{Guid.NewGuid():N}";
        using var server = NamedPipeFactory.CreateServer(pipeName);
        using var client = NamedPipeFactory.CreateClient(pipeName);
        ConnectPair(server, client);

        client.IsConnected.Should().BeTrue("客户端应成功连接到工厂创建的服务端");
        server.IsConnected.Should().BeTrue("服务端应接受客户端连接");
    }

    /// <summary>验证多消息连续写入不阻塞</summary>
    [Fact]
    public async Task CreateServer_多消息连续写入不阻塞() {
        var pipeName = $"test-factory-multi-{Guid.NewGuid():N}";
        using var server = NamedPipeFactory.CreateServer(pipeName);
        using var client = NamedPipeFactory.CreateClient(pipeName);
        ConnectPair(server, client);

        var messages = new List<byte[]>();
        for (var i = 0; i < 100; i++) {
            var data = Encoding.UTF8.GetBytes($"msg-{i}");
            messages.Add(data);
            await server.WriteAsync(data);
            await server.FlushAsync();
        }

        var totalLen = messages.Sum(m => m.Length);
        var allData = new byte[totalLen];
        var read = 0;
        while (read < totalLen) {
            var n = await client.ReadAsync(allData.AsMemory(read));
            if (n == 0) break;
            read += n;
        }
        read.Should().Be(totalLen, "客户端应完整收到100条消息的拼接数据");
    }
}