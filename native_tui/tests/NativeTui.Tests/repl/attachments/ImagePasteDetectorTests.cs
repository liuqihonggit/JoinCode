// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace NativeTui.Tests.Repl.Attachments;

/// <summary>
/// 粘贴内容图片路径识别测试 — 基准图片粘贴文本形态协议（imagePaste.ts + usePasteHandler.ts）：
/// 单行识别链（trim → 成对引号剥离 → 非 Windows 转义剥离 → 扩展名正则 imagePaste.ts:270）、
/// 多行/空格多图分割（usePasteHandler.ts:126-130）、尾部焦点残留剥离（usePasteHandler.ts:113-116）、
/// 不识别回退 null（走普通文本粘贴）。internal 平台参数形态双平台分支覆盖（运行时恒 Windows）。
/// </summary>
public sealed class ImagePasteDetectorTests
{
    // ==== 单行识别：Windows 形态 ====

    [Fact]
    public void DetectSingle_WindowsAbsolutePath_MatchesWithFilename()
    {
        var match = ImagePasteDetector.DetectSingle(@"C:\imgs\shot.png", isWindows: true);
        Assert.NotNull(match);
        Assert.Equal(@"C:\imgs\shot.png", match.Path);
        Assert.True(match.IsAbsolute);
        Assert.Equal("shot.png", match.Filename);
    }

    [Fact]
    public void DetectSingle_WindowsPath_KeepsBackslashesUnescaped()
    {
        // Windows 反斜杠是路径组成部分 — 基准 stripBackslashEscapes win32 直接返回（imagePaste.ts:297-299）
        var match = ImagePasteDetector.DetectSingle(@"C:\a\b.png", isWindows: true);
        Assert.NotNull(match);
        Assert.Equal(@"C:\a\b.png", match.Path);
    }

    [Fact]
    public void DetectSingle_UncPath_IsAbsolute()
    {
        var match = ImagePasteDetector.DetectSingle(@"\\server\share\a.png", isWindows: true);
        Assert.NotNull(match);
        Assert.True(match.IsAbsolute);
        Assert.Equal("a.png", match.Filename);
    }

    [Fact]
    public void DetectSingle_BareDriveLetter_IsNotAbsolute()
    {
        // node path.win32.isAbsolute('C:') === false — 裸盘符不算绝对路径
        var match = ImagePasteDetector.DetectSingle("C:shot.png", isWindows: true);
        Assert.NotNull(match);
        Assert.False(match.IsAbsolute);
    }

    // ==== 单行识别：Unix 形态 ====

    [Fact]
    public void DetectSingle_UnixAbsolutePath_Matches()
    {
        var match = ImagePasteDetector.DetectSingle("/home/user/shot.png", isWindows: false);
        Assert.NotNull(match);
        Assert.Equal("/home/user/shot.png", match.Path);
        Assert.True(match.IsAbsolute);
        Assert.Equal("shot.png", match.Filename);
    }

    [Fact]
    public void DetectSingle_UnixPath_StripsShellEscapes()
    {
        // 基准示例（imagePaste.ts:312）："name\ \(15\).png" -> "name (15).png"
        var match = ImagePasteDetector.DetectSingle(@"name\ \(15\).png", isWindows: false);
        Assert.NotNull(match);
        Assert.Equal("name (15).png", match.Path);
        Assert.False(match.IsAbsolute);
    }

    [Fact]
    public void DetectSingle_UnixPath_DoubleBackslashCollapses()
    {
        // 文件名含字面反斜杠：\\ → \（基准 stripBackslashEscapes 306-316）
        var match = ImagePasteDetector.DetectSingle(@"a\\b.png", isWindows: false);
        Assert.NotNull(match);
        Assert.Equal(@"a\b.png", match.Path);
    }

    [Fact]
    public void DetectSingle_UnixTrailingLoneBackslash_IsKeptAndFailsExtension()
    {
        // 孤尾反斜杠无转义对 — 基准 regex \\(.) 不匹配保留（imagePaste.ts:313），扩展名锚定 $ 不命中
        Assert.Null(ImagePasteDetector.DetectSingle("a.png\\", isWindows: false));
    }

    [Fact]
    public void DetectSingle_WindowsSemantics_DoesNotStripEscapes()
    {
        // 同一路径在 Windows 语义下原样保留（反斜杠是路径分隔符）
        var match = ImagePasteDetector.DetectSingle(@"name\ \(15\).png", isWindows: true);
        Assert.NotNull(match);
        Assert.Equal(@"name\ \(15\).png", match.Path);
    }

