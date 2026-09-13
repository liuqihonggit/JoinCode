namespace Mcp.Tests;

/// <summary>
/// HttpTransportOptions 单元测试 — 验证 MCP 2025-11-25 协议版本默认值与 MCP-Protocol-Version 头部配置
/// </summary>
public class HttpTransportOptionsTests
{
    [Fact]
    public void ProtocolVersion_Default_IsCurrentProtocolVersion()
    {
        var options = new HttpTransportOptions();
        options.ProtocolVersion.Should().Be(McpProtocolVersion.Current);
    }

    [Fact]
    public void ProtocolVersion_Default_Is2025_11_25()
    {
        var options = new HttpTransportOptions();
        options.ProtocolVersion.Should().Be("2025-11-25");
    }

    [Fact]
    public void ProtocolVersion_CanBeOverridden()
    {
        var options = new HttpTransportOptions { ProtocolVersion = McpProtocolVersion.V2025_06_18 };
        options.ProtocolVersion.Should().Be("2025-06-18");
    }

    [Fact]
    public void McpProtocolVersion_Supported_ContainsCurrent()
    {
        McpProtocolVersion.Supported.Should().Contain(McpProtocolVersion.Current);
    }

    [Fact]
    public void McpProtocolVersion_Supported_ContainsAllNewVersions()
    {
        McpProtocolVersion.Supported.Should().Contain(new[]
        {
            McpProtocolVersion.V2025_11_25,
            McpProtocolVersion.V2025_06_18,
            McpProtocolVersion.V2025_03_26
        });
    }

    [Fact]
    public void McpProtocolVersion_Supported_DoesNotContainLegacy2024()
    {
        McpProtocolVersion.Supported.Should().NotContain(McpProtocolVersion.V2024_11_05);
    }

    [Fact]
    public void StatelessMode_Default_IsFalse()
    {
        var options = new HttpTransportOptions();
        options.StatelessMode.Should().BeFalse();
    }

    [Fact]
    public void HttpTransport_IsStateless_WhenStatelessMode_True()
    {
        var options = new HttpTransportOptions { Endpoint = "http://localhost:9999", StatelessMode = true };
        var transport = new HttpTransport(options);
        transport.IsStateless.Should().BeTrue();
    }

    [Fact]
    public void HttpTransport_IsStateless_WhenNoSession_True()
    {
        var options = new HttpTransportOptions { Endpoint = "http://localhost:9999" };
        var transport = new HttpTransport(options);
        transport.IsStateless.Should().BeTrue();
    }
}
