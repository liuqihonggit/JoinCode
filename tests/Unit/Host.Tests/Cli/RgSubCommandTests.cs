namespace Host.Tests.Cli;

using JoinCode.CliCommands;

public sealed class RgSubCommandTests
{
    [Theory]
    [InlineData(@"finally\s*\{", @"finally\s*\{")]
    [InlineData(@"finally\\s*\\{", @"finally\s*\{")]
    [InlineData(@"\d+\w+", @"\d+\w+")]
    [InlineData(@"\\d+\\w+", @"\d+\w+")]
    [InlineData(@"class\s+\w+Service", @"class\s+\w+Service")]
    [InlineData(@"class\\s+\\w+Service", @"class\s+\w+Service")]
    [InlineData(@"^\s*finally", @"^\s*finally")]
    [InlineData(@"^\\s*finally", @"^\s*finally")]
    [InlineData(@"foo", "foo")]
    [InlineData("", "")]
    [InlineData("no backslash", "no backslash")]
    public void FixPowerShellEscaping_ShouldNormalizeDoubleBackslash(string input, string expected)
    {
        RgSubCommand.FixPowerShellEscaping(input).Should().Be(expected);
    }

    [Fact]
    public void FixPowerShellEscaping_ShouldHandleMixedEscaping()
    {
        var input = @"finally\\s*\{\\d";
        var result = RgSubCommand.FixPowerShellEscaping(input);
        result.Should().Be(@"finally\s*\{\d");
    }

    [Fact]
    public void FixPowerShellEscaping_ShouldNotTouchSingleBackslashBeforeNonMeta()
    {
        var input = @"foo\bar";
        RgSubCommand.FixPowerShellEscaping(input).Should().Be(@"foo\bar");
    }

    [Fact]
    public void FixPowerShellEscaping_ShouldNotTouchTrailingDoubleBackslash()
    {
        var input = @"foo\\";
        RgSubCommand.FixPowerShellEscaping(input).Should().Be(@"foo\\");
    }

    [Theory]
    [InlineData("C:\\", true)]
    [InlineData("C:/", true)]
    [InlineData("D:\\", true)]
    [InlineData("/", true)]
    [InlineData("C:\\project", false)]
    [InlineData("C:\\project\\w3", false)]
    [InlineData("/home/user", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    [InlineData("src", false)]
    public void IsRootPath_ShouldDetectRootPaths(string path, bool expected)
    {
        RgSubCommand.IsRootPath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData('s', true)]
    [InlineData('d', true)]
    [InlineData('w', true)]
    [InlineData('{', true)]
    [InlineData('}', true)]
    [InlineData('.', true)]
    [InlineData('+', true)]
    [InlineData('*', true)]
    [InlineData('?', true)]
    [InlineData('|', true)]
    [InlineData('^', true)]
    [InlineData('$', true)]
    [InlineData('n', true)]
    [InlineData('r', true)]
    [InlineData('t', true)]
    [InlineData('a', false)]
    [InlineData('z', true)]
    [InlineData('x', true)]
    [InlineData('p', true)]
    [InlineData('P', true)]
    [InlineData('b', true)]
    [InlineData('B', true)]
    [InlineData('A', true)]
    [InlineData('Z', true)]
    [InlineData('G', true)]
    [InlineData('k', true)]
    [InlineData('u', true)]
    [InlineData('c', true)]
    [InlineData('f', true)]
    [InlineData('v', true)]
    [InlineData('0', true)]
    [InlineData('[', true)]
    [InlineData(']', true)]
    [InlineData('(', true)]
    [InlineData(')', true)]
    [InlineData('m', false)]
    [InlineData('q', false)]
    [InlineData('y', false)]
    public void IsRegexMetaChar_ShouldClassifyCorrectly(char c, bool expected)
    {
        RgSubCommand.IsRegexMetaChar(c).Should().Be(expected);
    }
}
