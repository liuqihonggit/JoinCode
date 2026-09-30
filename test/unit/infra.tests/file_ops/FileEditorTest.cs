namespace Infra.Tests.FileOps;

/// <summary>
/// FileEditor 纯计算方法单元测试(阶段2.9 拆分)。
/// <para>测试 internal static 方法:NormalizeForMatch / IsMarkdownFile / CheckUniqueOccurrence /
/// ApplyReplacement / RestoreLineEndings / CountOccurrences / ValidateLineRange /
/// ComputeActualEndLine / ExtractOriginalContent / BuildUpdatedFileContent。</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class FileEditorTest {
    // ===== NormalizeForMatch =====

    [Fact]
    public void NormalizeForMatch_Crlf_ConvertedToLf() {
        var (normalizedOld, normalizedNew, normalizedContent) =
            FileEditor.NormalizeForMatch("a\r\nb", "c\r\nd", "x\r\ny\r\nz");

        normalizedOld.Should().Be("a\nb");
        normalizedNew.Should().Be("c\nd");
        normalizedContent.Should().Be("x\ny\nz");
    }

    [Fact]
    public void NormalizeForMatch_LineNumberPrefix_StrippedFromOldAndNew() {
        // LLM 复制 old_string 时可能带入行号前缀(行号+\t)
        var (normalizedOld, normalizedNew, _) =
            FileEditor.NormalizeForMatch("1\tfoo\n2\tbar", "10\tbaz", "content");

        normalizedOld.Should().Be("foo\nbar");
        normalizedNew.Should().Be("baz");
    }

    [Fact]
    public void NormalizeForMatch_ContentNotStripped_OnlyCrlfNormalized() {
        // content 仅做 CRLF→LF,不剥离行号前缀(文件内容里的行号是真实内容)
        var (_, _, normalizedContent) =
            FileEditor.NormalizeForMatch("old", "new", "1\treal\n2\tdata");

        normalizedContent.Should().Be("1\treal\n2\tdata");
    }

    [Fact]
    public void NormalizeForMatch_NoCrlf_NoPrefix_Idempotent() {
        var (normalizedOld, normalizedNew, normalizedContent) =
            FileEditor.NormalizeForMatch("abc", "xyz", "abcdef");

        normalizedOld.Should().Be("abc");
        normalizedNew.Should().Be("xyz");
        normalizedContent.Should().Be("abcdef");
    }

    // ===== IsMarkdownFile =====

    [Theory]
    [InlineData("file.md")]
    [InlineData("file.MD")]
    [InlineData("file.mdx")]
    [InlineData("FILE.MDX")]
    [InlineData("/path/to/doc.md")]
    public void IsMarkdownFile_MarkdownExtensions_ReturnsTrue(string path) {
        FileEditor.IsMarkdownFile(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("file.txt")]
    [InlineData("file.cs")]
    [InlineData("file.json")]
    [InlineData("file.")]
    [InlineData("file")]
    [InlineData("")]
    public void IsMarkdownFile_NonMarkdown_ReturnsFalse(string path) {
        FileEditor.IsMarkdownFile(path).Should().BeFalse();
    }

    // ===== CheckUniqueOccurrence =====

    [Fact]
    public void CheckUniqueOccurrence_ReplaceAll_True_ReturnsNull() {
        // replaceAll=true 跳过唯一性检查
        FileEditor.CheckUniqueOccurrence("a a a", "a", replaceAll: true).Should().BeNull();
    }

    [Fact]
    public void CheckUniqueOccurrence_SingleMatch_ReturnsNull() {
        FileEditor.CheckUniqueOccurrence("hello world", "world", replaceAll: false).Should().BeNull();
    }

    [Fact]
    public void CheckUniqueOccurrence_NoMatch_ReturnsNull() {
        // 无匹配不触发唯一性错误(后续 ApplyReplacement 返回 0 次)
        FileEditor.CheckUniqueOccurrence("hello", "xyz", replaceAll: false).Should().BeNull();
    }

    [Fact]
    public void CheckUniqueOccurrence_MultipleMatches_ReturnsErrorMessage() {
        var error = FileEditor.CheckUniqueOccurrence("a a a", "a", replaceAll: false);
        error.Should().NotBeNull();
        error.Should().Contain("matched 3 times");
        error.Should().Contain("replace_all is false");
    }

    // ===== ApplyReplacement =====

    [Fact]
    public void ApplyReplacement_ReplaceAll_ReplacesAllOccurrences() {
        var (updated, count) = FileEditor.ApplyReplacement("a a a", "a", "X", replaceAll: true);

        updated.Should().Be("X X X");
        count.Should().Be(3);
    }

    [Fact]
    public void ApplyReplacement_SingleReplace_ReplacesFirstOccurrence() {
        var (updated, count) = FileEditor.ApplyReplacement("a a a", "a", "X", replaceAll: false);

        updated.Should().Be("X a a");
        count.Should().Be(1);
    }

    [Fact]
    public void ApplyReplacement_NotFound_ReturnsOriginalAndZero() {
        var (updated, count) = FileEditor.ApplyReplacement("hello", "xyz", "X", replaceAll: false);

        updated.Should().Be("hello");
        count.Should().Be(0);
    }

    [Fact]
    public void ApplyReplacement_ReplaceAll_NotFound_ReturnsOriginalAndZero() {
        var (updated, count) = FileEditor.ApplyReplacement("hello", "xyz", "X", replaceAll: true);

        updated.Should().Be("hello");
        count.Should().Be(0);
    }

    [Fact]
    public void ApplyReplacement_EmptyNewString_DeletesOccurrence() {
        var (updated, count) = FileEditor.ApplyReplacement("foo bar baz", "bar ", "", replaceAll: false);

        updated.Should().Be("foo baz");
        count.Should().Be(1);
    }

    [Fact]
    public void ApplyReplacement_LongerNewString_ExpandsContent() {
        var (updated, count) = FileEditor.ApplyReplacement("ab", "b", "XYZ", replaceAll: false);

        updated.Should().Be("aXYZ");
        count.Should().Be(1);
    }

    // ===== RestoreLineEndings =====

    [Fact]
    public void RestoreLineEndings_HasCrlf_ConvertsLfToCrlf() {
        FileEditor.RestoreLineEndings("a\nb\nc", hasCrlf: true).Should().Be("a\r\nb\r\nc");
    }

    [Fact]
    public void RestoreLineEndings_NoCrlf_ReturnsAsIs() {
        FileEditor.RestoreLineEndings("a\nb\nc", hasCrlf: false).Should().Be("a\nb\nc");
    }

    [Fact]
    public void RestoreLineEndings_RoundTripWithNormalizeForMatch() {
        // NormalizeForMatch (CRLF→LF) → RestoreLineEndings (LF→CRLF) 应恢复原样
        const string original = "a\r\nb\r\nc";
        var (_, _, normalized) = FileEditor.NormalizeForMatch("x", "y", original);
        FileEditor.RestoreLineEndings(normalized, hasCrlf: true).Should().Be(original);
    }

    // ===== CountOccurrences =====

    [Fact]
    public void CountOccurrences_NoMatch_ReturnsZero() {
        FileEditor.CountOccurrences("hello", "xyz").Should().Be(0);
    }

    [Fact]
    public void CountOccurrences_MultipleMatches_ReturnsCount() {
        FileEditor.CountOccurrences("banana", "a").Should().Be(3);
    }

    [Fact]
    public void CountOccurrences_EmptySubstring_ReturnsZero() {
        FileEditor.CountOccurrences("hello", "").Should().Be(0);
    }

    [Fact]
    public void CountOccurrences_EmptyText_ReturnsZero() {
        FileEditor.CountOccurrences("", "a").Should().Be(0);
    }

    [Fact]
    public void CountOccurrences_NonOverlapping_DoesNotCountOverlaps() {
        // "aaa" 中 "aa" 非重叠计数为 1(IndexOf 跳过 substring.Length)
        FileEditor.CountOccurrences("aaa", "aa").Should().Be(1);
    }

    // ===== ValidateLineRange =====

    [Fact]
    public void ValidateLineRange_StartLineLessThanOne_ReturnsError() {
        var error = FileEditor.ValidateLineRange(0, 5);
        error.Should().Be("Start line must be at least 1");
    }

    [Fact]
    public void ValidateLineRange_EndLineLessThanStartLine_ReturnsError() {
        var error = FileEditor.ValidateLineRange(5, 3);
        error.Should().Be("End line must not be less than start line");
    }

    [Fact]
    public void ValidateLineRange_ValidRange_ReturnsNull() {
        FileEditor.ValidateLineRange(1, 1).Should().BeNull();
        FileEditor.ValidateLineRange(3, 10).Should().BeNull();
    }

    // ===== ComputeActualEndLine =====

    [Fact]
    public void ComputeActualEndLine_EndLineWithinTotal_ReturnsAsIs() {
        var (actualEnd, replacedCount) = FileEditor.ComputeActualEndLine(2, 5, totalLines: 10);

        actualEnd.Should().Be(5);
        replacedCount.Should().Be(4); // 5 - 2 + 1
    }

    [Fact]
    public void ComputeActualEndLine_EndLineExceedsTotal_TruncatesToTotal() {
        var (actualEnd, replacedCount) = FileEditor.ComputeActualEndLine(8, 20, totalLines: 10);

        actualEnd.Should().Be(10);
        replacedCount.Should().Be(3); // 10 - 8 + 1
    }

    [Fact]
    public void ComputeActualEndLine_SingleLine_ReturnsOneReplaced() {
        var (actualEnd, replacedCount) = FileEditor.ComputeActualEndLine(3, 3, totalLines: 5);

        actualEnd.Should().Be(3);
        replacedCount.Should().Be(1);
    }

    // ===== ExtractOriginalContent =====

    [Fact]
    public void ExtractOriginalContent_ExtractsSpecifiedLines() {
        var allLines = new List<string> { "a", "b", "c", "d", "e" };

        var original = FileEditor.ExtractOriginalContent(allLines, startLine: 2, replacedLinesCount: 3);

        original.Should().Be("b\nc\nd");
    }

    [Fact]
    public void ExtractOriginalContent_SingleLine_ReturnsOneLine() {
        var allLines = new List<string> { "a", "b", "c" };

        var original = FileEditor.ExtractOriginalContent(allLines, startLine: 2, replacedLinesCount: 1);

        original.Should().Be("b");
    }

    [Fact]
    public void ExtractOriginalContent_FirstLine() {
        var allLines = new List<string> { "a", "b", "c" };

        var original = FileEditor.ExtractOriginalContent(allLines, startLine: 1, replacedLinesCount: 2);

        original.Should().Be("a\nb");
    }

    // ===== BuildUpdatedFileContent =====

    [Fact]
    public void BuildUpdatedFileContent_ReplaceMiddleLines() {
        var allLines = new List<string> { "a", "b", "c", "d", "e" };

        var updated = FileEditor.BuildUpdatedFileContent(allLines, startLine: 2, actualEndLine: 3, totalLines: 5, newContent: "X\nY");

        updated.Should().Be("a\nX\nY\nd\ne");
    }

    [Fact]
    public void BuildUpdatedFileContent_ReplaceFirstLine() {
        var allLines = new List<string> { "a", "b", "c" };

        var updated = FileEditor.BuildUpdatedFileContent(allLines, startLine: 1, actualEndLine: 1, totalLines: 3, newContent: "X");

        updated.Should().Be("X\nb\nc");
    }

    [Fact]
    public void BuildUpdatedFileContent_ReplaceLastLine() {
        var allLines = new List<string> { "a", "b", "c" };

        var updated = FileEditor.BuildUpdatedFileContent(allLines, startLine: 3, actualEndLine: 3, totalLines: 3, newContent: "X");

        updated.Should().Be("a\nb\nX");
    }

    [Fact]
    public void BuildUpdatedFileContent_ReplaceAllLines() {
        var allLines = new List<string> { "a", "b", "c" };

        var updated = FileEditor.BuildUpdatedFileContent(allLines, startLine: 1, actualEndLine: 3, totalLines: 3, newContent: "X\nY");

        updated.Should().Be("X\nY");
    }

    [Fact]
    public void BuildUpdatedFileContent_NewContentMultiLine_ExpandsLineCount() {
        var allLines = new List<string> { "a", "b", "c" };

        var updated = FileEditor.BuildUpdatedFileContent(allLines, startLine: 2, actualEndLine: 2, totalLines: 3, newContent: "X\nY\nZ");

        updated.Should().Be("a\nX\nY\nZ\nc");
    }

    // ===== ExtractOriginalContent 守卫(确定性) =====

    [Trait("Category", "Deterministic")]
    [Fact]
    public void ExtractOriginalContent_NullAllLines_ThrowsArgumentNullException() {
        Action act = () => FileEditor.ExtractOriginalContent(null!, 1, 1);
        act.Should().Throw<ArgumentNullException>().WithParameterName("allLines");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void ExtractOriginalContent_StartLineLessThanOne_ThrowsArgumentOutOfRangeException() {
        var lines = new List<string> { "a" };
        Action act = () => FileEditor.ExtractOriginalContent(lines, 0, 1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("startLine");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void ExtractOriginalContent_NegativeReplacedLinesCount_ThrowsArgumentOutOfRangeException() {
        var lines = new List<string> { "a" };
        Action act = () => FileEditor.ExtractOriginalContent(lines, 1, -1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("replacedLinesCount");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void ExtractOriginalContent_RangeExceedsAllLines_ThrowsArgumentOutOfRangeException() {
        var lines = new List<string> { "a", "b" };
        // startLine=1, replacedLinesCount=3 → 1-1+3=3 > allLines.Count=2
        Action act = () => FileEditor.ExtractOriginalContent(lines, 1, 3);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("replacedLinesCount");
    }

    // ===== BuildUpdatedFileContent 守卫(确定性) =====

    [Trait("Category", "Deterministic")]
    [Fact]
    public void BuildUpdatedFileContent_NullAllLines_ThrowsArgumentNullException() {
        Action act = () => FileEditor.BuildUpdatedFileContent(null!, 1, 1, 1, "x");
        act.Should().Throw<ArgumentNullException>().WithParameterName("allLines");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void BuildUpdatedFileContent_NullNewContent_ThrowsArgumentNullException() {
        var lines = new List<string> { "a" };
        Action act = () => FileEditor.BuildUpdatedFileContent(lines, 1, 1, 1, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("newContent");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void BuildUpdatedFileContent_StartLineLessThanOne_ThrowsArgumentOutOfRangeException() {
        var lines = new List<string> { "a" };
        Action act = () => FileEditor.BuildUpdatedFileContent(lines, 0, 1, 1, "x");
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("startLine");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void BuildUpdatedFileContent_NegativeTotalLines_ThrowsArgumentOutOfRangeException() {
        var lines = new List<string> { "a" };
        Action act = () => FileEditor.BuildUpdatedFileContent(lines, 1, 1, -1, "x");
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("totalLines");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void BuildUpdatedFileContent_TotalLinesMismatch_ThrowsArgumentException() {
        var lines = new List<string> { "a" };
        // allLines.Count=1 但 totalLines=2 → 不一致
        Action act = () => FileEditor.BuildUpdatedFileContent(lines, 1, 1, 2, "x");
        act.Should().Throw<ArgumentException>().WithParameterName("totalLines");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void BuildUpdatedFileContent_ActualEndLineLessThanStartLine_ThrowsArgumentOutOfRangeException() {
        var lines = new List<string> { "a", "b" };
        Action act = () => FileEditor.BuildUpdatedFileContent(lines, 2, 1, 2, "x");
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("actualEndLine");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void BuildUpdatedFileContent_ActualEndLineExceedsTotalLines_ThrowsArgumentOutOfRangeException() {
        var lines = new List<string> { "a", "b" };
        Action act = () => FileEditor.BuildUpdatedFileContent(lines, 1, 3, 2, "x");
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("actualEndLine");
    }
}
