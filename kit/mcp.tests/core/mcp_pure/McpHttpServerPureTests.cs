namespace Mcp.Tests;

/// <summary>
/// McpHttpServer 纯计算单元测试 — 验证 IsInitializeRequest 静态判定
/// </summary>
public sealed class McpHttpServerPureTests {
    [Fact]
    public void IsInitializeRequest_ValidInitializeBody_ReturnsTrue() {
        var body = """{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"2025-11-25"}}""";
        McpHttpServer.IsInitializeRequest(body).Should().BeTrue();
    }

    [Fact]
    public void IsInitializeRequest_MethodWithoutId_ReturnsTrue() {
        // 只检查包含 "method" 和 "initialize",不检查 id
        var body = """{"jsonrpc":"2.0","method":"initialize","params":{}}""";
        McpHttpServer.IsInitializeRequest(body).Should().BeTrue();
    }

    [Fact]
    public void IsInitializeRequest_ToolsListMethod_ReturnsFalse() {
        var body = """{"jsonrpc":"2.0","method":"tools/list","id":1}""";
        McpHttpServer.IsInitializeRequest(body).Should().BeFalse();
    }

    [Fact]
    public void IsInitializeRequest_NoMethod_ReturnsFalse() {
        var body = """{"jsonrpc":"2.0","result":{}}""";
        McpHttpServer.IsInitializeRequest(body).Should().BeFalse();
    }

    [Fact]
    public void IsInitializeRequest_InitializeSubstringInOtherField_ReturnsTrue() {
        // 实现只检查 body 同时包含 "method" 和 "initialize" 子串
        var body = """{"method":"initialize"}""";
        McpHttpServer.IsInitializeRequest(body).Should().BeTrue();
    }

    [Fact]
    public void IsInitializeRequest_OnlyMethodKeyword_ReturnsFalse() {
        var body = """{"method":"tools/call"}""";
        McpHttpServer.IsInitializeRequest(body).Should().BeFalse();
    }

    [Fact]
    public void IsInitializeRequest_OnlyInitializeWord_ReturnsFalse() {
        var body = """{"result":"initialize done"}""";
        McpHttpServer.IsInitializeRequest(body).Should().BeFalse();
    }

    [Fact]
    public void IsInitializeRequest_EmptyString_ReturnsFalse() {
        McpHttpServer.IsInitializeRequest(string.Empty).Should().BeFalse();
    }
}