    // ==== 引号剥离（基准 removeOuterQuotes — imagePaste.ts:277-285）====

    [Theory]
    [InlineData("\"C:\\imgs\\shot.png\"", "C:\\imgs\\shot.png", true)]
    [InlineData("'/home/user/shot.png'", "/home/user/shot.png", false)]
    public void DetectSingle_OuterQuotes_Stripped(
        string quoted, string expected, bool isWindows)
    {
        var match = ImagePasteDetector.DetectSingle(quoted, isWindows: isWindows);
        Assert.NotNull(match);
        Assert.Equal(expected, match.Path);
    }

    [Fact]
    public void DetectSingle_MismatchedQuotes_NotStripped()
    {
        // 不对称引号原样保留（基准 startsWith && endsWith 语义 — 单双混搭不剥）：
        // 前引号无尾引号 → 保留前引号，结尾扩展名仍命中
        var match = ImagePasteDetector.DetectSingle("\"a.png", isWindows: true);
        Assert.NotNull(match);
        Assert.Equal("\"a.png", match.Path);
    }

    // ==== 扩展名家族（基准 IMAGE_EXTENSION_REGEX — imagePaste.ts:270）====

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.PNG")]
    [InlineData("a.jpg")]
    [InlineData("a.JPEG")]
    [InlineData("a.jpeg")]
    [InlineData("a.gif")]
    [InlineData("a.webp")]
    public void DetectSingle_SupportedExtensions_Match(string path)
    {
        Assert.NotNull(ImagePasteDetector.DetectSingle(path, isWindows: true));
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.pdf")]
    [InlineData("a.bmp")]
    [InlineData("a.pngx")]
    [InlineData("plain text")]
    public void DetectSingle_UnsupportedForms_ReturnNull(string line)
    {
        // bmp 不在扩展名白名单（基准注释 — MIME_BY_EXT 未列 bmp 上传无预览）
        Assert.Null(ImagePasteDetector.DetectSingle(line, isWindows: true));
    }

    [Fact]
    public void DetectSingle_TrimsSurroundingWhitespace()
    {
        var match = ImagePasteDetector.DetectSingle("  /u/a.png  ", isWindows: false);
        Assert.NotNull(match);
        Assert.Equal("/u/a.png", match.Path);
    }

    [Fact]
    public void DetectSingle_UrlEndingInPng_Matches()
    {
        // 基准 regex 只看结尾扩展名 — .png 结尾 URL 同样命中（与基准 JS regex 语义一致；
        // 误命中由宿主读文件失败自然回退普通文本粘贴）
        var match = ImagePasteDetector.DetectSingle("https://example.com/a.png", isWindows: true);
        Assert.NotNull(match);
        Assert.False(match.IsAbsolute);
    }

    [Fact]
    public void DetectSingle_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(ImagePasteDetector.DetectSingle(null, isWindows: true));
        Assert.Null(ImagePasteDetector.DetectSingle("", isWindows: true));
    }

    // ==== 整块识别：多图分割（基准 usePasteHandler.ts:126-130）====

    [Fact]
    public void Detect_NewlineSeparatedPaths_SplitsIntoImages()
    {
        var result = ImagePasteDetector.Detect("/u/a.png\n/u/b.png", isWindows: false);
        Assert.True(result.HasImage);
        Assert.Equal(2, result.ImagePaths.Count);
        Assert.Equal("/u/a.png", result.ImagePaths[0].Path);
        Assert.Equal("/u/b.png", result.ImagePaths[1].Path);
        Assert.Empty(result.NonImageLines);
    }

    [Fact]
    public void Detect_UnixSpaceSeparatedAbsolutePaths_SplitsOnSpaceBeforeSlash()
    {
        // Finder 拖拽多图空格分隔形态 — 基准分割正则 / (?=\/)/
        var result = ImagePasteDetector.Detect("/u/a.png /u/b.png", isWindows: false);
        Assert.Equal(2, result.ImagePaths.Count);
        Assert.Equal("/u/b.png", result.ImagePaths[1].Path);
    }

