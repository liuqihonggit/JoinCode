namespace Infra.Tests.Services;

/// <summary>
/// LineEndingDetector 单元测试 — 对齐 TS: fileRead.ts detectLineEndingsForString
/// </summary>
public class LineEndingDetectorTests
{
    [Fact]
    public void DetectFromString_AllLF_ReturnsLF()
    {
        var content = "line1\nline2\nline3\n";
        var result = LineEndingDetector.DetectFromString(content.AsSpan());
        Assert.Equal(LineEndingDetector.LineEndingType.LF, result);
    }

    [Fact]
    public void DetectFromString_AllCRLF_ReturnsCRLF()
    {
        var content = "line1\r\nline2\r\nline3\r\n";
        var result = LineEndingDetector.DetectFromString(content.AsSpan());
        Assert.Equal(LineEndingDetector.LineEndingType.CRLF, result);
    }

    [Fact]
    public void DetectFromString_MixedMoreCRLF_ReturnsCRLF()
    {
        // CRLF=2, LF=1 → CRLF wins
        var content = "line1\r\nline2\r\nline3\n";
        var result = LineEndingDetector.DetectFromString(content.AsSpan());
        Assert.Equal(LineEndingDetector.LineEndingType.CRLF, result);
    }

    [Fact]
    public void DetectFromString_MixedMoreLF_ReturnsLF()
    {
        // CRLF=1, LF=2 → LF wins
        var content = "line1\r\nline2\nline3\n";
        var result = LineEndingDetector.DetectFromString(content.AsSpan());
        Assert.Equal(LineEndingDetector.LineEndingType.LF, result);
    }

    [Fact]
    public void DetectFromString_EqualCRLFAndLF_ReturnsLF()
    {
        // CRLF=1, LF=1 → tie goes to LF (TS default)
        var content = "line1\r\nline2\n";
        var result = LineEndingDetector.DetectFromString(content.AsSpan());
        Assert.Equal(LineEndingDetector.LineEndingType.LF, result);
    }

    [Fact]
    public void DetectFromString_NoNewlines_ReturnsLF()
    {
        var content = "no newlines here";
        var result = LineEndingDetector.DetectFromString(content.AsSpan());
        Assert.Equal(LineEndingDetector.LineEndingType.LF, result);
    }

    [Fact]
    public void DetectFromString_Empty_ReturnsLF()
    {
        var content = "";
        var result = LineEndingDetector.DetectFromString(content.AsSpan());
        Assert.Equal(LineEndingDetector.LineEndingType.LF, result);
    }

    [Fact]
    public void DetectFromString_OnlyScansFirst4096Chars()
    {
        // 对齐 TS: 只扫描前 4096 字符
        // 前 4096 字符全是 CRLF，之后全是 LF
        var crlfPart = new string('a', 50) + "\r\n";
        var lfPart = new string('b', 50) + "\n";
        // 构造前4096字符内CRLF占多数
        var content = string.Concat(Enumerable.Repeat(crlfPart, 40))  // ~2040 chars, 40 CRLF
                      + string.Concat(Enumerable.Repeat(lfPart, 10))  // ~510 chars, 10 LF
                      + string.Concat(Enumerable.Repeat(lfPart, 100)); // 4096+ chars, 100 more LF
        var result = LineEndingDetector.DetectFromString(content.AsSpan());
        // 前4096字符内CRLF=40 > LF=10，应返回CRLF
        Assert.Equal(LineEndingDetector.LineEndingType.CRLF, result);
    }

    [Fact]
    public void RestoreLineEndings_LF_NoChange()
    {
        var content = "line1\nline2\n";
        var result = LineEndingDetector.RestoreLineEndings(content, LineEndingDetector.LineEndingType.LF);
        Assert.Equal("line1\nline2\n", result);
    }

    [Fact]
    public void RestoreLineEndings_CRLF_ConvertsLFToCRLF()
    {
        var content = "line1\nline2\n";
        var result = LineEndingDetector.RestoreLineEndings(content, LineEndingDetector.LineEndingType.CRLF);
        Assert.Equal("line1\r\nline2\r\n", result);
    }

    [Fact]
    public void RestoreLineEndings_CRLF_PreventsDoubleConversion()
    {
        // 对齐 TS: 先将 \r\n 归一化为 \n，再替换为 \r\n
        // 防止 \r\r\n 双重转换
        var content = "line1\r\nline2\n";
        var result = LineEndingDetector.RestoreLineEndings(content, LineEndingDetector.LineEndingType.CRLF);
        Assert.Equal("line1\r\nline2\r\n", result);
    }

    [Fact]
    public void RestoreLineEndings_CRLF_AlreadyCRLF_NoDoubleConvert()
    {
        var content = "line1\r\nline2\r\n";
        var result = LineEndingDetector.RestoreLineEndings(content, LineEndingDetector.LineEndingType.CRLF);
        Assert.Equal("line1\r\nline2\r\n", result);
    }
}
