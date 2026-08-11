namespace Hands.Tests.ToolHandlers;

/// <summary>
/// NotebookToolHandlers 错误诊断方法单元测试。
/// 验证每个 BuildXxxDiagnostic 方法返回的 ToolDiagnostic 结构正确，
/// 且 FormattedMessage 与原有错误文本完全一致（向后兼容）。
/// </summary>
public class NotebookToolHandlersErrorDiagnosticTests
{
    [Fact]
    public void BuildNotebookPathEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildNotebookPathEmptyDiagnostic();
        diag.Reason.Should().Be("NotebookPathEmpty");
        diag.FormattedMessage.Should().Be("notebook_path cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "notebook_path");
    }

    [Fact]
    public void BuildUncPathNotAllowedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildUncPathNotAllowedDiagnostic();
        diag.Reason.Should().Be("UncPathNotAllowed");
        diag.FormattedMessage.Should().Be("UNC paths are not allowed for security reasons (potential NTLM credential leakage). Use a local path instead.");
        diag.Details.Should().Contain(d => d.Key == "Reason" && d.Value == "NTLM credential leakage risk");
    }

    [Fact]
    public void BuildNotIpynbFileDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildNotIpynbFileDiagnostic();
        diag.Reason.Should().Be("NotIpynbFile");
        diag.FormattedMessage.Should().Be("File must be a Jupyter notebook (.ipynb file). For editing other file types, use the FileEdit tool.");
        diag.Details.Should().Contain(d => d.Key == "ExpectedExtension" && d.Value == ".ipynb");
    }

    [Fact]
    public void BuildEditModeInvalidDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildEditModeInvalidDiagnostic();
        diag.Reason.Should().Be("EditModeInvalid");
        diag.FormattedMessage.Should().Be("edit_mode must be replace, insert, or delete");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "edit_mode");
        diag.Details.Should().Contain(d => d.Key == "ValidValues" && d.Value == "replace, insert, delete");
    }

    [Fact]
    public void BuildCellTypeRequiredForInsertDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildCellTypeRequiredForInsertDiagnostic();
        diag.Reason.Should().Be("CellTypeRequiredForInsert");
        diag.FormattedMessage.Should().Be("cell_type is required when using edit_mode=insert");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "cell_type");
        diag.Details.Should().Contain(d => d.Key == "EditMode" && d.Value == "insert");
    }

    [Fact]
    public void BuildCellIdRequiredDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildCellIdRequiredDiagnostic();
        diag.Reason.Should().Be("CellIdRequired");
        diag.FormattedMessage.Should().Be("cell_id must be specified when not inserting a new cell");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "cell_id");
    }

    [Fact]
    public void BuildPlanModeForbiddenDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildPlanModeForbiddenDiagnostic();
        diag.Reason.Should().Be("PlanModeForbidden");
        diag.FormattedMessage.Should().Be("Cannot edit notebook in plan mode. Exit plan mode first before editing files.");
        diag.Details.Should().Contain(d => d.Key == "CurrentMode" && d.Value == "Plan");
    }

    [Fact]
    public void BuildFileNotReadDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildFileNotReadDiagnostic();
        diag.Reason.Should().Be("FileNotRead");
        diag.FormattedMessage.Should().Be("File has not been read yet. Read it first before writing to it.");
        diag.Details.Should().Contain(d => d.Key == "Requirement" && d.Value == "Read-before-Edit");
    }

    [Fact]
    public void BuildFileModifiedSinceReadDiagnostic_ReturnsCorrectStructure()
    {
        var filePath = @"/tmp/sample.md";
        var lastWriteMs = DateTimeOffset.Parse("2026-08-11T12:03:09.950Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var readTimestampMs = DateTimeOffset.Parse("2026-08-11T12:02:04.486Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var diag = NotebookToolHandlers.BuildFileModifiedSinceReadDiagnostic(filePath, lastWriteMs, readTimestampMs);
        diag.Reason.Should().Be("FileModifiedSinceRead");
        diag.FormattedMessage.Should().Be($"File {filePath} has been modified since it was last read.\nLast modification: 2026-08-11T12:03:09.950Z\nLast read: 2026-08-11T12:02:04.486Z\nPlease read the file again before modifying it.");
        diag.Details.Should().Contain(d => d.Key == "filePath" && d.Value == filePath);
        diag.Details.Should().Contain(d => d.Key == "lastModification" && d.Value == "2026-08-11T12:03:09.950Z");
        diag.Details.Should().Contain(d => d.Key == "lastRead" && d.Value == "2026-08-11T12:02:04.486Z");
        diag.Details.Should().Contain(d => d.Key == "Tolerance" && d.Value == "1s");
    }

    [Fact]
    public void BuildNotebookInvalidJsonDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildNotebookInvalidJsonDiagnostic();
        diag.Reason.Should().Be("NotebookInvalidJson");
        diag.FormattedMessage.Should().Be("Notebook is not valid JSON");
        diag.Details.Should().Contain(d => d.Key == "Expectation" && d.Value == "Valid .ipynb JSON structure");
    }

    [Fact]
    public void BuildCellOperationFailedDiagnostic_WithErrorMessage_UsesProvidedMessage()
    {
        var diag = NotebookToolHandlers.BuildCellOperationFailedDiagnostic("DeleteCell", "cell index out of range", "Failed to delete cell");
        diag.Reason.Should().Be("DeleteCellFailed");
        diag.FormattedMessage.Should().Be("cell index out of range");
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "DeleteCell");
        diag.Details.Should().Contain(d => d.Key == "ErrorMessage" && d.Value == "cell index out of range");
    }

    [Fact]
    public void BuildCellOperationFailedDiagnostic_WithNullErrorMessage_UsesFallbackMessage()
    {
        var diag = NotebookToolHandlers.BuildCellOperationFailedDiagnostic("InsertCell", null, "Failed to insert cell");
        diag.Reason.Should().Be("InsertCellFailed");
        diag.FormattedMessage.Should().Be("Failed to insert cell");
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "InsertCell");
        diag.Details.Should().Contain(d => d.Key == "ErrorMessage" && d.Value == "Failed to insert cell");
    }

    [Fact]
    public void BuildSaveNotebookFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildSaveNotebookFailedDiagnostic();
        diag.Reason.Should().Be("SaveNotebookFailed");
        diag.FormattedMessage.Should().Be("Failed to save notebook");
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "SaveAsync");
    }

    [Fact]
    public void BuildFilePathEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildFilePathEmptyDiagnostic();
        diag.Reason.Should().Be("NotebookFilePathEmpty");
        diag.FormattedMessage.Should().Be(L.T(StringKey.NotebookFilePathCannotBeEmpty));
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "file_path");
    }

    [Fact]
    public void BuildFileNotExistDiagnostic_ReturnsCorrectStructure()
    {
        const string path = "/tmp/missing.ipynb";
        var diag = NotebookToolHandlers.BuildFileNotExistDiagnostic(path);
        diag.Reason.Should().Be("NotebookFileNotExist");
        diag.FormattedMessage.Should().Be(L.T(StringKey.NotebookFileNotExist, path));
        diag.Details.Should().Contain(d => d.Key == "FilePath" && d.Value == path);
    }

    [Fact]
    public void BuildNotebookParseFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildNotebookParseFailedDiagnostic();
        diag.Reason.Should().Be("NotebookParseFailed");
        diag.FormattedMessage.Should().Be(L.T(StringKey.NotebookParseFailed));
        diag.Details.Should().Contain(d => d.Key == "Expectation" && d.Value == "Valid .ipynb JSON structure");
    }

    [Fact]
    public void BuildNotebookSaveFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = NotebookToolHandlers.BuildNotebookSaveFailedDiagnostic();
        diag.Reason.Should().Be("NotebookSaveFailed");
        diag.FormattedMessage.Should().Be(L.T(StringKey.NotebookSaveFailed));
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "SaveAsync");
    }

    [Fact]
    public void BuildFileAlreadyExistsDiagnostic_ReturnsCorrectStructure()
    {
        const string path = "/tmp/existing.ipynb";
        var diag = NotebookToolHandlers.BuildFileAlreadyExistsDiagnostic(path);
        diag.Reason.Should().Be("NotebookFileAlreadyExists");
        diag.FormattedMessage.Should().Be(L.T(StringKey.NotebookFileAlreadyExists, path));
        diag.Details.Should().Contain(d => d.Key == "FilePath" && d.Value == path);
    }

    [Fact]
    public void BuildInvalidCellTypeDiagnostic_ReturnsCorrectStructure()
    {
        const string cellType = "invalid";
        var diag = NotebookToolHandlers.BuildInvalidCellTypeDiagnostic(cellType);
        diag.Reason.Should().Be("NotebookInvalidCellType");
        diag.FormattedMessage.Should().Be(L.T(StringKey.NotebookInvalidCellType, cellType));
        diag.Details.Should().Contain(d => d.Key == "CellType" && d.Value == cellType);
        diag.Details.Should().Contain(d => d.Key == "ValidValues" && d.Value == "code, markdown, raw");
    }

    [Fact]
    public void BuildInvalidTypeDiagnostic_ReturnsCorrectStructure()
    {
        const string newType = "unknown";
        var diag = NotebookToolHandlers.BuildInvalidTypeDiagnostic(newType);
        diag.Reason.Should().Be("NotebookInvalidType");
        diag.FormattedMessage.Should().Be(L.T(StringKey.NotebookInvalidType, newType));
        diag.Details.Should().Contain(d => d.Key == "NewType" && d.Value == newType);
        diag.Details.Should().Contain(d => d.Key == "ValidValues" && d.Value == "code, markdown, raw");
    }

    [Fact]
    public void BuildInvalidCellIndexDiagnostic_ReturnsCorrectStructure()
    {
        const int index = 42;
        var diag = NotebookToolHandlers.BuildInvalidCellIndexDiagnostic(index);
        diag.Reason.Should().Be("NotebookInvalidCellIndex");
        diag.FormattedMessage.Should().Be(L.T(StringKey.NotebookInvalidCellIndex, index));
        diag.Details.Should().Contain(d => d.Key == "Index" && d.Value == "42");
    }
}
