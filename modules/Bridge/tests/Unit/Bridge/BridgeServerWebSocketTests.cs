namespace Bridge.Tests;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using JoinCode.Abstractions.Models.Shell;

/// <summary>
/// BridgeServer WebSocket 端到端集成测试 — P0-B E2E
/// 启动真实 BridgeServer + ClientWebSocket，验证 executeCommand/setSelection 完整消息流
/// 标记为 Integration 类别，避免拖慢快速测试
/// </summary>
[Trait("Category", "Integration")]
public sealed class BridgeServerWebSocketTests : IAsyncDisposable
{
    private readonly int _port;
    private readonly BridgeServer _server;
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(10));

    public BridgeServerWebSocketTests()
    {
        // 选择一个不太可能冲突的端口（避开 3456 默认端口与常用端口）
        _port = Random.Shared.Next(8800, 9800);
        _server = new BridgeServer(
            CreateFileOpMock().Object,
            port: _port,
            logger: NullLogger<BridgeServer>.Instance,
            shellService: CreateShellServiceMock().Object,
            ideService: CreateIdeServiceMock().Object);
        _server.Start();
    }

    public async ValueTask DisposeAsync()
    {
        try { await _server.StopAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Dispose server failed: {ex.Message}"); }
        _cts.Dispose();
    }

    private static Mock<IFileOperationService> CreateFileOpMock()
    {
        var mock = new Mock<IFileOperationService>();
        mock.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        return mock;
    }

    private static Mock<IShellExecutionService> CreateShellServiceMock()
    {
        var mock = new Mock<IShellExecutionService>();
        mock.Setup(s => s.ExecuteAsync(
                It.Is<string>(c => c == "echo hello"),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellExecutionResult
            {
                Stdout = "hello",
                Stderr = "",
                ExitCode = 0
            });
        mock.Setup(s => s.ExecuteAsync(
                It.Is<string>(c => c == "nonexistent-cmd-xxx"),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellExecutionResult
            {
                Stdout = "",
                Stderr = "command not found",
                ExitCode = 127
            });
        return mock;
    }

    private static Mock<IIdeIntegrationService> CreateIdeServiceMock()
    {
        var mock = new Mock<IIdeIntegrationService>();
        mock.Setup(i => i.SetSelectionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock;
    }

    private async Task<BridgeServerMessage> SendAndReceiveAsync(BridgeServerMessage request)
    {
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://localhost:{_port}/"), _cts.Token).ConfigureAwait(true);

        var requestJson = JsonSerializer.Serialize(request, BridgeJsonContext.Default.BridgeServerMessage);
        var requestBytes = Encoding.UTF8.GetBytes(requestJson);
        await client.SendAsync(requestBytes, WebSocketMessageType.Text, endOfMessage: true, _cts.Token).ConfigureAwait(true);

        // 先读 connected 欢迎消息，再读真正的响应
        var buffer = new byte[8192];
        var received = await client.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(true);
        received.MessageType.Should().Be(WebSocketMessageType.Text);
        var connectedJson = Encoding.UTF8.GetString(buffer, 0, received.Count);

        // 如果第一帧就是响应（比如 connected 直接被合并），尝试解析
        if (JsonDocument.Parse(connectedJson).RootElement.GetProperty("type").GetString() == "connected")
        {
            received = await client.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(true);
            received.MessageType.Should().Be(WebSocketMessageType.Text);
            var responseJson = Encoding.UTF8.GetString(buffer, 0, received.Count);
            return JsonSerializer.Deserialize(responseJson, BridgeJsonContext.Default.BridgeServerMessage)
                ?? throw new InvalidOperationException("反序列化失败");
        }

        return JsonSerializer.Deserialize(connectedJson, BridgeJsonContext.Default.BridgeServerMessage)
            ?? throw new InvalidOperationException("反序列化失败");
    }

    /// <summary>
    /// 用例1: executeCommand "echo hello" 应返回 success=true, output="hello", exitCode=0
    /// </summary>
    [Fact]
    public async Task WebSocket_ExecuteCommand_EchoHello_ShouldReturnSuccessWithOutput()
    {
        // Arrange
        var request = new BridgeServerMessage
        {
            Type = "executeCommand",
            Data = JsonDocument.Parse("""{"command":"echo hello"}""").RootElement
        };

        // Act
        var response = await SendAndReceiveAsync(request).ConfigureAwait(true);

        // Assert
        response.Type.Should().Be("commandExecuted");
        var data = response.Data!.Value.Deserialize<BridgeCommandExecutedData>(BridgeJsonContext.Default.BridgeCommandExecutedData);
        data.Should().NotBeNull();
        data!.Success.Should().BeTrue();
        data.Output.Should().Be("hello");
        data.ExitCode.Should().Be(0);
        data.DurationMs.Should().NotBeNull().And.BeGreaterThanOrEqualTo(0);
    }

    /// <summary>
    /// 用例2: executeCommand 无效命令应返回 success=false, exitCode!=0, error 非空
    /// </summary>
    [Fact]
    public async Task WebSocket_ExecuteCommand_InvalidCommand_ShouldReturnFailureWithStdError()
    {
        // Arrange
        var request = new BridgeServerMessage
        {
            Type = "executeCommand",
            Data = JsonDocument.Parse("""{"command":"nonexistent-cmd-xxx"}""").RootElement
        };

        // Act
        var response = await SendAndReceiveAsync(request).ConfigureAwait(true);

        // Assert
        response.Type.Should().Be("commandExecuted");
        var data = response.Data!.Value.Deserialize<BridgeCommandExecutedData>(BridgeJsonContext.Default.BridgeCommandExecutedData);
        data.Should().NotBeNull();
        data!.Success.Should().BeFalse();
        data.ExitCode.Should().NotBe(0);
        data.Error.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// 用例3: setSelection 有效文件+选区 应返回 success=true
    /// </summary>
    [Fact]
    public async Task WebSocket_SetSelection_WithIdeConnected_ShouldSucceed()
    {
        // Arrange
        var request = new BridgeServerMessage
        {
            Type = "setSelection",
            Data = JsonDocument.Parse("""{"file":"test.cs","startLine":5,"startCol":1,"endLine":5,"endCol":10}""").RootElement
        };

        // Act
        var response = await SendAndReceiveAsync(request).ConfigureAwait(true);

        // Assert
        response.Type.Should().Be("selectionSet");
        var data = response.Data!.Value.Deserialize<BridgeSelectionSetData>(BridgeJsonContext.Default.BridgeSelectionSetData);
        data.Should().NotBeNull();
        data!.Success.Should().BeTrue();
    }

    /// <summary>
    /// 用例4: 无 IDE 服务注入时 setSelection 应返回 success=false, error 包含 IDE
    /// </summary>
    [Fact]
    public async Task WebSocket_SetSelection_WithoutIdeService_ShouldReturnFailure()
    {
        // Arrange — 启动一个无 IDE 服务的 BridgeServer 实例
        var port = Random.Shared.Next(9800, 9999);
        using var serverNoIde = new BridgeServer(
            CreateFileOpMock().Object,
            port: port,
            logger: NullLogger<BridgeServer>.Instance,
            shellService: CreateShellServiceMock().Object,
            ideService: null);
        serverNoIde.Start();
        try
        {
            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://localhost:{port}/"), _cts.Token).ConfigureAwait(true);

            // 读 connected 欢迎消息
            var buffer = new byte[8192];
            await client.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(true);

            // 发送 setSelection 请求
            var requestJson = """{"type":"setSelection","data":{"file":"test.cs","startLine":5,"startCol":1,"endLine":5,"endCol":10}}""";
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);
            await client.SendAsync(requestBytes, WebSocketMessageType.Text, endOfMessage: true, _cts.Token).ConfigureAwait(true);

            // 接收响应
            var received = await client.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(true);
            var responseJson = Encoding.UTF8.GetString(buffer, 0, received.Count);
            var response = JsonSerializer.Deserialize(responseJson, BridgeJsonContext.Default.BridgeServerMessage)!;

            // Assert
            response.Type.Should().Be("selectionSet");
            var data = response.Data!.Value.Deserialize<BridgeSelectionSetData>(BridgeJsonContext.Default.BridgeSelectionSetData);
            data.Should().NotBeNull();
            data!.Success.Should().BeFalse();
            data.Error.Should().NotBeNullOrEmpty();
            data.Error.Should().Contain("IDE");
        }
        finally
        {
            try { await serverNoIde.StopAsync(CancellationToken.None).ConfigureAwait(true); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Dispose serverNoIde failed: {ex.Message}"); }
        }
    }
}
