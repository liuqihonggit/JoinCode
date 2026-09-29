namespace Hands.Tests.Shell;

/// <summary>
/// ShellSedInterceptMiddleware 单元测试 — 验证 sed 拦截中间件的结构化诊断
/// </summary>
public class ShellSedInterceptMiddlewareTests {
    [Fact]
    public void BuildFileSystemUnavailableDiagnostic_ReturnsCorrectStructure() {
        var diagnostic = ShellSedInterceptMiddleware.BuildFileSystemUnavailableDiagnostic();

        diagnostic.Reason.Should().Be("服务不可用");
        diagnostic.FormattedMessage.Should().Contain("IFileSystem");
    }

    [Fact]
    public void BuildWriteFailedDiagnostic_ReturnsCorrectStructure() {
        var diagnostic = ShellSedInterceptMiddleware.BuildWriteFailedDiagnostic("/test/file.txt", "Access denied");

        diagnostic.Reason.Should().Be("写入文件失败");
        diagnostic.FormattedMessage.Should().Contain("Access denied");
        diagnostic.Details.Should().Contain(d => d.Key == "file_path" && d.Value == "/test/file.txt");
        diagnostic.Details.Should().Contain(d => d.Key == "error" && d.Value == "Access denied");
    }

    [Fact]
    public void BuildFileNotFoundDiagnostic_ReturnsCorrectStructure() {
        var diagnostic = ShellSedInterceptMiddleware.BuildFileNotFoundDiagnostic("missing.txt");

        diagnostic.Reason.Should().Be("文件未找到");
        diagnostic.FormattedMessage.Should().Contain("missing.txt");
        diagnostic.Details.Should().ContainSingle(d => d.Key == "file_path" && d.Value == "missing.txt");
        diagnostic.Suggestions.Should().HaveCount(2);
    }

    [Fact]
    public void BuildReadFailedDiagnostic_ReturnsCorrectStructure() {
        var diagnostic = ShellSedInterceptMiddleware.BuildReadFailedDiagnostic("/test/file.txt", "IO error");

        diagnostic.Reason.Should().Be("读取文件失败");
        diagnostic.FormattedMessage.Should().Contain("IO error");
        diagnostic.Details.Should().Contain(d => d.Key == "file_path" && d.Value == "/test/file.txt");
        diagnostic.Details.Should().Contain(d => d.Key == "error" && d.Value == "IO error");
    }

    // === ResolveFilePath 纯计算测试 ===

    [Fact]
    public void ResolveFilePath_AbsolutePath_ReturnsAsIs() {
        var absolute = Path.GetFullPath("/tmp/test.txt");
        ShellSedInterceptMiddleware.ResolveFilePath(absolute, "/work", "/cwd").Should().Be(absolute);
    }

    [Fact]
    public void ResolveFilePath_RelativePath_WithWorkingDirectory_Combines() {
        var result = ShellSedInterceptMiddleware.ResolveFilePath("sub/file.txt", "/work", "/cwd");
        result.Should().Be(Path.Combine("/work", "sub/file.txt"));
    }

    [Fact]
    public void ResolveFilePath_RelativePath_NullWorkingDirectory_UsesCurrentDir() {
        var result = ShellSedInterceptMiddleware.ResolveFilePath("file.txt", null, "/cwd");
        result.Should().Be(Path.Combine("/cwd", "file.txt"));
    }

    [Fact]
    public void ResolveFilePath_WorkingDirectory_TakesPrecedence_OverCurrentDir() {
        // workingDirectory 非 null 时优先于 currentDir
        var result = ShellSedInterceptMiddleware.ResolveFilePath("f.txt", "/work", "/cwd");
        result.Should().Be(Path.Combine("/work", "f.txt"));
        result.Should().NotContain("/cwd");
    }

    // === DetectLineEnding 纯计算测试 ===

    [Fact]
    public void DetectLineEnding_Crlf_ReturnsCrlf() {
        ShellSedInterceptMiddleware.DetectLineEnding("line1\r\nline2\r\n").Should().Be("\r\n");
    }

    [Fact]
    public void DetectLineEnding_LfOnly_ReturnsLf() {
        ShellSedInterceptMiddleware.DetectLineEnding("line1\nline2\n").Should().Be("\n");
    }

    [Fact]
    public void DetectLineEnding_EmptyString_ReturnsLf() {
        ShellSedInterceptMiddleware.DetectLineEnding("").Should().Be("\n");
    }

    [Fact]
    public void DetectLineEnding_CrOnly_ReturnsLf() {
        // 仅 CR（无 LF）不含 \r\n,应返回 LF
        ShellSedInterceptMiddleware.DetectLineEnding("line1\rline2").Should().Be("\n");
    }

    [Fact]
    public void DetectLineEnding_NoLineEnding_ReturnsLf() {
        ShellSedInterceptMiddleware.DetectLineEnding("single line").Should().Be("\n");
    }

    // === NormalizeLineEndings 纯计算测试 ===

    [Fact]
    public void NormalizeLineEndings_Crlf_ReplacedWithLf() {
        ShellSedInterceptMiddleware.NormalizeLineEndings("a\r\nb\r\n").Should().Be("a\nb\n");
    }

    [Fact]
    public void NormalizeLineEndings_LfOnly_Unchanged() {
        ShellSedInterceptMiddleware.NormalizeLineEndings("a\nb\n").Should().Be("a\nb\n");
    }

