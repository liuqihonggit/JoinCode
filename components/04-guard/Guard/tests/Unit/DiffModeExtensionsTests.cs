namespace Host.Tests.ChatCommands;

/// <summary>
/// DiffMode 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / DiffModeConstants 常量值
/// </summary>
public sealed class DiffModeExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_Files_Should_Return_files()
    {
        DiffMode.Files.ToValue().Should().Be("files");
    }

    [Fact]
    public void ToValue_Cached_Should_Return_cached()
    {
        DiffMode.Cached.ToValue().Should().Be("cached");
    }

    [Fact]
    public void ToValue_Staged_Should_Return_staged()
    {
        DiffMode.Staged.ToValue().Should().Be("staged");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("files", DiffMode.Files)]
    [InlineData("cached", DiffMode.Cached)]
    [InlineData("staged", DiffMode.Staged)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, DiffMode expected)
    {
        DiffModeExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void FromValue_Should_Be_CaseInsensitive()
    {
        DiffModeExtensions.FromValue("FILES").Should().Be(DiffMode.Files);
        DiffModeExtensions.FromValue("Cached").Should().Be(DiffMode.Cached);
        DiffModeExtensions.FromValue("STAGED").Should().Be(DiffMode.Staged);
    }

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        DiffModeExtensions.FromValue("invalid").Should().BeNull();
    }

    [Fact]
    public void FromValue_EmptyString_Should_Return_Null()
    {
        DiffModeExtensions.FromValue("").Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        DiffModeExtensions.FromValue(null).Should().BeNull();
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(DiffMode.Files, true)]
    [InlineData(DiffMode.Cached, true)]
    [InlineData(DiffMode.Staged, true)]
    public void IsDefined_AllValidValues_Should_Return_True(DiffMode value, bool expected)
    {
        DiffModeExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== DiffModeConstants 测试 =====

    [Fact]
    public void Constants_Should_Match_EnumValues()
    {
        DiffModeConstants.Files.Should().Be("files");
        DiffModeConstants.Cached.Should().Be("cached");
        DiffModeConstants.Staged.Should().Be("staged");
    }

    // ===== 往返一致性测试 =====

    [Theory]
    [InlineData(DiffMode.Files)]
    [InlineData(DiffMode.Cached)]
    [InlineData(DiffMode.Staged)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(DiffMode value)
    {
        var str = value.ToValue();
        DiffModeExtensions.FromValue(str).Should().Be(value);
    }

    // ===== 数量验证 =====

    [Fact]
    public void AllValues_Should_Be_3()
    {
        var values = Enum.GetValues<DiffMode>();
        values.Should().HaveCount(3);
    }
}
