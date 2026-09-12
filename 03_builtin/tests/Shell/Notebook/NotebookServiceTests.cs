namespace Hands.Tests.Notebook;

public sealed class NotebookServiceTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOperationService;
    private readonly NotebookService _service;

    public NotebookServiceTests()
    {
        _fileOperationService = new InMemoryFileOperationService();
        _service = new NotebookService(_fileOperationService, _fileOperationService.FileSystem);
    }

    public void Dispose()
    {
        _fileOperationService.Dispose();
    }

    [Fact]
    public void Create_WithoutKernel_ReturnsEmptyNotebook()
    {
        var doc = _service.Create();

        doc.NbFormat.Should().Be(4);
        doc.NbFormatMinor.Should().Be(5);
        doc.Cells.Should().BeEmpty();
        doc.Metadata.KernelSpec.Should().BeNull();
        doc.Metadata.LanguageInfo.Should().BeNull();
    }

    [Fact]
    public void Create_WithKernelAndLanguage_SetsMetadata()
    {
        var doc = _service.Create("Python 3", "python");

        doc.Metadata.KernelSpec.Should().NotBeNull();
        doc.Metadata.KernelSpec!.DisplayName.Should().Be("Python 3");
        doc.Metadata.KernelSpec.Language.Should().Be("python");
        doc.Metadata.KernelSpec.Name.Should().Be("python 3");
        doc.Metadata.LanguageInfo.Should().NotBeNull();
        doc.Metadata.LanguageInfo!.Name.Should().Be("python");
        doc.Metadata.LanguageInfo.MimeType.Should().Be("text/x-python");
        doc.Metadata.LanguageInfo.FileExtension.Should().Be(".py");
    }

    [Fact]
    public void AddCell_AppendsToEnd()
    {
        var doc = _service.Create();
        var result = _service.AddCell(doc, NotebookCellType.Code, "print('hi')");

        result.Success.Should().BeTrue();
        result.AffectedCellIndex.Should().Be(0);
        doc.Cells.Should().HaveCount(1);
        doc.Cells[0].CellType.Should().Be("code");
        doc.Cells[0].SourceText.Should().Be("print('hi')");
    }

    [Fact]
    public void AddCell_AtSpecificIndex_InsertsThere()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "first");
        _service.AddCell(doc, NotebookCellType.Markdown, "second");

        var result = _service.AddCell(doc, NotebookCellType.Code, "middle", 1);

        result.AffectedCellIndex.Should().Be(1);
        doc.Cells[1].SourceText.Should().Be("middle");
    }

    [Fact]
    public void AddCell_NegativeIndex_ClampedToZero()
    {
        var doc = _service.Create();

        var result = _service.AddCell(doc, NotebookCellType.Code, "first", -1);

        result.AffectedCellIndex.Should().Be(0);
    }

    [Fact]
    public void AddCell_IndexBeyondCount_AppendsAtEnd()
    {
        var doc = _service.Create();

        var result = _service.AddCell(doc, NotebookCellType.Code, "first", 100);

        result.AffectedCellIndex.Should().Be(0);
    }

    [Fact]
    public void AddCell_GeneratesId_WhenNbFormatMinor45()
    {
        var doc = _service.Create();

        _service.AddCell(doc, NotebookCellType.Code, "x");

        doc.Cells[0].Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AddCell_DoesNotGenerateId_WhenOldFormat()
    {
        var doc = new NotebookDocument { NbFormat = 4, NbFormatMinor = 4, Metadata = new NotebookMetadata(), Cells = new List<NotebookCell>() };

        _service.AddCell(doc, NotebookCellType.Code, "x");

        doc.Cells[0].Id.Should().BeNull();
    }

    [Fact]
    public void DeleteCell_RemovesCell()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "x");

        var result = _service.DeleteCell(doc, 0);

        result.Success.Should().BeTrue();
        doc.Cells.Should().BeEmpty();
    }

    [Fact]
    public void DeleteCell_InvalidIndex_ReturnsError()
    {
        var doc = _service.Create();

        var result = _service.DeleteCell(doc, 0);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("无效的单元格索引");
    }

    [Fact]
    public void EditCell_UpdatesContent()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "old");

        var result = _service.EditCell(doc, 0, "new");

        result.Success.Should().BeTrue();
        doc.Cells[0].SourceText.Should().Be("new");
    }

    [Fact]
    public void EditCell_ChangesType()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "x");

        var result = _service.EditCell(doc, 0, "x", "markdown");

        result.Success.Should().BeTrue();
        doc.Cells[0].CellType.Should().Be("markdown");
    }

    [Fact]
    public void MoveCell_ReordersCells()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "first");
        _service.AddCell(doc, NotebookCellType.Code, "second");
        _service.AddCell(doc, NotebookCellType.Code, "third");

        var result = _service.MoveCell(doc, 0, 2);

        result.Success.Should().BeTrue();
        result.AffectedCellIndex.Should().Be(1);
        doc.Cells[0].SourceText.Should().Be("second");
        doc.Cells[1].SourceText.Should().Be("first");
        doc.Cells[2].SourceText.Should().Be("third");
    }

    [Fact]
    public void MoveCell_InvalidFromIndex_ReturnsError()
    {
        var doc = _service.Create();

        var result = _service.MoveCell(doc, 0, 0);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("无效的源索引");
    }

    [Fact]
    public void MoveCell_InvalidToIndex_ReturnsError()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "x");

        var result = _service.MoveCell(doc, 0, 2);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("无效的目标索引");
    }

    [Fact]
    public void ChangeCellType_SameType_DoesNotModifyCell()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "x");
        var originalId = doc.Cells[0].Id;

        var result = _service.ChangeCellType(doc, 0, NotebookCellType.Code);

        result.Success.Should().BeTrue();
        doc.Cells[0].CellType.Should().Be("code");
        doc.Cells[0].Id.Should().Be(originalId);
    }

    [Fact]
    public void ChangeCellType_ToCode_ClearsOutputs()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Markdown, "x");

        var result = _service.ChangeCellType(doc, 0, NotebookCellType.Code);

        result.Success.Should().BeTrue();
        doc.Cells[0].CellType.Should().Be("code");
        doc.Cells[0].Outputs.Should().NotBeNull().And.BeEmpty();
        doc.Cells[0].ExecutionCount.Should().BeNull();
    }

    [Fact]
    public void ExecuteCell_NonCodeCell_ReturnsError()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Markdown, "x");

        var result = _service.ExecuteCell(doc, 0);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("只有代码单元格可以执行");
    }

    [Fact]
    public void ExecuteCell_CodeCell_SetsExecutionCountAndOutput()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "print('hi')");

        var result = _service.ExecuteCell(doc, 0, "hi");

        result.Success.Should().BeTrue();
        doc.Cells[0].ExecutionCount.Should().Be(1);
        doc.Cells[0].Outputs.Should().ContainSingle();
    }

    [Fact]
    public void ExecuteCell_MultipleCells_IncrementsExecutionCount()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "x");
        _service.ExecuteCell(doc, 0);
        _service.AddCell(doc, NotebookCellType.Code, "y");

        var result = _service.ExecuteCell(doc, 1);

        doc.Cells[1].ExecutionCount.Should().Be(2);
    }

    [Fact]
    public void ClearAllOutputs_ResetsCodeCells()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "x");
        _service.ExecuteCell(doc, 0, "output");

        var result = _service.ClearAllOutputs(doc);

        result.Success.Should().BeTrue();
        doc.Cells[0].Outputs.Should().NotBeNull().And.BeEmpty();
        doc.Cells[0].ExecutionCount.Should().BeNull();
    }

    [Fact]
    public void GetCellContent_ReturnsSourceText()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "hello");

        var content = _service.GetCellContent(doc, 0);

        content.Should().Be("hello");
    }

    [Fact]
    public void GetCellContent_InvalidIndex_ReturnsNull()
    {
        var doc = _service.Create();

        var content = _service.GetCellContent(doc, 0);

        content.Should().BeNull();
    }

    [Fact]
    public void ListCells_TruncatesLongContent()
    {
        var doc = _service.Create();
        var longText = new string('a', 60);
        _service.AddCell(doc, NotebookCellType.Code, longText);

        var cells = _service.ListCells(doc);

        cells.Should().ContainSingle();
        cells[0].Preview.Should().EndWith("...");
        cells[0].Preview.Length.Should().BeLessThan(60);
    }

    [Fact]
    public void ListCells_ReplacesNewlinesInPreview()
    {
        var doc = _service.Create();
        _service.AddCell(doc, NotebookCellType.Code, "line1\nline2");

        var cells = _service.ListCells(doc);

        cells[0].Preview.Should().NotContain("\n");
    }
}
