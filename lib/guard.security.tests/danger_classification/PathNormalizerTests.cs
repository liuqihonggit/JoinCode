namespace Guard.Security.Tests;

/// <summary>
/// PathNormalizer 单元测试 — 验证路径归一化工具跨分隔符行为
/// </summary>
public class PathNormalizerTests
{
    [Theory]
    [InlineData("src/", "src")]
    [InlineData("src\\", "src")]
    [InlineData("a/b/c/", "c")]
    [InlineData("a\\b\\c", "c")]
    [InlineData("D:\\proj\\SRC", "SRC")]
    [InlineData("D:/proj/SRC", "SRC")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void GetLeafName_Should_Handle_Mixed_Separators(string path, string expected)
    {
        PathNormalizer.GetLeafName(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("src/", "src")]
    [InlineData("src\\", "src")]
    [InlineData(" src ", "src")]
    [InlineData("a/b/c/", "a/b/c")]
    [InlineData("", "")]
    public void TrimTrailingSeparators_Should_Trim(string path, string expected)
    {
        PathNormalizer.TrimTrailingSeparators(path).Should().Be(expected);
    }

    [Fact]
    public void EqualsOrdinal_Should_Be_Case_Sensitive_And_Separator_Insensitive()
    {
        PathNormalizer.EqualsOrdinal("D:\\proj\\src", "D:\\proj\\src\\").Should().BeTrue();
        PathNormalizer.EqualsOrdinal("D:\\proj\\src", "D:/proj/src").Should().BeTrue();
        PathNormalizer.EqualsOrdinal("D:\\proj\\src", "D:\\proj\\SRC").Should().BeFalse();
    }

    [Fact]
    public void EqualsIgnoreCase_Should_Ignore_Case_And_Separator()
    {
        PathNormalizer.EqualsIgnoreCase("D:\\proj\\src", "D:/proj/SRC").Should().BeTrue();
    }

    [Fact]
    public void Normalize_Should_Remove_Trailing_Separator()
    {
        var normalized = PathNormalizer.Normalize("D:\\proj\\src\\");
        normalized.Should().NotEndWith("\\");
        normalized.Should().NotEndWith("/");
    }

    [Theory]
    [InlineData("a\\b\\c/b", "b")]
    [InlineData("a/b/c\\d", "d")]
    [InlineData("D:\\proj\\a/b\\c", "c")]
    public void GetLeafName_Should_Handle_Mixed_Separators_In_Single_Path(string path, string expected)
    {
        PathNormalizer.GetLeafName(path).Should().Be(expected);
    }

    [Fact]
    public void Normalize_Should_Unify_Mixed_Separators_To_Platform_Separator()
    {
        var normalized = PathNormalizer.Normalize("D:\\proj\\a\\b/c");
        normalized.Should().NotContain("/");
        normalized.Should().Contain("\\");
    }
}
