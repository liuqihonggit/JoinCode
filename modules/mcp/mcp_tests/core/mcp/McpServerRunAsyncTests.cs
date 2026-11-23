namespace Mcp.Tests;

/// <summary>
/// McpServer.RunAsync 主循环确定性测试 —
/// 用 StringReader/StringWriter 注入消除 Console IO,喂入有限 JSON 行自然终止循环。
/// 不喂空行以绕过空行分支的 Task.Delay 时序点(残留时序可容忍)。
/// </summary>
public sealed class McpServerRunAsyncTests {

    [Fact]
    public async Task RunAsync_PingRequest_WritesResultResponse() {
        var input = new StringReader("""{"jsonrpc":"2.0","method":"ping","id":1}""");
        var output = new StringWriter();
        var server = new McpServer("test", "1.0", null, input, output);

        await server.RunAsync(CancellationToken.None);

        var result = output.ToString();
        result.Should().Contain("\"result\"");
        result.Should().Contain("\"id\":1");
    }

    [Fact]
    public async Task RunAsync_InitializeRequest_WritesServerInfoAndProtocolVersion() {
        var input = new StringReader("""{"jsonrpc":"2.0","method":"initialize","id":1}""");
        var output = new StringWriter();
        var server = new McpServer("myServer", "2.5", null, input, output);

        await server.RunAsync(CancellationToken.None);

        var result = output.ToString();
        result.Should().Contain("\"result\"");
        result.Should().Contain("\"protocolVersion\"");
        result.Should().Contain("\"name\":\"myServer\"");
        result.Should().Contain("\"version\":\"2.5\"");
    }

    [Fact]
    public async Task RunAsync_ToolsListRequest_WritesEmptyToolsArray() {
        var input = new StringReader("""{"jsonrpc":"2.0","method":"tools/list","id":2}""");
        var output = new StringWriter();
        var server = new McpServer("test", "1.0", null, input, output);

        await server.RunAsync(CancellationToken.None);

        var result = output.ToString();
        result.Should().Contain("\"result\"");
        result.Should().Contain("\"tools\"");
    }

    [Fact]
    public async Task RunAsync_UnknownMethod_WritesMethodNotFoundError() {
        var input = new StringReader("""{"jsonrpc":"2.0","method":"foo/bar","id":3}""");
        var output = new StringWriter();
        var server = new McpServer("test", "1.0", null, input, output);

        await server.RunAsync(CancellationToken.None);

        var result = output.ToString();
        result.Should().Contain("\"error\"");
        result.Should().Contain("-32601");
        result.Should().Contain("foo/bar");
    }

    [Fact]
    public async Task RunAsync_NotificationWithoutId_WritesNoResponse() {
        var input = new StringReader("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        var output = new StringWriter();
        var server = new McpServer("test", "1.0", null, input, output);

        await server.RunAsync(CancellationToken.None);

        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_MultipleRequests_WritesAllResponses() {
        var input = new StringReader(
            """{"jsonrpc":"2.0","method":"ping","id":1}""" + "\n" +
            """{"jsonrpc":"2.0","method":"tools/list","id":2}""" + "\n" +
            """{"jsonrpc":"2.0","method":"ping","id":3}""");
        var output = new StringWriter();
        var server = new McpServer("test", "1.0", null, input, output);

        await server.RunAsync(CancellationToken.None);

        var result = output.ToString();
        result.Should().Contain("\"id\":1");
        result.Should().Contain("\"id\":2");
        result.Should().Contain("\"id\":3");
        var responseLineCount = result.Count(c => c == '\n');
        responseLineCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task RunAsync_LspContentLengthFrame_WritesContentLengthResponse() {
        var json = """{"jsonrpc":"2.0","method":"ping","id":7}""";
        var input = new StringReader($"Content-Length: {json.Length}\n\n{json}");
        var output = new StringWriter();
        var server = new McpServer("test", "1.0", null, input, output);

        await server.RunAsync(CancellationToken.None);

        var result = output.ToString();
        result.Should().Contain("Content-Length:");
        result.Should().Contain("\"result\"");
        result.Should().Contain("\"id\":7");
    }

    [Fact]
    public async Task RunAsync_EmptyInput_TerminatesCleanlyWithoutOutput() {
        var input = new StringReader("");
        var output = new StringWriter();
        var server = new McpServer("test", "1.0", null, input, output);

        await server.RunAsync(CancellationToken.None);

        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PreCancelledToken_ReturnsImmediatelyWithoutOutput() {
        var input = new StringReader("""{"jsonrpc":"2.0","method":"ping","id":1}""");
        var output = new StringWriter();
        var server = new McpServer("test", "1.0", null, input, output);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await server.RunAsync(cts.Token);

        output.ToString().Should().BeEmpty();
    }
}
