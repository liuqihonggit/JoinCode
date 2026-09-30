namespace Abs.Tests.Constants;

/// <summary>
/// MCP 协议版本常量确定性测试 — 验证下沉到 abstractions 后常量值与原硬编码一致
/// <para>TASK031 阶段B4:常量委托统一</para>
/// </summary>
[Trait("Category", "Deterministic")]
public class McpProtocolVersionTest {
    [Fact]
    public void V2024_11_05_ShouldBeExpectedValue() {
        McpProtocolVersion.V2024_11_05.Should().Be("2024-11-05");
    }

    [Fact]
    public void V2025_03_26_ShouldBeExpectedValue() {
        McpProtocolVersion.V2025_03_26.Should().Be("2025-03-26");
    }

    [Fact]
    public void V2025_06_18_ShouldBeExpectedValue() {
        McpProtocolVersion.V2025_06_18.Should().Be("2025-06-18");
    }

    [Fact]
    public void V2025_11_25_ShouldBeExpectedValue() {
        McpProtocolVersion.V2025_11_25.Should().Be("2025-11-25");
    }

    [Fact]
    public void Current_ShouldBeV2025_11_25() {
        McpProtocolVersion.Current.Should().Be(McpProtocolVersion.V2025_11_25);
        McpProtocolVersion.Current.Should().Be("2025-11-25");
    }

    [Fact]
    public void Supported_ShouldContainCurrentAndRecentVersions() {
        McpProtocolVersion.Supported.Should().Contain(McpProtocolVersion.V2025_11_25);
        McpProtocolVersion.Supported.Should().Contain(McpProtocolVersion.V2025_06_18);
        McpProtocolVersion.Supported.Should().Contain(McpProtocolVersion.V2025_03_26);
    }

    [Fact]
    public void Supported_ShouldNotContainArchivedV2024_11_05() {
        McpProtocolVersion.Supported.Should().NotContain(McpProtocolVersion.V2024_11_05);
    }

    [Fact]
    public void Supported_ShouldHaveThreeVersions() {
        McpProtocolVersion.Supported.Should().HaveCount(3);
    }

    [Fact]
    public void Current_ShouldBeInSupported() {
        McpProtocolVersion.Supported.Should().Contain(McpProtocolVersion.Current);
    }
}