    [Fact]
    public void Detect_WindowsSpaceSeparatedDrivePaths_SplitsOnSpaceBeforeDrive()
    {
        // Windows 空格+盘符前瞻分割 — 基准分割正则 / (?=[A-Za-z]:\\)/
        var result = ImagePasteDetector.Detect(@"C:\a.png C:\b.png", isWindows: true);
        Assert.Equal(2, result.ImagePaths.Count);
        Assert.Equal(@"C:\b.png", result.ImagePaths[1].Path);
    }

    [Fact]
    public void Detect_EscapedSpaceWithinPath_NotSplit()
    {
        // 路径内空格被终端转义（\ 空格）— 不会被前瞻分割（基准 usePasteHandler.ts:125 注释）
        var result = ImagePasteDetector.Detect(@"name\ \(15\).png /u/b.png", isWindows: false);
        Assert.Equal(2, result.ImagePaths.Count);
        Assert.Equal("name (15).png", result.ImagePaths[0].Path);
        Assert.Equal("/u/b.png", result.ImagePaths[1].Path);
    }

    [Fact]
    public void Detect_MixedContent_PartitionsImagesAndTextLines()
    {
        // 混合粘贴：图片行 + 普通文本行两桶分离（基准 usePasteHandler.ts:130-164）
        var result = ImagePasteDetector.Detect("/u/a.png\nplain text\nnote", isWindows: false);
        Assert.Single(result.ImagePaths);
        Assert.Equal(2, result.NonImageLines.Count);
        Assert.Equal("plain text", result.NonImageLines[0]);
        Assert.Equal("note", result.NonImageLines[1]);
    }

    [Fact]
    public void Detect_EmptyLines_Filtered()
    {
        var result = ImagePasteDetector.Detect("/u/a.png\n\n  \n/u/b.png", isWindows: false);
        Assert.Equal(2, result.ImagePaths.Count);
        Assert.Empty(result.NonImageLines);
    }

    // ==== 尾部焦点残留剥离（基准 usePasteHandler.ts:113-116）====

    [Theory]
    [InlineData("/u/a.png[I", "/u/a.png")]
    [InlineData("/u/a.png[O", "/u/a.png")]
    public void Detect_TrailingFocusSequence_StrippedBeforeRecognition(string raw, string expected)
    {
        // 焦点事件拆分粘贴块时 ESC 剥离后残留 [I/[O 文本尾巴 — 基准 replace(/\[I$/,'')
        var result = ImagePasteDetector.Detect(raw, isWindows: false);
        Assert.Single(result.ImagePaths);
        Assert.Equal(expected, result.ImagePaths[0].Path);
    }

    [Fact]
    public void Detect_InteriorFocusSequence_NotStripped()
    {
        // 仅剥尾部（$ 锚定 — 基准 replace 无 g flag）；中部 [I 是内容一部分原样保留
        var result = ImagePasteDetector.Detect("/u/a[I.png", isWindows: false);
        Assert.Single(result.ImagePaths);
        Assert.Equal("/u/a[I.png", result.ImagePaths[0].Path);
    }

    // ==== 空粘贴回退 ====

    [Fact]
    public void Detect_NullOrEmpty_ReturnsEmptyResult()
    {
        // 空粘贴 = 终端发空 bracketed paste 块（剪贴板位图场景 — 位图读取属宿主域，Core 只见空文本）
        var nullResult = ImagePasteDetector.Detect(null, isWindows: true);
        Assert.False(nullResult.HasImage);
        Assert.Empty(nullResult.ImagePaths);
        Assert.Empty(nullResult.NonImageLines);
        var emptyResult = ImagePasteDetector.Detect(string.Empty, isWindows: true);
        Assert.False(emptyResult.HasImage);
    }

    [Fact]
    public void Detect_PlainTextOnly_AllLinesGoToTextBucket()
    {
        // 无图片形态 → 全部非图片行（宿主走 onPaste 原文回退 — 基准 usePasteHandler.ts:169-173）
        var result = ImagePasteDetector.Detect("line1\nline2", isWindows: false);
        Assert.False(result.HasImage);
        Assert.Equal(2, result.NonImageLines.Count);
    }

    [Fact]
    public void Detect_PublicOverload_UsesRuntimePlatform()
    {
        // public 入口平台分支与运行时一致（测试机 Windows — 反斜杠路径原样保留语义）
        var result = ImagePasteDetector.Detect(@"C:\a.png");
        Assert.True(result.HasImage);
        Assert.Equal(@"C:\a.png", result.ImagePaths[0].Path);
    }
}
