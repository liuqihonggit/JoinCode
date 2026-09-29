namespace Mcp.Tests;

/// <summary>
/// McpHeadersHelper 单元测试 — 验证请求头合并(静态头 + 动态头,动态优先)
/// </summary>
public sealed class McpHeadersHelperTests {
    [Fact]
    public void CombineHeaders_BothNull_ReturnsEmpty() {
        var result = McpHeadersHelper.CombineHeaders(null, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void CombineHeaders_StaticOnly_ReturnsStatic() {
        var staticHeaders = new Dictionary<string, string> {
            ["Authorization"] = "Bearer token",
            ["X-Custom"] = "abc"
        };
        var result = McpHeadersHelper.CombineHeaders(staticHeaders, null);
        result.Should().HaveCount(2);
        result["Authorization"].Should().Be("Bearer token");
        result["X-Custom"].Should().Be("abc");
    }

    [Fact]
    public void CombineHeaders_DynamicOnly_ReturnsDynamic() {
        var dynamicHeaders = new Dictionary<string, string> {
            ["X-Trace-Id"] = "trace-123"
        };
        var result = McpHeadersHelper.CombineHeaders(null, dynamicHeaders);
        result.Should().HaveCount(1);
        result["X-Trace-Id"].Should().Be("trace-123");
    }

    [Fact]
    public void CombineHeaders_BothPresent_DynamicOverridesStatic() {
        var staticHeaders = new Dictionary<string, string> {
            ["Authorization"] = "static",
            ["X-Keep"] = "kept"
        };
        var dynamicHeaders = new Dictionary<string, string> {
            ["Authorization"] = "dynamic",
            ["X-New"] = "new"
        };
        var result = McpHeadersHelper.CombineHeaders(staticHeaders, dynamicHeaders);
        result.Should().HaveCount(3);
        result["Authorization"].Should().Be("dynamic");
        result["X-Keep"].Should().Be("kept");
        result["X-New"].Should().Be("new");
    }

    [Fact]
    public void CombineHeaders_BothEmpty_ReturnsEmpty() {
        var result = McpHeadersHelper.CombineHeaders(new(), new());
        result.Should().BeEmpty();
    }

    [Fact]
    public void CombineHeaders_StaticEmpty_DynamicOnly() {
        var dynamicHeaders = new Dictionary<string, string> { ["K"] = "V" };
        var result = McpHeadersHelper.CombineHeaders(new(), dynamicHeaders);
        result.Should().HaveCount(1);
        result["K"].Should().Be("V");
    }
}