    [Fact]
    public void NormalizeLineEndings_EmptyString_ReturnsEmpty() {
        ShellSedInterceptMiddleware.NormalizeLineEndings("").Should().Be("");
    }

    [Fact]
    public void NormalizeLineEndings_MultipleCrlf_AllReplaced() {
        ShellSedInterceptMiddleware.NormalizeLineEndings("a\r\nb\r\nc\r\n").Should().Be("a\nb\nc\n");
    }

    [Fact]
    public void NormalizeLineEndings_NoCrlf_Unchanged() {
        var content = "no line endings at all";
        ShellSedInterceptMiddleware.NormalizeLineEndings(content).Should().Be(content);
    }

    // === BuildNoChangeMessage 纯计算测试 ===

    [Fact]
    public void BuildNoChangeMessage_EmptyContent_ReturnsEmptyFileMessage() {
        ShellSedInterceptMiddleware.BuildNoChangeMessage("").Should().Be("File is empty, pattern did not match");
    }

    [Fact]
    public void BuildNoChangeMessage_NonEmptyContent_ReturnsNoMatchMessage() {
        ShellSedInterceptMiddleware.BuildNoChangeMessage("some content").Should().Be("Pattern did not match any content");
    }

    // === BuildSedPreview 纯计算测试 ===

    private static SedEditInfo CreateSedInfo(string filePath = "test.txt", string pattern = "old", string replacement = "new", string flags = "g") =>
        new() { FilePath = filePath, Pattern = pattern, Replacement = replacement, Flags = flags, ExtendedRegex = false };

    [Fact]
    public void BuildSedPreview_ContainsHeaderInfo() {
        var sedInfo = CreateSedInfo("file.cs", "foo", "bar", "g");
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, "foo", "bar");

        preview.Should().Contain("Sed edit preview for file.cs:");
        preview.Should().Contain("Pattern: foo");
        preview.Should().Contain("Replacement: bar");
        preview.Should().Contain("Flags: g");
    }

    [Fact]
    public void BuildSedPreview_WithDiff_ShowsChangedLines() {
        var sedInfo = CreateSedInfo();
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, "old line\n", "new line\n");

        preview.Should().Contain("- old line");
        preview.Should().Contain("+ new line");
        preview.Should().Contain("1 line(s) changed");
    }

    [Fact]
    public void BuildSedPreview_NoChange_ShowsZeroChanges() {
        var sedInfo = CreateSedInfo();
        var content = "same content\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, content, content);

        // 内容相同,changeCount 保持 0（不进入 diff 循环）
        preview.Should().Contain("0 line(s) changed");
    }

    [Fact]
    public void BuildSedPreview_MultipleChanges_CountsAll() {
        var sedInfo = CreateSedInfo();
        var oldContent = "line1\nline2\nline3\n";
        var newContent = "changed1\nchanged2\nchanged3\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, oldContent, newContent);

        preview.Should().Contain("- line1");
        preview.Should().Contain("+ changed1");
        preview.Should().Contain("- line2");
        preview.Should().Contain("+ changed2");
        preview.Should().Contain("- line3");
        preview.Should().Contain("+ changed3");
        preview.Should().Contain("3 line(s) changed");
    }

    [Fact]
    public void BuildSedPreview_CappedAt20Changes() {
        // 超过 20 行变更时,只显示前 20 行 diff,changeCount 也被截断为 20（循环退出条件）
        var sedInfo = CreateSedInfo();
        var oldLines = Enumerable.Range(0, 30).Select(i => $"old{i}").ToArray();
        var newLines = Enumerable.Range(0, 30).Select(i => $"new{i}").ToArray();
        var oldContent = string.Join("\n", oldLines) + "\n";
        var newContent = string.Join("\n", newLines) + "\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, oldContent, newContent);

        // 应包含前 20 行的 diff
        preview.Should().Contain("- old0");
        preview.Should().Contain("+ new0");
        preview.Should().Contain("- old19");
        preview.Should().Contain("+ new19");
        // 不应包含第 21 行（changeCount 已达 20,循环退出）
        preview.Should().NotContain("- old20");
        preview.Should().NotContain("+ new20");
        // changeCount 在循环中被截断为 20（原代码行为:changeCount < 20 是循环条件）
        preview.Should().Contain("20 line(s) changed");
    }

    [Fact]
    public void BuildSedPreview_DifferentLineCount_OnlyAdditions() {
        // 新增行:oldContent 行数少于 newContent
        var sedInfo = CreateSedInfo();
        var oldContent = "line1\n";
        var newContent = "line1\nline2\nline3\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, oldContent, newContent);

        // line1 相同,新增 line2/line3
        preview.Should().Contain("+ line2");
        preview.Should().Contain("+ line3");
        preview.Should().NotContain("- line1");
    }

    [Fact]
    public void BuildSedPreview_DifferentLineCount_OnlyDeletions() {
        // 删除行:oldContent 行数多于 newContent
        var sedInfo = CreateSedInfo();
        var oldContent = "line1\nline2\nline3\n";
        var newContent = "line1\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, oldContent, newContent);

        preview.Should().Contain("- line2");
        preview.Should().Contain("- line3");
        preview.Should().NotContain("+ line2");
    }

    [Fact]
    public void BuildSedPreview_EndsWithConfirmInstruction() {
        var sedInfo = CreateSedInfo();
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, "a\n", "b\n");

        // AppendLine 使用 Environment.NewLine,用 Contain 验证关键文本（跨平台兼容）
        preview.Should().Contain("Re-run the same sed command to confirm and apply this edit.");
    }
}