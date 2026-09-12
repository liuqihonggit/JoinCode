namespace Hands.Tests.ToolHandlers;

/// <summary>
/// FileToolHandlers 错误诊断方法单元测试。
/// 验证每个诊断方法的 Reason、FormattedMessage、Details 结构正确，且 FormattedMessage 与原有错误文本向后兼容。
/// </summary>
public class FileToolHandlersErrorDiagnosticTests
{
    [Fact]
    public void BuildValidationErrorDiagnostic_ReturnsCorrectStructure()
    {
        const string validationError = "file_path is required";
        var diag = FileToolHandlers.BuildValidationErrorDiagnostic(validationError);
        diag.Reason.Should().Be("FileValidationError");
        diag.FormattedMessage.Should().Be(validationError);
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == validationError);
    }

    [Fact]
    public void BuildUncPathWriteRejectedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildUncPathWriteRejectedDiagnostic();
        diag.Reason.Should().Be("UncPathWriteRejected");
        diag.FormattedMessage.Should().Be("Cannot write UNC path files (starting with \\\\), this may lead to credential leakage");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "UncPath");
    }

    [Fact]
    public void BuildUncPathEditRejectedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildUncPathEditRejectedDiagnostic();
        diag.Reason.Should().Be("UncPathEditRejected");
        diag.FormattedMessage.Should().Be("Cannot edit UNC path files (starting with \\\\), this may lead to credential leakage");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "UncPath");
    }

    [Fact]
    public void BuildTeamMemSecretRejectedDiagnostic_ReturnsCorrectStructure()
    {
        const string secretError = "Secret detected in content";
        var diag = FileToolHandlers.BuildTeamMemSecretRejectedDiagnostic(secretError);
        diag.Reason.Should().Be("TeamMemSecretRejected");
        diag.FormattedMessage.Should().Be(secretError);
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == secretError);
    }

    [Fact]
    public void BuildFileNotReadBeforeWriteDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildFileNotReadBeforeWriteDiagnostic();
        diag.Reason.Should().Be("FileNotReadBeforeWrite");
        diag.FormattedMessage.Should().Be("File has not been read yet. Read it first before writing to it. Use the Read tool to examine the file, then write your changes.");
        diag.Details.Should().Contain(d => d.Key == "operation" && d.Value == "write");
    }

    [Fact]
    public void BuildFileNotReadBeforeEditDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildFileNotReadBeforeEditDiagnostic();
        diag.Reason.Should().Be("FileNotReadBeforeEdit");
        diag.FormattedMessage.Should().Be("File has not been read yet. Read it first before editing it. Use the Read tool to examine the file, then make your edits.");
        diag.Details.Should().Contain(d => d.Key == "operation" && d.Value == "edit");
    }

    [Fact]
    public void BuildFileModifiedSinceReadDiagnostic_ForWriting_ReturnsCorrectStructure()
    {
        var filePath = @"/tmp/sample.md";
        var lastWriteMs = DateTimeOffset.Parse("2026-08-11T12:03:09.950Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var readTimestampMs = DateTimeOffset.Parse("2026-08-11T12:02:04.486Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var diag = FileToolHandlers.BuildFileModifiedSinceReadDiagnostic("writing", filePath, lastWriteMs, readTimestampMs);
        diag.Reason.Should().Be("FileModifiedSinceRead");
        diag.FormattedMessage.Should().Be($"File {filePath} has been modified since it was last read.\nLast modification: 2026-08-11T12:03:09.950Z\nLast read: 2026-08-11T12:02:04.486Z\nPlease read the file again before modifying it.");
        diag.Details.Should().Contain(d => d.Key == "operation" && d.Value == "writing");
        diag.Details.Should().Contain(d => d.Key == "filePath" && d.Value == filePath);
        diag.Details.Should().Contain(d => d.Key == "lastModification" && d.Value == "2026-08-11T12:03:09.950Z");
        diag.Details.Should().Contain(d => d.Key == "lastRead" && d.Value == "2026-08-11T12:02:04.486Z");
    }

    [Fact]
    public void BuildFileModifiedSinceReadDiagnostic_ForEditing_ReturnsCorrectStructure()
    {
        var filePath = @"/tmp/sample.md";
        var lastWriteMs = DateTimeOffset.Parse("2026-08-11T12:03:09.950Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var readTimestampMs = DateTimeOffset.Parse("2026-08-11T12:02:04.486Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var diag = FileToolHandlers.BuildFileModifiedSinceReadDiagnostic("editing", filePath, lastWriteMs, readTimestampMs);
        diag.Reason.Should().Be("FileModifiedSinceRead");
        diag.FormattedMessage.Should().Be($"File {filePath} has been modified since it was last read.\nLast modification: 2026-08-11T12:03:09.950Z\nLast read: 2026-08-11T12:02:04.486Z\nPlease read the file again before modifying it.");
        diag.Details.Should().Contain(d => d.Key == "operation" && d.Value == "editing");
        diag.Details.Should().Contain(d => d.Key == "filePath" && d.Value == filePath);
        diag.Details.Should().Contain(d => d.Key == "lastModification" && d.Value == "2026-08-11T12:03:09.950Z");
        diag.Details.Should().Contain(d => d.Key == "lastRead" && d.Value == "2026-08-11T12:02:04.486Z");
    }

    [Fact]
    public void BuildNotebookEditRejectedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildNotebookEditRejectedDiagnostic();
        diag.Reason.Should().Be("NotebookEditRejected");
        diag.FormattedMessage.Should().Be("This is a Jupyter Notebook file. Use the notebook_edit tool to edit this file.");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "NotebookFile");
    }

    [Fact]
    public void BuildIdenticalStringsDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildIdenticalStringsDiagnostic();
        diag.Reason.Should().Be("IdenticalStrings");
        diag.FormattedMessage.Should().Be("old_string and new_string are identical, no changes needed");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "Identical");
    }

    [Fact]
    public void BuildSettingsEditRejectedDiagnostic_ReturnsCorrectStructure()
    {
        const string settingsError = "Invalid settings format";
        var diag = FileToolHandlers.BuildSettingsEditRejectedDiagnostic(settingsError);
        diag.Reason.Should().Be("SettingsEditRejected");
        diag.FormattedMessage.Should().Be(settingsError);
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == settingsError);
    }

    [Fact]
    public void BuildKeywordSectionsEditRejectedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildKeywordSectionsEditRejectedDiagnostic();
        diag.Reason.Should().Be("KeywordSectionsEditRejected");
        diag.FormattedMessage.Should().Be("keyword-sections.json 只能由 keywordMaintenance Agent 编辑");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "PermissionDenied");
    }

    [Fact]
    public void BuildDoctorAgentEditRejectedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildDoctorAgentEditRejectedDiagnostic();
        diag.Reason.Should().Be("DoctorAgentEditRejected");
        diag.FormattedMessage.Should().Be("doctor Agent 只能编辑 .jcc/diag/、.jcc/reflexion/ 和 worktree 内文件");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "PermissionDenied");
    }

    [Fact]
    public void BuildListDirectoryFailedDiagnostic_ReturnsCorrectStructure()
    {
        const string errorMessage = "Directory not found";
        var diag = FileToolHandlers.BuildListDirectoryFailedDiagnostic(errorMessage);
        diag.Reason.Should().Be("ListDirectoryFailed");
        diag.FormattedMessage.Should().Be(errorMessage);
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == errorMessage);
    }

    [Fact]
    public void BuildFileEditServiceNotInitializedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildFileEditServiceNotInitializedDiagnostic();
        diag.Reason.Should().Be("FileEditServiceNotInitialized");
        diag.FormattedMessage.Should().Be("File edit service is not initialized");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "NotInitialized");
    }

    [Fact]
    public void BuildFileChunkingServiceNotInitializedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildFileChunkingServiceNotInitializedDiagnostic();
        diag.Reason.Should().Be("FileChunkingServiceNotInitialized");
        diag.FormattedMessage.Should().Be("File chunking service is not initialized");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "NotInitialized");
    }

    [Fact]
    public void BuildFilePathRequiredDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildFilePathRequiredDiagnostic();
        diag.Reason.Should().Be("FilePathRequired");
        diag.FormattedMessage.Should().Be("At least one file path is required");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "EmptyPaths");
    }

    [Fact]
    public void BuildImageBase64TooLargeDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildImageBase64TooLargeDiagnostic(6000000, 5242880);
        diag.Reason.Should().Be("ImageBase64TooLarge");
        diag.FormattedMessage.Should().Be("Image base64 size (6000000 bytes) exceeds API limit (5242880 bytes). Please use a smaller image.");
        diag.Details.Should().Contain(d => d.Key == "base64Length" && d.Value == "6000000");
        diag.Details.Should().Contain(d => d.Key == "limit" && d.Value == "5242880");
    }

    [Fact]
    public void BuildImageTokenExceededDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildImageTokenExceededDiagnostic(30000, 240000, 25000);
        diag.Reason.Should().Be("ImageTokenExceeded");
        diag.FormattedMessage.Should().Be("Image content (30000 tokens, 240000 bytes) exceeds maximum allowed tokens (25000). Try reading a smaller image or use offset/limit on text files instead.");
        diag.Details.Should().Contain(d => d.Key == "estimatedTokens" && d.Value == "30000");
        diag.Details.Should().Contain(d => d.Key == "bufferSize" && d.Value == "240000");
        diag.Details.Should().Contain(d => d.Key == "maxTokens" && d.Value == "25000");
    }

    [Fact]
    public void BuildPdfInvalidPagesDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildPdfInvalidPagesDiagnostic("abc");
        diag.Reason.Should().Be("PdfInvalidPages");
        diag.FormattedMessage.Should().Be("Invalid pages parameter: \"abc\". Use formats like \"1-5\", \"3\", or \"10-20\". Pages are 1-indexed.");
        diag.Details.Should().Contain(d => d.Key == "pages" && d.Value == "abc");
    }

    [Fact]
    public void BuildPdfPageRangeExceedsMaxDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildPdfPageRangeExceedsMaxDiagnostic("1-30", 20);
        diag.Reason.Should().Be("PdfPageRangeExceedsMax");
        diag.FormattedMessage.Should().Be("Page range \"1-30\" exceeds maximum of 20 pages per request. Please use a smaller range.");
        diag.Details.Should().Contain(d => d.Key == "pages" && d.Value == "1-30");
        diag.Details.Should().Contain(d => d.Key == "maxPages" && d.Value == "20");
    }

    [Fact]
    public void BuildPdfFallbackReadFailedDiagnostic_ReturnsCorrectStructure()
    {
        const string errorMessage = "PDF parse error";
        var diag = FileToolHandlers.BuildPdfFallbackReadFailedDiagnostic(errorMessage);
        diag.Reason.Should().Be("PdfFallbackReadFailed");
        diag.FormattedMessage.Should().Be(errorMessage);
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == errorMessage);
    }

    [Fact]
    public void BuildPdfExtractFailedDiagnostic_ReturnsCorrectStructure()
    {
        const string errorMessage = "Page 5 not found";
        var diag = FileToolHandlers.BuildPdfExtractFailedDiagnostic(errorMessage);
        diag.Reason.Should().Be("PdfExtractFailed");
        diag.FormattedMessage.Should().Be(errorMessage);
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == errorMessage);
    }

    [Fact]
    public void BuildApplyPatchNotAvailableDiagnostic_ReturnsCorrectStructure()
    {
        var diag = FileToolHandlers.BuildApplyPatchNotAvailableDiagnostic();
        diag.Reason.Should().Be("ApplyPatchNotAvailable");
        diag.FormattedMessage.Should().Be("ApplyPatchLogic is not available");
        diag.Details.Should().Contain(d => d.Key == "reason" && d.Value == "NotInitialized");
    }

    [Fact]
    public void BuildApplyPatchFailedDiagnostic_ReturnsCorrectStructure()
    {
        const string errorText = "Patch did not apply";
        var diag = FileToolHandlers.BuildApplyPatchFailedDiagnostic(errorText);
        diag.Reason.Should().Be("ApplyPatchFailed");
        diag.FormattedMessage.Should().Be(errorText);
        diag.Details.Should().Contain(d => d.Key == "Error" && d.Value == errorText);
    }
}
