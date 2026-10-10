namespace Host.Tests.Cli;

public sealed class RgCoreTests {
    [Fact]
    public void IsAscii_PureAsciiData_ShouldReturnTrue() {
        var data = "Hello, World!"u8;
        RgCore.IsAscii(data).Should().BeTrue();
    }

    [Fact]
    public void IsAscii_WithUtf8Multibyte_ShouldReturnFalse() {
        var data = "Hello\xc3\xa9World"u8;
        RgCore.IsAscii(data).Should().BeFalse();
    }

    [Fact]
    public void IsAscii_EmptyData_ShouldReturnTrue() {
        RgCore.IsAscii([]).Should().BeTrue();
    }

    [Fact]
    public void IsAscii_OnlyHighBytes_ShouldReturnFalse() {
        var data = new byte[] { 0x80, 0x81, 0x82 };
        RgCore.IsAscii(data).Should().BeFalse();
    }

    [Fact]
    public void ContainsNullByte_WithNull_ShouldReturnTrue() {
        var data = new byte[] { 0x41, 0x00, 0x42 };
        RgCore.ContainsNullByte(data).Should().BeTrue();
    }

    [Fact]
    public void ContainsNullByte_WithoutNull_ShouldReturnFalse() {
        var data = "Hello, World!"u8;
        RgCore.ContainsNullByte(data).Should().BeFalse();
    }

    [Fact]
    public void ContainsNullByte_EmptyData_ShouldReturnFalse() {
        RgCore.ContainsNullByte([]).Should().BeFalse();
    }

    [Fact]
    public void DetectBom_WithBom_ShouldReturn3() {
        var data = new byte[] { 0xEF, 0xBB, 0xBF, 0x48, 0x65 };
        RgCore.DetectBom(data).Should().Be(3);
    }

    [Fact]
    public void DetectBom_WithoutBom_ShouldReturn0() {
        var data = "Hello"u8;
        RgCore.DetectBom(data).Should().Be(0);
    }

    [Fact]
    public void DetectBom_TooShort_ShouldReturn0() {
        var data = new byte[] { 0xEF, 0xBB };
        RgCore.DetectBom(data).Should().Be(0);
    }

    [Fact]
    public void DetectBom_Empty_ShouldReturn0() {
        RgCore.DetectBom([]).Should().Be(0);
    }

    [Fact]
    public void IsLineMatch_RegexMatch_ShouldReturnTrue() {
        var regex = new Regex("foo");
        RgCore.IsLineMatch("foobar".AsSpan(), regex, default, false, false).Should().BeTrue();
    }

    [Fact]
    public void IsLineMatch_RegexNoMatch_ShouldReturnFalse() {
        var regex = new Regex("xyz");
        RgCore.IsLineMatch("foobar".AsSpan(), regex, default, false, false).Should().BeFalse();
    }

    [Fact]
    public void IsLineMatch_FastFixedMatch_ShouldReturnTrue() {
        var regex = new Regex("foo");
        RgCore.IsLineMatch("foobar".AsSpan(), regex, "foo".AsSpan(), true, false).Should().BeTrue();
    }

    [Fact]
    public void IsLineMatch_FastFixedNoMatch_ShouldReturnFalse() {
        var regex = new Regex("foo");
        RgCore.IsLineMatch("barbaz".AsSpan(), regex, "foo".AsSpan(), true, false).Should().BeFalse();
    }

    [Fact]
    public void IsLineMatch_InvertMatch_ShouldFlipResult() {
        var regex = new Regex("foo");
        RgCore.IsLineMatch("foobar".AsSpan(), regex, default, false, true).Should().BeFalse();
        RgCore.IsLineMatch("barbaz".AsSpan(), regex, default, false, true).Should().BeTrue();
    }

    [Fact]
    public void CanUseFastFixedString_OnlyFixedStrings_ShouldReturnTrue() {
        RgCore.CanUseFastFixedString(true, false, false, false).Should().BeTrue();
    }

    [Fact]
    public void CanUseFastFixedString_WithCaseInsensitive_ShouldReturnFalse() {
        RgCore.CanUseFastFixedString(true, true, false, false).Should().BeFalse();
    }

    [Fact]
    public void CanUseFastFixedString_WithWordRegexp_ShouldReturnFalse() {
        RgCore.CanUseFastFixedString(true, false, true, false).Should().BeFalse();
    }

    [Fact]
    public void CanUseFastFixedString_WithSmartCase_ShouldReturnFalse() {
        RgCore.CanUseFastFixedString(true, false, false, true).Should().BeFalse();
    }

    [Fact]
    public void CanUseFastFixedString_NotFixedStrings_ShouldReturnFalse() {
        RgCore.CanUseFastFixedString(false, false, false, false).Should().BeFalse();
    }
}
