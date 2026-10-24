// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace Infra.Tests.Process;

[Trait("Category", "Unit")]
public sealed class CommandArgumentValidatorTests {
    [Theory]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData(";")]
    [InlineData("`")]
    [InlineData("$")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void ValidateString_WithDangerousChar_Throws(string dangerousStr) {
        var arg = "prefix" + dangerousStr + "suffix";

        var act = () => CommandArgumentValidator.ValidateString(arg);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*危险字符*");
    }

    [Fact]
    public void ValidateString_WithSafeArgument_DoesNotThrow() {
        var act = () => CommandArgumentValidator.ValidateString("--flag value");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateString_WithFilePath_DoesNotThrow() {
        var act = () => CommandArgumentValidator.ValidateString(@"C:\Users\test\file.txt");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateString_WithUnixPath_DoesNotThrow() {
        var act = () => CommandArgumentValidator.ValidateString("/home/user/file.txt");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateString_WithEmpty_DoesNotThrow() {
        var act = () => CommandArgumentValidator.ValidateString("");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateList_WithDangerousChar_Throws() {
        var args = new[] { "--flag", "foo & bar" };

        var act = () => CommandArgumentValidator.ValidateList(args);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*危险字符*");
    }

    [Fact]
    public void ValidateList_WithSafeArgs_DoesNotThrow() {
        var args = new[] { "--flag", "value", @"C:\path\file.txt" };

        var act = () => CommandArgumentValidator.ValidateList(args);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateList_WithNull_DoesNotThrow() {
        var act = () => CommandArgumentValidator.ValidateList(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateList_WithEmpty_DoesNotThrow() {
        var act = () => CommandArgumentValidator.ValidateList(Array.Empty<string>());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData(";")]
    [InlineData("`")]
    [InlineData("$")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("<")]
    [InlineData(">")]
    public void DangerousChars_ContainsAllExpected(string ch) {
        CommandArgumentValidator.DangerousChars.Should().Contain(ch[0]);
    }

    [Fact]
    public void DangerousChars_DoesNotContainSafeChars() {
        CommandArgumentValidator.DangerousChars.Should().NotContain(' ');
        CommandArgumentValidator.DangerousChars.Should().NotContain('-');
        CommandArgumentValidator.DangerousChars.Should().NotContain('/');
        CommandArgumentValidator.DangerousChars.Should().NotContain('\\');
        CommandArgumentValidator.DangerousChars.Should().NotContain(':');
        CommandArgumentValidator.DangerousChars.Should().NotContain('.');
    }
}