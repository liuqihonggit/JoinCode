namespace Mcp.Tests;

/// <summary>
/// McpStdioClient.ParseEndpoint 单元测试 — 验证 endpoint 解析(引号路径 + 空格拆分)
/// </summary>
public sealed class McpStdioClientParseEndpointTests {
    [Fact]
    public void ParseEndpoint_SimpleCommand_ReturnsFileNameOnly() {
        var (fileName, args) = McpStdioClient.ParseEndpoint("node");
        fileName.Should().Be("node");
        args.Should().BeEmpty();
    }

    [Fact]
    public void ParseEndpoint_CommandWithArgs_SplitsOnSpace() {
        var (fileName, args) = McpStdioClient.ParseEndpoint("node script.js");
        fileName.Should().Be("node");
        args.Should().Be("script.js");
    }

    [Fact]
    public void ParseEndpoint_CommandWithMultipleArgs_SplitsOnFirstSpace() {
        var (fileName, args) = McpStdioClient.ParseEndpoint("node script.js --port 3000");
        fileName.Should().Be("node");
        args.Should().Be("script.js --port 3000");
    }

    [Fact]
    public void ParseEndpoint_QuotedPath_ReturnsPathAsFileName() {
        var (fileName, args) = McpStdioClient.ParseEndpoint("\"C:\\Program Files\\node.exe\" script.js");
        fileName.Should().Be("C:\\Program Files\\node.exe");
        args.Should().Be("script.js");
    }

    [Fact]
    public void ParseEndpoint_QuotedPathNoArgs_ReturnsPathOnly() {
        var (fileName, args) = McpStdioClient.ParseEndpoint("\"C:\\my tool\\run.exe\"");
        fileName.Should().Be("C:\\my tool\\run.exe");
        args.Should().BeEmpty();
    }

    [Fact]
    public void ParseEndpoint_QuotedPathWithMultipleArgs_ReturnsArgsAfterQuote() {
        var (fileName, args) = McpStdioClient.ParseEndpoint("\"/usr/local/bin/node\" server.js --verbose");
        fileName.Should().Be("/usr/local/bin/node");
        args.Should().Be("server.js --verbose");
    }

    [Fact]
    public void ParseEndpoint_WhitespaceOnly_ReturnsAsIs() {
        var (fileName, args) = McpStdioClient.ParseEndpoint("   ");
        fileName.Should().Be("   ");
        args.Should().BeEmpty();
    }

    [Fact]
    public void ParseEndpoint_EmptyString_ReturnsEmpty() {
        var (fileName, args) = McpStdioClient.ParseEndpoint(string.Empty);
        fileName.Should().BeEmpty();
        args.Should().BeEmpty();
    }

    [Fact]
    public void ParseEndpoint_LeadingSpace_NoQuote_FirstSpaceAtZero() {
        // " node" → firstSpace = 0, IndexOf(' ') = 0, firstSpace <= 0 → 返回 (endpoint, "")
        var (fileName, args) = McpStdioClient.ParseEndpoint(" node script.js");
        fileName.Should().Be(" node script.js");
        args.Should().BeEmpty();
    }

    [Fact]
    public void ParseEndpoint_UnclosedQuote_FallsBackToSpaceSplit() {
        // 引号未闭合 → closingQuote = -1, 不满足 > 0, 回退到空格拆分
        var (fileName, args) = McpStdioClient.ParseEndpoint("\"unclosed node script.js");
        fileName.Should().Be("\"unclosed");
        args.Should().Be("node script.js");
    }
}
