namespace Mcp.Tests;

public sealed class McpNameNormalizerTests
{
    [Fact]
    public void NormalizeNameForMCP_DelegatesToNameNormalizer()
    {
        McpNameNormalizer.NormalizeNameForMCP("hello world").Should().Be("hello_world");
    }

    [Fact]
    public void GetMcpPrefix_ReturnsCorrectFormat()
    {
        McpNameNormalizer.GetMcpPrefix("my-server").Should().Be("mcp__my-server__");
    }

    [Fact]
    public void BuildMcpToolName_CombinesServerAndTool()
    {
        McpNameNormalizer.BuildMcpToolName("my-server", "my-tool").Should().Be("mcp__my-server__my-tool");
    }

    [Fact]
    public void McpInfoFromString_ParsesValidToolString()
    {
        var result = McpNameNormalizer.McpInfoFromString("mcp__my-server__my-tool");
        result.Should().NotBeNull();
        result.Value.ServerName.Should().Be("my-server");
        result.Value.ToolName.Should().Be("my-tool");
    }

    [Fact]
    public void McpInfoFromString_ParsesServerOnly()
    {
        var result = McpNameNormalizer.McpInfoFromString("mcp__my-server__");
        result.Should().NotBeNull();
        result.Value.ServerName.Should().Be("my-server");
        result.Value.ToolName.Should().BeEmpty();
    }

    [Fact]
    public void McpInfoFromString_ReturnsNullForNonMcp()
    {
        McpNameNormalizer.McpInfoFromString("other__tool").Should().BeNull();
    }

    [Fact]
    public void McpInfoFromString_ReturnsNullForMissingServer()
    {
        McpNameNormalizer.McpInfoFromString("mcp____tool").Should().BeNull();
    }

    [Fact]
    public void McpInfoFromString_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => McpNameNormalizer.McpInfoFromString(null!));
    }

    [Fact]
    public void GetMcpDisplayName_StripsPrefix()
    {
        McpNameNormalizer.GetMcpDisplayName("mcp__my-server__my-tool", "my-server").Should().Be("my-tool");
    }

    [Fact]
    public void GetMcpDisplayName_ReturnsFullNameIfNoPrefix()
    {
        McpNameNormalizer.GetMcpDisplayName("other-tool", "my-server").Should().Be("other-tool");
    }
}
