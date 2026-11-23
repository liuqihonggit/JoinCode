// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Brain.Tests.Context.Compact;

/// <summary>
/// MagicDocDetector 单元测试 — 对齐 TS magicDocs.ts::detectMagicDocHeader
/// </summary>
public sealed class MagicDocDetectorTests {
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Detect_WithValidHeader_ReturnsTitle() {
        var content = "# MAGIC DOC: My Architecture Guide\nSome content here";

        var result = MagicDocDetector.Detect(content);

        result.Should().NotBeNull();
        result!.Title.Should().Be("My Architecture Guide");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Detect_WithHeaderAndItalicInstruction_ReturnsBoth() {
        var content = "# MAGIC DOC: API Reference\n_Only update API signatures, not examples_\n\n## Endpoints";

        var result = MagicDocDetector.Detect(content);

        result.Should().NotBeNull();
        result!.Title.Should().Be("API Reference");
        result.CustomInstructions.Should().Be("Only update API signatures, not examples");
    }

    [Fact]
    public void Detect_WithoutMagicDocHeader_ReturnsNull() {
        var content = "# Regular Markdown\nThis is just a normal file.";

        var result = MagicDocDetector.Detect(content);

        result.Should().BeNull();
    }

    [Fact]
    public void Detect_WithEmptyContent_ReturnsNull() {
        var result = MagicDocDetector.Detect("");

        result.Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Detect_WithNullContent_ReturnsNull() {
        var result = MagicDocDetector.Detect(null!);

        result.Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Detect_CaseInsensitive_ReturnsTitle() {
        var content = "# magic doc: Lower Case Title\nContent";

        var result = MagicDocDetector.Detect(content);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Lower Case Title");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Detect_WithExtraSpacesInHeader_ReturnsTitle() {
        var content = "#   MAGIC   DOC:   Spaced Title  \nContent";

        var result = MagicDocDetector.Detect(content);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Spaced Title");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Detect_WithNonItalicInstruction_CloseToHeader_IgnoresInstruction() {
        var content = "# MAGIC DOC: Title\nNot italic text";

        var result = MagicDocDetector.Detect(content);

        result.Should().NotBeNull();
        result!.CustomInstructions.Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Detect_WithItalicInstruction_AtBoundaryFive_IgnoresInstruction() {
        var content = "# MAGIC DOC: Title\n1234\n_Italic instruction_";

        var result = MagicDocDetector.Detect(content);

        result.Should().NotBeNull();
        result!.CustomInstructions.Should().BeNull();
    }
}