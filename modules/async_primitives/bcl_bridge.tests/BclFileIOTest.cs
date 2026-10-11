namespace BclBridge.Tests;

/// <summary>
/// BclFileIO 确定性单元测试 — 覆盖 DetectFromBOM 4 分支+边界、DecodeBytes 端到端、DecodeBytesWithEncoding
/// <para>纯函数测试:不依赖时序/IO/异步,给定字节输入→断言编码/字符串输出</para>
/// </summary>
public class BclFileIOTest {
    private static readonly BclFileIO Io = BclFileIO.Instance;

    // === DetectFromBOM:4 分支 BOM 检测(引用相等验证返回已知静态实例)===

    [Fact]
    public void DetectFromBOM_Utf8BOM_ReturnsUtf8Encoding() {
        Assert.Same(Encoding.UTF8, BclFileIO.DetectFromBOM([0xEF, 0xBB, 0xBF]));
    }

    [Fact]
    public void DetectFromBOM_Utf16LEBOM_ReturnsUnicodeEncoding() {
        Assert.Same(Encoding.Unicode, BclFileIO.DetectFromBOM([0xFF, 0xFE]));
    }

    [Fact]
    public void DetectFromBOM_Utf16BEBOM_ReturnsBigEndianUnicodeEncoding() {
        Assert.Same(Encoding.BigEndianUnicode, BclFileIO.DetectFromBOM([0xFE, 0xFF]));
    }

    [Fact]
    public void DetectFromBOM_NoBOM_ReturnsDefaultUtf8Encoding() {
        Assert.Same(Encoding.UTF8, BclFileIO.DetectFromBOM([0x41, 0x42, 0x43]));
    }

    // === DetectFromBOM:边界 ===

    [Fact]
    public void DetectFromBOM_EmptyArray_ReturnsDefaultUtf8Encoding() {
        Assert.Same(Encoding.UTF8, BclFileIO.DetectFromBOM([]));
    }

    [Fact]
    public void DetectFromBOM_SingleByte_ReturnsDefaultUtf8Encoding() {
        // 单字节无法构成任何 BOM,回落 UTF8
        Assert.Same(Encoding.UTF8, BclFileIO.DetectFromBOM([0xEF]));
    }

    [Fact]
    public void DetectFromBOM_TwoBytesPartialUtf8BOM_ReturnsDefaultUtf8Encoding() {
        // EF BB 是 UTF8 BOM 前两字节但不完整,不应识别为 UTF8 BOM
        Assert.Same(Encoding.UTF8, BclFileIO.DetectFromBOM([0xEF, 0xBB]));
    }

    [Fact]
    public void DetectFromBOM_Utf8BOMWithContent_ReturnsUtf8Encoding() {
        Assert.Same(Encoding.UTF8, BclFileIO.DetectFromBOM([0xEF, 0xBB, 0xBF, 0x41]));
    }

    [Fact]
    public void DetectFromBOM_Utf16LEBOMWithContent_ReturnsUnicodeEncoding() {
        Assert.Same(Encoding.Unicode, BclFileIO.DetectFromBOM([0xFF, 0xFE, 0x41, 0x00]));
    }

    [Fact]
    public void DetectFromBOM_Utf16BEBOMWithContent_ReturnsBigEndianUnicodeEncoding() {
        Assert.Same(Encoding.BigEndianUnicode, BclFileIO.DetectFromBOM([0xFE, 0xFF, 0x00, 0x41]));
    }

    // === DetectFromBOM:参数化覆盖(MemberData,因 attribute 参数不支持 byte[])===

    public static IEnumerable<object[]> DetectFromBOM_ParamData => new[] {
        new object[] { new byte[] { 0xEF, 0xBB, 0xBF }, "utf-8" },
        new object[] { new byte[] { 0xFF, 0xFE }, "utf-16" },
        new object[] { new byte[] { 0xFE, 0xFF }, "utf-16BE" },
        new object[] { new byte[] { 0x41, 0x42 }, "utf-8" },
        new object[] { Array.Empty<byte>(), "utf-8" },
        new object[] { new byte[] { 0xEF }, "utf-8" },
        new object[] { new byte[] { 0xEF, 0xBB }, "utf-8" }
    };

