namespace McpClient;

public sealed class McpProtocolExceptionTests
{
    [Fact]
    public void MethodNotFound_ShouldUseMcpToolNotFoundErrorCode()
    {
        var ex = McpProtocolException.MethodNotFound("read_file", "req-1");

        ex.ErrorCode.Should().Be("MCP004");
    }

    [Fact]
    public void ParseError_ShouldUseMcpProtocolErrorCode()
    {
        var ex = McpProtocolException.ParseError("{invalid", new InvalidOperationException("bad json"));

        ex.ErrorCode.Should().Be("MCP002");
    }

    [Fact]
    public void ServerError_ShouldUseMcpProtocolErrorCode()
    {
        var ex = McpProtocolException.ServerError("req-1", "boom");

        ex.ErrorCode.Should().Be("MCP002");
    }
}
