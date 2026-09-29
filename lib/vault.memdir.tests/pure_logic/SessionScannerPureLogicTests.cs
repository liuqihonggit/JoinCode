
namespace Core.Tests.Memdir;

/// <summary>
/// SessionScanner 纯逻辑确定性测试
/// CategorizeToolError: 6 类错误字符串匹配 + Other
/// ExtractLanguageAndFileStats: 语言/Git 统计
/// </summary>
public sealed class SessionScannerPureLogicTests {

    // === CategorizeToolError: 6 类错误 ===

    [Fact]
    public void CategorizeToolError_ExitCode_ReturnsCommandFailed() {
        SessionScanner.CategorizeToolError("Command failed with exit code 1").Should().Be("Command Failed");
    }

    [Fact]
    public void CategorizeToolError_ExitCode_CaseInsensitive() {
        SessionScanner.CategorizeToolError("EXIT CODE 127").Should().Be("Command Failed");
    }

    [Fact]
    public void CategorizeToolError_Rejected_ReturnsUserRejected() {
        SessionScanner.CategorizeToolError("Permission rejected by user").Should().Be("User Rejected");
    }

    [Fact]
    public void CategorizeToolError_DoesntWant_ReturnsUserRejected() {
        SessionScanner.CategorizeToolError("User doesn't want to proceed").Should().Be("User Rejected");
    }

    [Fact]
    public void CategorizeToolError_StringToReplaceNotFound_ReturnsEditFailed() {
        SessionScanner.CategorizeToolError("string to replace not found in file").Should().Be("Edit Failed");
    }

    [Fact]
    public void CategorizeToolError_NoChanges_ReturnsEditFailed() {
        SessionScanner.CategorizeToolError("no changes made").Should().Be("Edit Failed");
    }

    [Fact]
    public void CategorizeToolError_ModifiedSinceRead_ReturnsFileChanged() {
        SessionScanner.CategorizeToolError("file was modified since read").Should().Be("File Changed");
    }

    [Fact]
    public void CategorizeToolError_ExceedsMaximum_ReturnsFileTooLarge() {
        SessionScanner.CategorizeToolError("file size exceeds maximum limit").Should().Be("File Too Large");
    }

    [Fact]
    public void CategorizeToolError_TooLarge_ReturnsFileTooLarge() {
        SessionScanner.CategorizeToolError("response too large").Should().Be("File Too Large");
    }

    [Fact]
    public void CategorizeToolError_FileNotFound_ReturnsFileNotFound() {
        SessionScanner.CategorizeToolError("file not found at path").Should().Be("File Not Found");
    }

    [Fact]
    public void CategorizeToolError_DoesNotExist_ReturnsFileNotFound() {
        SessionScanner.CategorizeToolError("path does not exist").Should().Be("File Not Found");
    }

    [Fact]
    public void CategorizeToolError_UnknownError_ReturnsOther() {
        SessionScanner.CategorizeToolError("some random error message").Should().Be("Other");
    }

    [Fact]
    public void CategorizeToolError_EmptyContent_ReturnsOther() {
        SessionScanner.CategorizeToolError("").Should().Be("Other");
    }

    // === 优先级:exit code 先于其他 ===

    [Fact]
    public void CategorizeToolError_ExitCodeTakesPrecedenceOverFileNotFound() {
        // "exit code" 在前面检查,即使内容也含 "file not found"
        SessionScanner.CategorizeToolError("exit code 1: file not found").Should().Be("Command Failed");
    }

    // === 确定性 ===

    [Fact]
    public void CategorizeToolError_Deterministic_SameInputSameOutput() {
        var content = "exit code 42";
        var r1 = SessionScanner.CategorizeToolError(content);
        var r2 = SessionScanner.CategorizeToolError(content);
        r1.Should().Be(r2);
    }

    // === ExtractLanguageAndFileStats: 语言识别 ===

    [Fact]
    public void ExtractLanguageAndFileStats_CSharpExtension_RecordsCSharpLanguage() {
        var entry = new TranscriptEntry { Content = "modified src/Program.cs" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        languages.Should().ContainKey("C#");
        languages["C#"].Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void ExtractLanguageAndFileStats_TypeScriptExtension_RecordsTypeScriptLanguage() {
        var entry = new TranscriptEntry { Content = "edited app/component.tsx" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        languages.Should().ContainKey("TypeScript");
    }

    [Fact]
    public void ExtractLanguageAndFileStats_PythonExtension_RecordsPythonLanguage() {
        var entry = new TranscriptEntry { Content = "ran script.py" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        languages.Should().ContainKey("Python");
    }

    [Fact]
    public void ExtractLanguageAndFileStats_MultipleExtensions_RecordsAllLanguages() {
        var entry = new TranscriptEntry { Content = "touched a.cs b.py c.rs" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        languages.Should().ContainKeys("C#", "Python", "Rust");
    }

    [Fact]
    public void ExtractLanguageAndFileStats_NoExtension_NoLanguageRecorded() {
        var entry = new TranscriptEntry { Content = "plain text without file extensions" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        languages.Should().BeEmpty();
    }

    // === Git 操作统计 ===

    [Fact]
    public void ExtractLanguageAndFileStats_GitCommit_IncrementsCommitCount() {
        var entry = new TranscriptEntry { Content = "git commit -m feat" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        commits.Should().Be(1);
        pushes.Should().Be(0);
    }

    [Fact]
    public void ExtractLanguageAndFileStats_GitPush_IncrementsPushCount() {
        var entry = new TranscriptEntry { Content = "git push origin main" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        pushes.Should().Be(1);
        commits.Should().Be(0);
    }

    [Fact]
    public void ExtractLanguageAndFileStats_GitCommitAndPush_IncrementsBoth() {
        var entry = new TranscriptEntry { Content = "git commit && git push" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        commits.Should().Be(1);
        pushes.Should().Be(1);
    }

    [Fact]
    public void ExtractLanguageAndFileStats_GitCaseInsensitive_MatchesUpperCase() {
        var entry = new TranscriptEntry { Content = "GIT COMMIT" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        commits.Should().Be(1);
    }

    // === 空内容 ===

    [Fact]
    public void ExtractLanguageAndFileStats_EmptyContent_NoChanges() {
        var entry = new TranscriptEntry { Content = "" };
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, languages, files, ref commits, ref pushes, ref added, ref removed);

        languages.Should().BeEmpty();
        commits.Should().Be(0);
        pushes.Should().Be(0);
    }

    // === 确定性 ===

    [Fact]
    public void ExtractLanguageAndFileStats_Deterministic_SameInputSameOutput() {
        var entry = new TranscriptEntry { Content = "git commit on file.cs" };
        var langs1 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var langs2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files1 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var c1 = 0; var p1 = 0; var a1 = 0; var r1 = 0;
        var c2 = 0; var p2 = 0; var a2 = 0; var r2 = 0;

        SessionScanner.ExtractLanguageAndFileStats(entry, langs1, files1, ref c1, ref p1, ref a1, ref r1);
        SessionScanner.ExtractLanguageAndFileStats(entry, langs2, files2, ref c2, ref p2, ref a2, ref r2);

        langs1.Should().BeEquivalentTo(langs2);
        c1.Should().Be(c2);
        p1.Should().Be(p2);
    }
}
