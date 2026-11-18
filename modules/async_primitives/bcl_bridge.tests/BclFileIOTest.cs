namespace BclBridge.Tests;

/// <summary>
/// BclFileIO 确定性单元测试 — 覆盖 DetectFromBOM 全部分支+边界、DecodeBytes 端到端、
/// 以及「有签名保持签名、没有就不加」的编解码往返不变量。
/// <para>纯函数测试:不依赖时序/IO/异步,给定字节输入→断言编码/字符串/回写字节</para>
/// </summary>
public class BclFileIOTest {
    private static readonly BclFileIO Io = BclFileIO.Instance;

    private static byte[] WithBom(Encoding encoding, string content)
        => encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray();

    // === DetectFromBOM:BOM 分支(有签名 → 带 preamble 的编码)===

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
    public void DetectFromBOM_Utf32LEBOM_ReturnsUtf32Encoding() {
        // FF FE 00 00 必须先于 UTF-16LE(FF FE) 判定，否则四字节 BOM 会被截成两字节
        var encoding = BclFileIO.DetectFromBOM([0xFF, 0xFE, 0x00, 0x00]);
        Assert.Equal(Encoding.UTF32, encoding);
    }

    [Fact]
    public void DetectFromBOM_Utf32BEBOM_ReturnsUtf32BigEndianEncoding() {
        var encoding = BclFileIO.DetectFromBOM([0x00, 0x00, 0xFE, 0xFF]);
        Assert.Equal(new UTF32Encoding(bigEndian: true, byteOrderMark: true), encoding);
    }

    // === DetectFromBOM:无签名 → 不得带 preamble（否则写回会凭空多出 BOM）===

