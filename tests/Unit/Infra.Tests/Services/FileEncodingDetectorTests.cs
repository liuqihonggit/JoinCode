using System.Text;
using Infrastructure.IO.Services.FileOps;
using IO.FileSystem;

/// <summary>
/// FileEncodingDetector 单元测试
/// 对齐 TS: fileRead.ts detectFileEncoding + FileEditTool.ts L207-213
/// TS 逻辑：检查 BOM（0xFF 0xFE → UTF-16LE），否则默认 UTF-8
/// </summary>
public class FileEncodingDetectorTests
{
    private static readonly IFileSystem Fs = TestFileSystem.Current;

    #region DetectFromBOM — 从字节数组检测编码

    [Fact]
    public void DetectFromBOM_Utf16LE_BOM_ReturnsUtf16LE()
    {
        // TS: buffer[0] === 0xff && buffer[1] === 0xfe → 'utf16le'
        var bytes = new byte[] { 0xFF, 0xFE, 0x41, 0x00 }; // UTF-16LE BOM + "A"
        var encoding = FileEncodingDetector.DetectFromBOM(bytes);
        encoding.Should().BeSameAs(Encoding.Unicode); // Unicode = UTF-16LE in .NET
    }

    [Fact]
    public void DetectFromBOM_Utf8_BOM_ReturnsUtf8()
    {
        // TS: buffer[0] === 0xef && buffer[1] === 0xbb && buffer[2] === 0xbf → 'utf8'
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }; // UTF-8 BOM + "A"
        var encoding = FileEncodingDetector.DetectFromBOM(bytes);
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    [Fact]
    public void DetectFromBOM_NoBOM_ReturnsUtf8()
    {
        // TS: 默认 utf8
        var bytes = new byte[] { 0x41, 0x42, 0x43 }; // "ABC" 无 BOM
        var encoding = FileEncodingDetector.DetectFromBOM(bytes);
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    [Fact]
    public void DetectFromBOM_EmptyBuffer_ReturnsUtf8()
    {
        // TS: bytesRead === 0 → 'utf8'
        var bytes = Array.Empty<byte>();
        var encoding = FileEncodingDetector.DetectFromBOM(bytes);
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    [Fact]
    public void DetectFromBOM_SingleByte_ReturnsUtf8()
    {
        // TS: bytesRead >= 2 才检查 UTF-16LE，1 字节默认 utf8
        var bytes = new byte[] { 0xFF };
        var encoding = FileEncodingDetector.DetectFromBOM(bytes);
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    [Fact]
    public void DetectFromBOM_Utf16BE_BOM_ReturnsUtf16BE()
    {
        // C# 增强：检测 UTF-16BE（0xFE 0xFF），TS 原版不检测
        var bytes = new byte[] { 0xFE, 0xFF, 0x00, 0x41 }; // UTF-16BE BOM
        var encoding = FileEncodingDetector.DetectFromBOM(bytes);
        encoding.Should().BeSameAs(Encoding.BigEndianUnicode); // UTF-16BE
    }

    [Fact]
    public void DetectFromBOM_Utf32LE_BOM_ReturnsUtf32LE()
    {
        // UTF-32LE BOM: FF FE 00 00
        var bytes = new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00 };
        var encoding = FileEncodingDetector.DetectFromBOM(bytes);
        encoding.Should().BeSameAs(Encoding.UTF32); // UTF-32LE in .NET
    }

    [Fact]
    public void DetectFromBOM_Utf32BE_BOM_ReturnsUtf32BE()
    {
        // UTF-32BE BOM: 00 00 FE FF
        var bytes = new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x41 };
        var encoding = FileEncodingDetector.DetectFromBOM(bytes);
        encoding.Should().BeOfType<UTF32Encoding>();
        encoding.Should().NotBeSameAs(Encoding.UTF32); // UTF32 = LE, this should be BE
    }

    [Fact]
    public void DetectFromBOM_Utf16LE_BOM_NotConfusedWith_Utf32LE()
    {
        // UTF-32LE BOM 前2字节与 UTF-16LE 相同（FF FE），但第3-4字节为 00 00
        // 必须优先检测 UTF-32LE（4字节BOM）再检测 UTF-16LE（2字节BOM）
        var utf32le = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
        FileEncodingDetector.DetectFromBOM(utf32le).Should().BeSameAs(Encoding.UTF32);

        var utf16le = new byte[] { 0xFF, 0xFE, 0x41, 0x00 };
        FileEncodingDetector.DetectFromBOM(utf16le).Should().BeSameAs(Encoding.Unicode);
    }

    #endregion

    #region DetectFromFile — 从文件路径检测编码

    [Fact]
    public async Task DetectFromFile_Utf16LEFile_ReturnsUtf16LE()
    {
        var filePath = "/test/test-utf16le.txt";

        // 写入 UTF-16LE BOM + 内容
        await Fs.WriteAllBytesAsync(filePath, [0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00]).ConfigureAwait(true);

        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, Fs).ConfigureAwait(true);
        encoding.Should().BeSameAs(Encoding.Unicode);
    }