    [Theory]
    [MemberData(nameof(DetectFromBOM_ParamData))]
    public void DetectFromBOM_Param_ReturnsExpectedWebName(byte[] bytes, string expectedWebName) {
        Assert.Equal(expectedWebName, BclFileIO.DetectFromBOM(bytes).WebName);
    }

    // === DecodeBytes:端到端解码(BOM + 实际内容)===

    [Fact]
    public void DecodeBytes_Utf8BOMWithChineseContent_ReturnsContentAndEncoding() {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("中文测试")).ToArray();
        var (content, encoding) = Io.DecodeBytes(bytes);
        Assert.Equal("中文测试", content);
        Assert.Same(Encoding.UTF8, encoding);
    }

    [Fact]
    public void DecodeBytes_Utf16LEBOMWithContent_ReturnsContentAndEncoding() {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("abc")).ToArray();
        var (content, encoding) = Io.DecodeBytes(bytes);
        Assert.Equal("abc", content);
        Assert.Same(Encoding.Unicode, encoding);
    }

    [Fact]
    public void DecodeBytes_Utf16BEBOMWithContent_ReturnsContentAndEncoding() {
        var bytes = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("abc")).ToArray();
        var (content, encoding) = Io.DecodeBytes(bytes);
        Assert.Equal("abc", content);
        Assert.Same(Encoding.BigEndianUnicode, encoding);
    }

    [Fact]
    public void DecodeBytes_NoBOMUtf8Content_ReturnsContentAndDefaultEncoding() {
        var bytes = Encoding.UTF8.GetBytes("Hello World");
        var (content, encoding) = Io.DecodeBytes(bytes);
        Assert.Equal("Hello World", content);
        Assert.Same(Encoding.UTF8, encoding);
    }

    [Fact]
    public void DecodeBytes_EmptyArray_ReturnsEmptyStringAndDefaultEncoding() {
        var (content, encoding) = Io.DecodeBytes([]);
        Assert.Equal(string.Empty, content);
        Assert.Same(Encoding.UTF8, encoding);
    }

    [Fact]
    public void DecodeBytes_Utf8BOMOnlyNoContent_ReturnsEmptyString() {
        // 仅 BOM 无内容,StreamReader 跳过 BOM 后读到空字符串
        var (content, encoding) = Io.DecodeBytes(Encoding.UTF8.GetPreamble());
        Assert.Equal(string.Empty, content);
        Assert.Same(Encoding.UTF8, encoding);
    }

    // === DecodeBytesWithEncoding:指定编码解码 ===

    [Fact]
    public void DecodeBytesWithEncoding_Utf8_ReturnsContent() {
        var bytes = Encoding.UTF8.GetBytes("Hello");
        Assert.Equal("Hello", Io.DecodeBytesWithEncoding(bytes, Encoding.UTF8));
    }

    [Fact]
    public void DecodeBytesWithEncoding_Utf16LE_ReturnsContent() {
        var bytes = Encoding.Unicode.GetBytes("abc");
        Assert.Equal("abc", Io.DecodeBytesWithEncoding(bytes, Encoding.Unicode));
    }

    [Fact]
    public void DecodeBytesWithEncoding_Ascii_ReturnsContent() {
        var bytes = Encoding.ASCII.GetBytes("abc");
        Assert.Equal("abc", Io.DecodeBytesWithEncoding(bytes, Encoding.ASCII));
    }

    [Fact]
    public void DecodeBytesWithEncoding_Latin1_ReturnsContent() {
        var bytes = Encoding.Latin1.GetBytes("abc");
        Assert.Equal("abc", Io.DecodeBytesWithEncoding(bytes, Encoding.Latin1));
    }

    [Fact]
    public void DecodeBytesWithEncoding_EmptyArray_ReturnsEmptyString() {
        Assert.Equal(string.Empty, Io.DecodeBytesWithEncoding([], Encoding.UTF8));
    }

    [Fact]
    public void DecodeBytesWithEncoding_Latin1HighByte_DecodesAsLatin1NotUtf8() {
        // 0xE9 在 Latin1 = é(U+00E9);在 UTF8 是非法前导字节→替换字符
        // 验证 Latin1 编码确实被使用,而非回退到 UTF8
        var result = Io.DecodeBytesWithEncoding([0xE9], Encoding.Latin1);
        Assert.Equal("é", result);
    }
}
