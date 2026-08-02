namespace Host.Tests.ChatCommands;

/// <summary>
/// CostScope 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / CostScopeConstants 常量值
/// </summary>
public sealed class CostScopeExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_Today_Should_Return_today()
    {
        CostScope.Today.ToValue().Should().Be("today");
    }

    [Fact]
    public void ToValue_Session_Should_Return_session()
    {
        CostScope.Session.ToValue().Should().Be("session");
    }

    [Fact]
    public void ToValue_Total_Should_Return_total()
    {
        CostScope.Total.ToValue().Should().Be("total");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("today", CostScope.Today)]
    [InlineData("session", CostScope.Session)]
    [InlineData("total", CostScope.Total)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, CostScope expected)
    {
        CostScopeExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void FromValue_Should_Be_CaseInsensitive()
    {
        CostScopeExtensions.FromValue("TODAY").Should().Be(CostScope.Today);
        CostScopeExtensions.FromValue("Session").Should().Be(CostScope.Session);
        CostScopeExtensions.FromValue("TOTAL").Should().Be(CostScope.Total);
    }

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        CostScopeExtensions.FromValue("invalid").Should().BeNull();
    }

    [Fact]
    public void FromValue_EmptyString_Should_Return_Null()
    {
        CostScopeExtensions.FromValue("").Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        CostScopeExtensions.FromValue(null).Should().BeNull();
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(CostScope.Today, true)]
    [InlineData(CostScope.Session, true)]
    [InlineData(CostScope.Total, true)]
    public void IsDefined_AllValidValues_Should_Return_True(CostScope value, bool expected)
    {
        CostScopeExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== CostScopeConstants 测试 =====

    [Fact]
    public void Constants_Should_Match_EnumValues()
    {
        CostScopeConstants.Today.Should().Be("today");
        CostScopeConstants.Session.Should().Be("session");
        CostScopeConstants.Total.Should().Be("total");
    }

    // ===== 往返一致性测试 =====

    [Theory]
    [InlineData(CostScope.Today)]
    [InlineData(CostScope.Session)]
    [InlineData(CostScope.Total)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(CostScope value)
    {
        var str = value.ToValue();
        CostScopeExtensions.FromValue(str).Should().Be(value);
    }

    // ===== 数量验证 =====

    [Fact]
    public void AllValues_Should_Be_3()
    {
        var values = Enum.GetValues<CostScope>();
        values.Should().HaveCount(3);
    }
}