    [Fact]
    public async Task DetectFromFile_Utf8NoBOM_ReturnsUtf8()
    {
        var filePath = "/test/test-utf8.txt";

        await Fs.WriteAllTextAsync(filePath, "Hello World", new UTF8Encoding(false)).ConfigureAwait(true);

        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, Fs).ConfigureAwait(true);
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    [Fact]
    public async Task DetectFromFile_Utf8WithBOM_ReturnsUtf8()
    {
        var filePath = "/test/test-utf8bom.txt";

        await Fs.WriteAllTextAsync(filePath, "Hello World", new UTF8Encoding(true)).ConfigureAwait(true);

        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, Fs).ConfigureAwait(true);
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    [Fact]
    public async Task DetectFromFile_NonExistentFile_ReturnsUtf8()
    {
        var encoding = await FileEncodingDetector.DetectFromFileAsync("/nonexistent/file.txt", Fs).ConfigureAwait(true);
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    [Fact]
    public async Task DetectFromFile_EmptyFile_ReturnsUtf8()
    {
        // TS: bytesRead === 0 → 'utf8'（空文件默认 UTF-8，不是 ASCII）
        var filePath = "/test/empty.txt";
        await Fs.WriteAllBytesAsync(filePath, []).ConfigureAwait(true);

        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, Fs).ConfigureAwait(true);
        encoding.Should().BeSameAs(Encoding.UTF8);
    }

    #endregion

    #region 集成测试：UTF-16LE 文件读写保持编码

    [Fact]
    public async Task ReadUtf16LEFile_ThenWriteBack_PreservesEncoding()
    {
        var filePath = "/test/roundtrip.txt";

        // 写入 UTF-16LE 文件
        var originalContent = "Hello 你好";
        var utf16LE = Encoding.Unicode;
        await Fs.WriteAllTextAsync(filePath, originalContent, utf16LE).ConfigureAwait(true);

        // 检测编码并读取
        var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(filePath, Fs).ConfigureAwait(true);
        var readContent = await Fs.ReadAllTextAsync(filePath, detectedEncoding).ConfigureAwait(true);

        readContent.Should().Be(originalContent);

        // 用检测到的编码写回
        await Fs.WriteAllTextAsync(filePath, readContent + " World", detectedEncoding).ConfigureAwait(true);

        // 验证仍然是 UTF-16LE
        var bytes = await Fs.ReadAllBytesAsync(filePath).ConfigureAwait(true);
        bytes[0].Should().Be(0xFF); // UTF-16LE BOM byte 1
        bytes[1].Should().Be(0xFE); // UTF-16LE BOM byte 2
    }

    #endregion

    /// <summary>
    /// 临时目录辅助类
    /// </summary>
    private sealed class TempDirectory
    {
        public string Path { get; } = "/test/" + Guid.NewGuid().ToString("N");

        public TempDirectory()
        {
            Fs.CreateDirectory(Path);
        }
    }
}
