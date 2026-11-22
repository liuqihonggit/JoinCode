
namespace Core.Tests.Memdir;

/// <summary>
/// SessionScanner 纯逻辑确定性测试
/// CategorizeToolError: 6 类错误字符串匹配 + Other
/// ExtractLanguageAndFileStats: 语言/Git 统计
/// </summary>
[Trait("Category", "Deterministic")]
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

    // === ProcessAssistantEntry: 助手消息统计 ===

    [Fact]
    public void ProcessAssistantEntry_PlainAssistant_IncrementsCountAndTokens() {
        var entry = new TranscriptEntry {
            Role = "assistant",
            PromptTokens = 100,
            CompletionTokens = 50,
            Timestamp = new DateTime(2026, 1, 1)
        };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        SessionScanner.ProcessAssistantEntry(entry, toolCounts,
            ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
            ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);

        assistantCount.Should().Be(1);
        inputTokens.Should().Be(100);
        outputTokens.Should().Be(50);
        lastTime.Should().Be(new DateTime(2026, 1, 1));
        toolCounts.Should().BeEmpty();
        usesMcp.Should().BeFalse();
    }

    [Fact]
    public void ProcessAssistantEntry_WithTool_RecordsToolCount() {
        var entry = new TranscriptEntry { Role = "assistant", ToolName = "bash" };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        SessionScanner.ProcessAssistantEntry(entry, toolCounts,
            ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
            ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);

        toolCounts.Should().ContainKey("bash").WhoseValue.Should().Be(1);
    }

    [Fact]
    public void ProcessAssistantEntry_McpTool_SetsUsesMcpFlag() {
        var entry = new TranscriptEntry { Role = "assistant", ToolName = "mcp__server__tool" };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        SessionScanner.ProcessAssistantEntry(entry, toolCounts,
            ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
            ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);

        usesMcp.Should().BeTrue();
        usesWebSearch.Should().BeFalse();
        usesTaskAgent.Should().BeFalse();
    }

    [Fact]
    public void ProcessAssistantEntry_WebSearchTool_SetsUsesWebSearchFlag() {
        var entry = new TranscriptEntry { Role = "assistant", ToolName = WebToolNameEnumConstants.WebSearch };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        SessionScanner.ProcessAssistantEntry(entry, toolCounts,
            ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
            ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);

        usesWebSearch.Should().BeTrue();
        usesWebFetch.Should().BeFalse();
    }

    [Fact]
    public void ProcessAssistantEntry_WebFetchTool_SetsUsesWebFetchFlag() {
        var entry = new TranscriptEntry { Role = "assistant", ToolName = WebToolNameEnumConstants.WebFetch };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        SessionScanner.ProcessAssistantEntry(entry, toolCounts,
            ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
            ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);

        usesWebFetch.Should().BeTrue();
    }

    [Fact]
    public void ProcessAssistantEntry_AgentTool_SetsUsesTaskAgentFlag() {
        var entry = new TranscriptEntry { Role = "assistant", ToolName = AgentToolNameEnumConstants.Agent };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        SessionScanner.ProcessAssistantEntry(entry, toolCounts,
            ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
            ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);

        usesTaskAgent.Should().BeTrue();
    }

    [Fact]
    public void ProcessAssistantEntry_LegacyTaskTool_SetsUsesTaskAgentFlag() {
        // 旧工具名 "Task" 也应触发 usesTaskAgent
        var entry = new TranscriptEntry { Role = "assistant", ToolName = "Task" };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        SessionScanner.ProcessAssistantEntry(entry, toolCounts,
            ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
            ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);

        usesTaskAgent.Should().BeTrue();
    }

    [Fact]
    public void ProcessAssistantEntry_MultipleCalls_AccumulateCounts() {
        var entry = new TranscriptEntry { Role = "assistant", ToolName = "bash", PromptTokens = 10, CompletionTokens = 5 };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        for (var i = 0; i < 3; i++) {
            SessionScanner.ProcessAssistantEntry(entry, toolCounts,
                ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
                ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);
        }

        assistantCount.Should().Be(3);
        inputTokens.Should().Be(30);
        outputTokens.Should().Be(15);
        toolCounts["bash"].Should().Be(3);
    }

    [Fact]
    public void ProcessAssistantEntry_DefaultTimestamp_DoesNotUpdateLastTime() {
        var entry = new TranscriptEntry { Role = "assistant", Timestamp = default };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = new DateTime(2026, 1, 1);
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        SessionScanner.ProcessAssistantEntry(entry, toolCounts,
            ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
            ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);

        lastTime.Should().Be(new DateTime(2026, 1, 1));
    }

    // === ProcessUserEntry: 用户消息统计 ===

    [Fact]
    public void ProcessUserEntry_HumanTextMessage_IncrementsCountAndSetsFirstPrompt() {
        var entry = new TranscriptEntry { Role = "user", Content = "hello world", Timestamp = new DateTime(2026, 1, 1) };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        userCount.Should().Be(1);
        firstPrompt.Should().Be("hello world");
        timestamps.Should().ContainSingle().Which.Should().Be(new DateTime(2026, 1, 1));
        interruptions.Should().Be(0);
    }

    [Fact]
    public void ProcessUserEntry_ToolResultType_DoesNotIncrementCount() {
        var entry = new TranscriptEntry { Role = "user", Content = "result text", Type = "tool_result" };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        userCount.Should().Be(0);
        firstPrompt.Should().BeNull();
        timestamps.Should().BeEmpty();
    }

    [Fact]
    public void ProcessUserEntry_WhitespaceContent_DoesNotIncrementCount() {
        var entry = new TranscriptEntry { Role = "user", Content = "   " };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        userCount.Should().Be(0);
        firstPrompt.Should().BeNull();
    }

    [Fact]
    public void ProcessUserEntry_LongContent_TruncatesFirstPromptTo200Chars() {
        var longContent = new string('x', 300);
        var entry = new TranscriptEntry { Role = "user", Content = longContent };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        firstPrompt.Should().HaveLength(200);
        firstPrompt.Should().Be(new string('x', 200));
    }

    [Fact]
    public void ProcessUserEntry_FirstPromptSetOnce_NotOverwrittenByLaterMessages() {
        var entry1 = new TranscriptEntry { Role = "user", Content = "first" };
        var entry2 = new TranscriptEntry { Role = "user", Content = "second" };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry1, ref userCount, ref firstPrompt, timestamps, ref interruptions);
        SessionScanner.ProcessUserEntry(entry2, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        firstPrompt.Should().Be("first");
        userCount.Should().Be(2);
    }

    [Fact]
    public void ProcessUserEntry_InterruptedContent_IncrementsInterruptions() {
        var entry = new TranscriptEntry { Role = "user", Content = "[Request interrupted by user]" };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        interruptions.Should().Be(1);
        // 中断内容也是人类消息,应计数
        userCount.Should().Be(1);
    }

    [Fact]
    public void ProcessUserEntry_InterruptedContent_CaseInsensitive() {
        var entry = new TranscriptEntry { Role = "user", Content = "[REQUEST INTERRUPTED BY USER]" };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        interruptions.Should().Be(1);
    }

    // === DetectUserInterruption ===

    [Fact]
    public void DetectUserInterruption_ContainsMarker_ReturnsTrue() {
        SessionScanner.DetectUserInterruption("[Request interrupted by user]").Should().BeTrue();
    }

    [Fact]
    public void DetectUserInterruption_NoMarker_ReturnsFalse() {
        SessionScanner.DetectUserInterruption("normal user message").Should().BeFalse();
    }

    [Fact]
    public void DetectUserInterruption_CaseInsensitive_ReturnsTrue() {
        SessionScanner.DetectUserInterruption("[request Interrupted By User]").Should().BeTrue();
    }

    [Fact]
    public void DetectUserInterruption_MarkerInLongerText_ReturnsTrue() {
        SessionScanner.DetectUserInterruption("some prefix [Request interrupted by user] suffix").Should().BeTrue();
    }

    [Fact]
    public void DetectUserInterruption_EmptyContent_ReturnsFalse() {
        SessionScanner.DetectUserInterruption("").Should().BeFalse();
    }

    // === ProcessToolResultEntry: 工具结果统计 ===

    [Fact]
    public void ProcessToolResultEntry_IsErrorTrue_IncrementsToolErrorsAndCategory() {
        var entry = new TranscriptEntry { Role = "tool", Content = """{"is_error":true}""" };
        var errorCategories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var modifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toolErrors = 0; var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ProcessToolResultEntry(entry, errorCategories, languages, modifiedFiles,
            ref toolErrors, ref commits, ref pushes, ref added, ref removed);

        toolErrors.Should().Be(1);
        errorCategories.Should().ContainKey("Other").WhoseValue.Should().Be(1);
    }

    [Fact]
    public void ProcessToolResultEntry_ExitCode_IncrementsToolErrorsAndCommandFailedCategory() {
        var entry = new TranscriptEntry { Role = "tool", Content = "Command failed with exit code 1" };
        var errorCategories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var modifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toolErrors = 0; var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ProcessToolResultEntry(entry, errorCategories, languages, modifiedFiles,
            ref toolErrors, ref commits, ref pushes, ref added, ref removed);

        toolErrors.Should().Be(1);
        errorCategories.Should().ContainKey("Command Failed").WhoseValue.Should().Be(1);
    }

    [Fact]
    public void ProcessToolResultEntry_NoError_DoesNotIncrementToolErrors() {
        var entry = new TranscriptEntry { Role = "tool", Content = "success output" };
        var errorCategories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var modifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toolErrors = 0; var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ProcessToolResultEntry(entry, errorCategories, languages, modifiedFiles,
            ref toolErrors, ref commits, ref pushes, ref added, ref removed);

        toolErrors.Should().Be(0);
        errorCategories.Should().BeEmpty();
    }

    [Fact]
    public void ProcessToolResultEntry_WithFileExtension_RecordsLanguageEvenWithoutError() {
        var entry = new TranscriptEntry { Role = "tool", Content = "modified src/Program.cs" };
        var errorCategories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var modifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toolErrors = 0; var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ProcessToolResultEntry(entry, errorCategories, languages, modifiedFiles,
            ref toolErrors, ref commits, ref pushes, ref added, ref removed);

        toolErrors.Should().Be(0);
        languages.Should().ContainKey("C#");
    }

    [Fact]
    public void ProcessToolResultEntry_GitCommit_IncrementsCommitCount() {
        var entry = new TranscriptEntry { Role = "tool", Content = "git commit -m feat" };
        var errorCategories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var modifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toolErrors = 0; var commits = 0; var pushes = 0; var added = 0; var removed = 0;

        SessionScanner.ProcessToolResultEntry(entry, errorCategories, languages, modifiedFiles,
            ref toolErrors, ref commits, ref pushes, ref added, ref removed);

        commits.Should().Be(1);
        pushes.Should().Be(0);
    }

    // === BuildSessionMeta: 结果构建 ===

    [Fact]
    public void BuildSessionMeta_MapsAllFieldsCorrectly() {
        var sessionId = "sess-123";
        var creation = new DateTime(2026, 1, 1, 10, 0, 0);
        var duration = 15.678;
        var toolCounts = new Dictionary<string, int> { ["bash"] = 3 };
        var languages = new Dictionary<string, int> { ["C#"] = 2 };
        var modifiedFiles = new HashSet<string> { "a.cs", "b.cs" };
        var errorCategories = new Dictionary<string, int> { ["Other"] = 1 };
        var timestamps = new List<DateTime> { new(2026, 1, 1, 10, 5, 0) };

        var meta = SessionScanner.BuildSessionMeta(sessionId, creation, duration,
            userMessageCount: 5, assistantMessageCount: 10, inputTokens: 1000, outputTokens: 500,
            toolCounts, languages, gitCommits: 2, gitPushes: 1, linesAdded: 50, linesRemoved: 20,
            modifiedFiles, userInterruptions: 1, toolErrors: 3, errorCategories,
            usesTaskAgent: true, usesMcp: true, usesWebSearch: false, usesWebFetch: true,
            firstPrompt: "hello", estimatedCost: 0.05m, timestamps);

        meta.SessionId.Should().Be("sess-123");
        meta.ProjectPath.Should().BeEmpty();
        meta.StartTime.Should().Be(creation);
        meta.DurationMinutes.Should().Be(15.7); // 四舍五入1位
        meta.UserMessageCount.Should().Be(5);
        meta.AssistantMessageCount.Should().Be(10);
        meta.InputTokens.Should().Be(1000);
        meta.OutputTokens.Should().Be(500);
        meta.ToolCounts.Should().ContainKey("bash").WhoseValue.Should().Be(3);
        meta.Languages.Should().ContainKey("C#").WhoseValue.Should().Be(2);
        meta.GitCommits.Should().Be(2);
        meta.GitPushes.Should().Be(1);
        meta.LinesAdded.Should().Be(50);
        meta.LinesRemoved.Should().Be(20);
        meta.FilesModified.Should().Be(2);
        meta.UserInterruptions.Should().Be(1);
        meta.ToolErrors.Should().Be(3);
        meta.UsesTaskAgent.Should().BeTrue();
        meta.UsesMcp.Should().BeTrue();
        meta.UsesWebSearch.Should().BeFalse();
        meta.UsesWebFetch.Should().BeTrue();
        meta.FirstPrompt.Should().Be("hello");
        meta.UserMessageTimestamps.Should().ContainSingle().Which.Should().Be(new DateTime(2026, 1, 1, 10, 5, 0));
    }

    [Fact]
    public void BuildSessionMeta_NullFirstPrompt_ConvertsToEmptyString() {
        var meta = SessionScanner.BuildSessionMeta("s", DateTime.UtcNow, 0,
            0, 0, 0, 0,
            new Dictionary<string, int>(), new Dictionary<string, int>(), 0, 0, 0, 0,
            new HashSet<string>(), 0, 0, new Dictionary<string, int>(),
            false, false, false, false, null, 0m, new List<DateTime>());

        meta.FirstPrompt.Should().BeEmpty();
    }

    [Fact]
    public void BuildSessionMeta_DurationRoundedToOneDecimal() {
        var meta = SessionScanner.BuildSessionMeta("s", DateTime.UtcNow, 12.34,
            0, 0, 0, 0,
            new Dictionary<string, int>(), new Dictionary<string, int>(), 0, 0, 0, 0,
            new HashSet<string>(), 0, 0, new Dictionary<string, int>(),
            false, false, false, false, null, 0m, new List<DateTime>());

        meta.DurationMinutes.Should().Be(12.3);
    }

    [Fact]
    public void BuildSessionMeta_Deterministic_SameInputSameOutput() {
        var toolCounts = new Dictionary<string, int> { ["bash"] = 1 };
        var langs = new Dictionary<string, int>();
        var files = new HashSet<string>();
        var cats = new Dictionary<string, int>();
        var ts = new List<DateTime> { new(2026, 1, 1) };

        var m1 = SessionScanner.BuildSessionMeta("s", new DateTime(2026, 1, 1), 10.0,
            1, 2, 3, 4, toolCounts, langs, 5, 6, 7, 8, files, 9, 10, cats, true, false, true, false, "p", 0m, ts);
        var m2 = SessionScanner.BuildSessionMeta("s", new DateTime(2026, 1, 1), 10.0,
            1, 2, 3, 4, toolCounts, langs, 5, 6, 7, 8, files, 9, 10, cats, true, false, true, false, "p", 0m, ts);

        m1.Should().BeEquivalentTo(m2);
    }

    // === SafeAddTokens: 溢出钳制 ===

    [Fact]
    public void SafeAddTokens_NormalAddition_ReturnsSum() {
        SessionScanner.SafeAddTokens(100L, 50).Should().Be(150L);
    }

    [Fact]
    public void SafeAddTokens_ZeroAddition_ReturnsCurrent() {
        SessionScanner.SafeAddTokens(100L, 0).Should().Be(100L);
    }

    [Fact]
    public void SafeAddTokens_NegativeAddition_ReturnsCurrent() {
        SessionScanner.SafeAddTokens(100L, -50).Should().Be(100L);
    }

    [Fact]
    public void SafeAddTokens_Overflow_ClampsToLongMaxValue() {
        SessionScanner.SafeAddTokens(long.MaxValue - 10, 100).Should().Be(long.MaxValue);
    }

    [Fact]
    public void SafeAddTokens_ExactlyAtBoundary_NoOverflow() {
        SessionScanner.SafeAddTokens(long.MaxValue - 100, 100).Should().Be(long.MaxValue);
    }

    [Fact]
    public void SafeAddTokens_CurrentAtMax_ReturnsMax() {
        SessionScanner.SafeAddTokens(long.MaxValue, 1).Should().Be(long.MaxValue);
    }

    // === ProcessAssistantEntry: long 溢出守卫 ===

    [Fact]
    public void ProcessAssistantEntry_AccumulateIntMaxValue_DoesNotOverflow() {
        var entry = new TranscriptEntry { Role = "assistant", PromptTokens = int.MaxValue, CompletionTokens = int.MaxValue };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0; var inputTokens = 0L; var outputTokens = 0L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        for (var i = 0; i < 3; i++) {
            SessionScanner.ProcessAssistantEntry(entry, toolCounts,
                ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
                ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);
        }

        inputTokens.Should().Be(3L * int.MaxValue);
        outputTokens.Should().Be(3L * int.MaxValue);
        inputTokens.Should().BePositive();
    }

    [Fact]
    public void ProcessAssistantEntry_AccumulateToLongMax_ClampsAndDoesNotWrapNegative() {
        var entry = new TranscriptEntry { Role = "assistant", PromptTokens = int.MaxValue, CompletionTokens = int.MaxValue };
        var toolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assistantCount = 0;
        var inputTokens = long.MaxValue - 100L;
        var outputTokens = long.MaxValue - 100L;
        DateTime? lastTime = null;
        var usesMcp = false; var usesWebSearch = false; var usesWebFetch = false; var usesTaskAgent = false;

        for (var i = 0; i < 3; i++) {
            SessionScanner.ProcessAssistantEntry(entry, toolCounts,
                ref assistantCount, ref inputTokens, ref outputTokens, ref lastTime,
                ref usesMcp, ref usesWebSearch, ref usesWebFetch, ref usesTaskAgent);
        }

        inputTokens.Should().Be(long.MaxValue);
        outputTokens.Should().Be(long.MaxValue);
        inputTokens.Should().BePositive();
    }

    // === ProcessUserEntry: 恰好 200/201 字符边界 ===

    [Fact]
    public void ProcessUserEntry_Exactly200Chars_DoesNotTruncate() {
        var content = new string('x', 200);
        var entry = new TranscriptEntry { Role = "user", Content = content };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        firstPrompt.Should().HaveLength(200);
        firstPrompt.Should().Be(content);
    }

    [Fact]
    public void ProcessUserEntry_Exactly201Chars_TruncatesTo200() {
        var content = new string('x', 201);
        var entry = new TranscriptEntry { Role = "user", Content = content };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        firstPrompt.Should().HaveLength(200);
        firstPrompt.Should().Be(new string('x', 200));
    }

    [Fact]
    public void ProcessUserEntry_Exactly199Chars_DoesNotTruncate() {
        var content = new string('x', 199);
        var entry = new TranscriptEntry { Role = "user", Content = content };
        var userCount = 0; string? firstPrompt = null;
        var timestamps = new List<DateTime>();
        var interruptions = 0;

        SessionScanner.ProcessUserEntry(entry, ref userCount, ref firstPrompt, timestamps, ref interruptions);

        firstPrompt.Should().HaveLength(199);
        firstPrompt.Should().Be(content);
    }

    // === SafeRoundDuration: NaN/Infinity 守卫 ===

    [Fact]
    public void SafeRoundDuration_NormalValue_RoundsToOneDecimal() {
        SessionScanner.SafeRoundDuration(15.678).Should().Be(15.7);
    }

    [Fact]
    public void SafeRoundDuration_NaN_ReturnsZero() {
        SessionScanner.SafeRoundDuration(double.NaN).Should().Be(0);
    }

    [Fact]
    public void SafeRoundDuration_PositiveInfinity_ReturnsZero() {
        SessionScanner.SafeRoundDuration(double.PositiveInfinity).Should().Be(0);
    }

    [Fact]
    public void SafeRoundDuration_NegativeInfinity_ReturnsZero() {
        SessionScanner.SafeRoundDuration(double.NegativeInfinity).Should().Be(0);
    }

    [Fact]
    public void SafeRoundDuration_Zero_ReturnsZero() {
        SessionScanner.SafeRoundDuration(0).Should().Be(0);
    }

    // === BuildSessionMeta: NaN/Infinity duration 守卫 ===

    [Fact]
    public void BuildSessionMeta_NaNDuration_ReturnsZeroDuration() {
        var meta = SessionScanner.BuildSessionMeta("s", DateTime.UtcNow, double.NaN,
            0, 0, 0, 0,
            new Dictionary<string, int>(), new Dictionary<string, int>(), 0, 0, 0, 0,
            new HashSet<string>(), 0, 0, new Dictionary<string, int>(),
            false, false, false, false, null, 0m, new List<DateTime>());

        meta.DurationMinutes.Should().Be(0);
    }

    [Fact]
    public void BuildSessionMeta_PositiveInfinityDuration_ReturnsZeroDuration() {
        var meta = SessionScanner.BuildSessionMeta("s", DateTime.UtcNow, double.PositiveInfinity,
            0, 0, 0, 0,
            new Dictionary<string, int>(), new Dictionary<string, int>(), 0, 0, 0, 0,
            new HashSet<string>(), 0, 0, new Dictionary<string, int>(),
            false, false, false, false, null, 0m, new List<DateTime>());

        meta.DurationMinutes.Should().Be(0);
    }

    [Fact]
    public void BuildSessionMeta_NegativeInfinityDuration_ReturnsZeroDuration() {
        var meta = SessionScanner.BuildSessionMeta("s", DateTime.UtcNow, double.NegativeInfinity,
            0, 0, 0, 0,
            new Dictionary<string, int>(), new Dictionary<string, int>(), 0, 0, 0, 0,
            new HashSet<string>(), 0, 0, new Dictionary<string, int>(),
            false, false, false, false, null, 0m, new List<DateTime>());

        meta.DurationMinutes.Should().Be(0);
    }
}
