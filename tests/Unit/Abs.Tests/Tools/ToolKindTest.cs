namespace Abs.Tests.Tools;

/// <summary>
/// ToolKind 枚举单元测试 — 验证 EnumValue 特性值、源码生成器输出的 Constants 和 Extensions
/// </summary>
public sealed class ToolKindTest
{
    // === ToValue 正向映射 ===

    [Fact]
    public void ToValue_System_ReturnsSystem()
    {
        ToolKind.System.ToValue().Should().Be("system");
    }

    [Fact]
    public void ToValue_Mcp_ReturnsMcp()
    {
        ToolKind.Mcp.ToValue().Should().Be("mcp");
    }

    [Fact]
    public void ToValue_OnError_ReturnsOnError()
    {
        ToolKind.OnError.ToValue().Should().Be("on_error");
    }

    // === FromValue 反向映射 ===

    [Fact]
    public void FromValue_System_ReturnsSystemEnum()
    {
        ToolKindExtensions.FromValue("system").Should().Be(ToolKind.System);
    }

    [Fact]
    public void FromValue_Mcp_ReturnsMcpEnum()
    {
        ToolKindExtensions.FromValue("mcp").Should().Be(ToolKind.Mcp);
    }

    [Fact]
    public void FromValue_OnError_ReturnsOnErrorEnum()
    {
        ToolKindExtensions.FromValue("on_error").Should().Be(ToolKind.OnError);
    }

    [Fact]
    public void FromValue_Null_ReturnsNull()
    {
        ToolKindExtensions.FromValue(null).Should().BeNull();
    }

    [Fact]
    public void FromValue_Unknown_ReturnsNull()
    {
        ToolKindExtensions.FromValue("unknown").Should().BeNull();
    }

    [Fact]
    public void FromValue_CaseInsensitive_ReturnsCorrectEnum()
    {
        ToolKindExtensions.FromValue("SYSTEM").Should().Be(ToolKind.System);
        ToolKindExtensions.FromValue("Mcp").Should().Be(ToolKind.Mcp);
    }

    // === IsDefined ===

    [Fact]
    public void IsDefined_System_ReturnsTrue()
    {
        ToolKindExtensions.IsDefined(ToolKind.System).Should().BeTrue();
    }

    [Fact]
    public void IsDefined_Mcp_ReturnsTrue()
    {
        ToolKindExtensions.IsDefined(ToolKind.Mcp).Should().BeTrue();
    }

    [Fact]
    public void IsDefined_OnError_ReturnsTrue()
    {
        ToolKindExtensions.IsDefined(ToolKind.OnError).Should().BeTrue();
    }

    // === Constants 常量类 ===

    [Fact]
    public void Constants_System_EqualsSystem()
    {
        ToolKindConstants.System.Should().Be("system");
    }

    [Fact]
    public void Constants_Mcp_EqualsMcp()
    {
        ToolKindConstants.Mcp.Should().Be("mcp");
    }

    [Fact]
    public void Constants_OnError_EqualsOnError()
    {
        ToolKindConstants.OnError.Should().Be("on_error");
    }

    // === 往返一致性 ===

    [Fact]
    public void RoundTrip_AllValues_ToValueThenFromValue_ReturnsOriginal()
    {
        foreach (ToolKind kind in Enum.GetValues<ToolKind>())
        {
            var value = kind.ToValue();
            var restored = ToolKindExtensions.FromValue(value);
            restored.Should().Be(kind, $"ToValue('{kind}') → '{value}' → FromValue should return {kind}");
        }
    }

    [Fact]
    public void RoundTrip_AllConstants_FromValue_ReturnsCorrectEnum()
    {
        ToolKindExtensions.FromValue(ToolKindConstants.System).Should().Be(ToolKind.System);
        ToolKindExtensions.FromValue(ToolKindConstants.Mcp).Should().Be(ToolKind.Mcp);
        ToolKindExtensions.FromValue(ToolKindConstants.OnError).Should().Be(ToolKind.OnError);
    }
}
