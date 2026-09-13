namespace Mcp.Tests;

/// <summary>
/// McpHttpServer 单元测试 — 验证 Streamable HTTP 服务端构造与无状态/有状态模式配置
/// </summary>
public class McpHttpServerTests
{
    [Fact]
    public void Constructor_StatelessMode_Default_True()
    {
        var server = new McpServer("test");
        using var httpServer = new McpHttpServer(server, "http://localhost:8080/");
        httpServer.IsStatelessMode.Should().BeTrue();
    }

    [Fact]
    public void Constructor_StatefulMode_WhenFalse()
    {
        var server = new McpServer("test");
        using var httpServer = new McpHttpServer(server, "http://localhost:8080/", statelessMode: false);
        httpServer.IsStatelessMode.Should().BeFalse();
    }

    [Fact]
    public void ActiveSessionCount_Initial_Zero()
    {
        var server = new McpServer("test");
        using var httpServer = new McpHttpServer(server, "http://localhost:8080/");
        httpServer.ActiveSessionCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_NullServer_Throws()
    {
        var act = () => new McpHttpServer(null!, "http://localhost:8080/");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyPrefix_Throws()
    {
        var server = new McpServer("test");
        var act = () => new McpHttpServer(server, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_AllowedOrigins_StoredAsFrozenSet()
    {
        var server = new McpServer("test");
        using var httpServer = new McpHttpServer(server, "http://localhost:8080/", allowedOrigins: ["https://example.com"]);
        httpServer.IsStatelessMode.Should().BeTrue();
    }
}
