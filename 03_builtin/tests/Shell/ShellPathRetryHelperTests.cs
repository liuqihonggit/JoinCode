namespace Hands.Tests.Shell;

/// <summary>
/// ShellPathRetryHelper 单元测试 — 检测路径错误 + 归一化命令路径分隔符
/// </summary>
public class ShellPathRetryHelperTests
{
    #region IsPathError

    [Fact]
    public void IsPathError_NullResult_ReturnsFalse()
    {
        ShellPathRetryHelper.IsPathError(null).Should().BeFalse();
    }

    [Fact]
    public void IsPathError_SuccessResult_ReturnsFalse()
    {
        var result = ToolResultBuilder.Success().WithText("done").Build();

        ShellPathRetryHelper.IsPathError(result).Should().BeFalse();
    }

    [Fact]
    public void IsPathError_ErrorWithoutKeyword_ReturnsFalse()
    {
        var result = ToolResultBuilder.Error().WithText("permission denied").Build();

        ShellPathRetryHelper.IsPathError(result).Should().BeFalse();
    }

    [Fact]
    public void IsPathError_ErrorWithEmptyText_ReturnsFalse()
    {
        var result = ToolResultBuilder.Error().Build();

        ShellPathRetryHelper.IsPathError(result).Should().BeFalse();
    }

    [Theory]
    [InlineData("No such file or directory")]
    [InlineData("cat: src/file.txt: No such file or directory")]
    [InlineData("The system cannot find the path specified")]
    [InlineData("系统找不到指定的路径")]
    [InlineData("系统找不到指定的文件")]
    [InlineData("Could not find a part of the path 'D:\\proj'")]
    public void IsPathError_ErrorWithKeyword_ReturnsTrue(string errorText)
    {
        var result = ToolResultBuilder.Error().WithText(errorText).Build();

        ShellPathRetryHelper.IsPathError(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("NOT FOUND")]
    [InlineData("Cannot Find")]
    public void IsPathError_CaseInsensitive_ReturnsTrue(string errorText)
    {
        var result = ToolResultBuilder.Error().WithText(errorText).Build();

        ShellPathRetryHelper.IsPathError(result).Should().BeTrue();
    }

    #endregion

    #region TryNormalizeCommand

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalizeCommand_EmptyOrWhitespace_ReturnsNull(string command)
    {
        ShellPathRetryHelper.TryNormalizeCommand(command, toForwardSlash: true).Should().BeNull();
    }

    [Fact]
    public void TryNormalizeCommand_Null_ReturnsNull()
    {
        ShellPathRetryHelper.TryNormalizeCommand(null!, toForwardSlash: true).Should().BeNull();
    }

    [Theory]
    [InlineData("ls -la")]
    [InlineData("cat src/file.txt")]
    [InlineData("cat src\\file.txt")]
    public void TryNormalizeCommand_NoMixedSeparators_ReturnsNull(string command)
    {
        ShellPathRetryHelper.TryNormalizeCommand(command, toForwardSlash: true).Should().BeNull();
    }

    [Fact]
    public void TryNormalizeCommand_MixedSeparators_ToForwardSlash_ReturnsNormalized()
    {
        var result = ShellPathRetryHelper.TryNormalizeCommand("cat src\\file/d.txt", toForwardSlash: true);

        result.Should().Be("cat src/file/d.txt");
    }

    [Fact]
    public void TryNormalizeCommand_MixedSeparators_ToBackslash_ReturnsNormalized()
    {
        var result = ShellPathRetryHelper.TryNormalizeCommand("cat src\\file/d.txt", toForwardSlash: false);

        result.Should().Be("cat src\\file\\d.txt");
    }

    [Fact]
    public void TryNormalizeCommand_RealWorldMixedPath_ToForwardSlash_ReturnsNormalized()
    {
        var result = ShellPathRetryHelper.TryNormalizeCommand("cat D:\\project\\src/file.txt", toForwardSlash: true);

        result.Should().Be("cat D:/project/src/file.txt");
    }

    [Fact]
    public void TryNormalizeCommand_RealWorldMixedPath_ToBackslash_ReturnsNormalized()
    {
        var result = ShellPathRetryHelper.TryNormalizeCommand("cat D:\\project\\src/file.txt", toForwardSlash: false);

        result.Should().Be("cat D:\\project\\src\\file.txt");
    }

    #endregion
}