    [Fact]
    public void DetectFromBOM_NoBOM_ReturnsEncodingWithoutPreamble() {
        var encoding = BclFileIO.DetectFromBOM([0x41, 0x42, 0x43]);
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void DetectFromBOM_EmptyArray_ReturnsEncodingWithoutPreamble() {
        var encoding = BclFileIO.DetectFromBOM([]);
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void DetectFromBOM_SingleByte_ReturnsEncodingWithoutPreamble() {
        // 单字节无法构成任何 BOM,回落无 BOM UTF-8
        Assert.Empty(BclFileIO.DetectFromBOM([0xEF]).GetPreamble());
    }

    [Fact]
    public void DetectFromBOM_TwoBytesPartialUtf8BOM_ReturnsEncodingWithoutPreamble() {
        // EF BB 是 UTF8 BOM 前两字节但不完整,不应识别为 UTF8 BOM
        Assert.Empty(BclFileIO.DetectFromBOM([0xEF, 0xBB]).GetPreamble());
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
        new object[] { new byte[] { 0xFF, 0xFE, 0x00, 0x00 }, "utf-32" },
        new object[] { new byte[] { 0x00, 0x00, 0xFE, 0xFF }, "utf-32BE" },
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
        var (content, encoding) = Io.DecodeBytes(WithBom(Encoding.UTF8, "中文测试"));
        Assert.Equal("中文测试", content);
        Assert.Same(Encoding.UTF8, encoding);
    }

    [Fact]
    public void DecodeBytes_Utf16LEBOMWithContent_ReturnsContentAndEncoding() {
        var (content, encoding) = Io.DecodeBytes(WithBom(Encoding.Unicode, "abc"));
        Assert.Equal("abc", content);
        Assert.Same(Encoding.Unicode, encoding);
    }

    [Fact]
    public void DecodeBytes_Utf16BEBOMWithContent_ReturnsContentAndEncoding() {
        var (content, encoding) = Io.DecodeBytes(WithBom(Encoding.BigEndianUnicode, "abc"));
        Assert.Equal("abc", content);
        Assert.Same(Encoding.BigEndianUnicode, encoding);
    }

    [Fact]
    public void DecodeBytes_NoBOMUtf8Content_ReturnsContentAndEncodingWithoutPreamble() {
        var (content, encoding) = Io.DecodeBytes(Encoding.UTF8.GetBytes("Hello World"));
        Assert.Equal("Hello World", content);
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void DecodeBytes_EmptyArray_ReturnsEmptyStringAndEncodingWithoutPreamble() {
        var (content, encoding) = Io.DecodeBytes([]);
        Assert.Equal(string.Empty, content);
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void DecodeBytes_Utf8BOMOnlyNoContent_ReturnsEmptyString() {
        // 仅 BOM 无内容,StreamReader 跳过 BOM 后读到空字符串
        var (content, encoding) = Io.DecodeBytes(Encoding.UTF8.GetPreamble());
        Assert.Equal(string.Empty, content);
        Assert.Same(Encoding.UTF8, encoding);
    }

    // === 往返不变量：解码再编码，字节完全一致（有签名保持签名，没有就不加）===

    public static IEnumerable<object[]> RoundTrip_Samples {
        get {
            yield return new object[] { Encoding.UTF8.GetBytes("no bom here"), "无 BOM 文件写入后不得新增 BOM" };
            yield return new object[] { WithBom(Encoding.UTF8, "中文 BOM"), "原 UTF-8 BOM 必须保留" };
            yield return new object[] { WithBom(Encoding.Unicode, "utf16le"), "原 UTF-16LE BOM 必须保留" };
            yield return new object[] { WithBom(Encoding.BigEndianUnicode, "utf16be"), "原 UTF-16BE BOM 必须保留" };
            yield return new object[] { WithBom(Encoding.UTF32, "utf32le"), "原 UTF-32LE BOM 必须保留" };
            yield return new object[] { WithBom(new UTF32Encoding(bigEndian: true, byteOrderMark: true), "utf32be"), "原 UTF-32BE BOM 必须保留" };
        }
    }

    [Theory]
    [MemberData(nameof(RoundTrip_Samples))]
    public void DecodeThenEncode_ReproducesOriginalBytes(byte[] original, string because) {
        var (content, encoding) = Io.DecodeBytes(original);

        var written = BclFileIO.EncodeString(content, encoding);

        Assert.True(original.SequenceEqual(written), because);
    }

    [Fact]
    public void EncodeString_NoBomEncoding_ProducesNoBom() {
        var bytes = BclFileIO.EncodeString("plain", BclFileIO.Utf8NoBom);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("plain", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void EncodeString_BomEncoding_KeepsSingleBom() {
        var bytes = BclFileIO.EncodeString("plain", Encoding.UTF8);
        Assert.Equal(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("plain")), bytes);
    }

    // === DecodeBytesWithEncoding:指定编码解码 ===

    [Fact]
    public void DecodeBytesWithEncoding_Utf8_ReturnsContent() {
        Assert.Equal("Hello", Io.DecodeBytesWithEncoding(Encoding.UTF8.GetBytes("Hello"), Encoding.UTF8));
    }

    [Fact]
    public void DecodeBytesWithEncoding_Utf16LE_ReturnsContent() {
        Assert.Equal("abc", Io.DecodeBytesWithEncoding(Encoding.Unicode.GetBytes("abc"), Encoding.Unicode));
    }

    [Fact]
    public void DecodeBytesWithEncoding_Ascii_ReturnsContent() {
        Assert.Equal("abc", Io.DecodeBytesWithEncoding(Encoding.ASCII.GetBytes("abc"), Encoding.ASCII));
    }

    [Fact]
    public void DecodeBytesWithEncoding_Latin1_ReturnsContent() {
        Assert.Equal("abc", Io.DecodeBytesWithEncoding(Encoding.Latin1.GetBytes("abc"), Encoding.Latin1));
    }

    [Fact]
    public void DecodeBytesWithEncoding_EmptyArray_ReturnsEmptyString() {
        Assert.Equal(string.Empty, Io.DecodeBytesWithEncoding([], Encoding.UTF8));
    }

    [Fact]
    public void DecodeBytesWithEncoding_Latin1HighByte_DecodesAsLatin1NotUtf8() {
        // 0xE9 在 Latin1 = é(U+00E9);在 UTF8 是非法前导字节→替换字符
        // 验证 Latin1 编码确实被使用,而非回退到 UTF8
        Assert.Equal("é", Io.DecodeBytesWithEncoding([0xE9], Encoding.Latin1));
    }
}
