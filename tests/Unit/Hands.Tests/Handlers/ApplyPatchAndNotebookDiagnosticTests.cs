using JoinCode.Abstractions.Models.Notebook;
using Services.Notebook.ToolHandlers;

namespace Tools.Handlers.Tests;

public sealed class ApplyPatchDiagnosticTests
{
    [Fact]
    public void BuildContextMismatchMessage_ContainsExpectedAndActual()
    {
        var hunk = new ApplyPatchLogic.PatchHunk
        {
            FilePath = "test.cs",
            StartLine = 5,
        };
        hunk.Lines.Add(" line4");
        hunk.Lines.Add("-line5_old");
        hunk.Lines.Add("+line5_new");

        var fileLines = new List<string> { "line1", "line2", "line3", "line4", "line5_DIFFERENT", "line6" };
        var msg = ApplyPatchLogic.BuildContextMismatchMessage("test.cs", hunk, fileLines, adjustedStart: 3);

        msg.Should().Contain("context mismatch");
        msg.Should().Contain("[诊断]");
        msg.Should().Contain("期望");
        msg.Should().Contain("line5_old");
        msg.Should().Contain("实际");
        msg.Should().Contain("line5_DIFFERENT");
    }

    [Fact]
    public void BuildContextMismatchMessage_MatchingLine_NoDiffMarker()
    {
        var hunk = new ApplyPatchLogic.PatchHunk
        {
            FilePath = "test.cs",
            StartLine = 1,
        };
        hunk.Lines.Add(" matching_line");
        hunk.Lines.Add("- mismatched_old");

        var fileLines = new List<string> { "matching_line", "mismatched_actual" };
        var msg = ApplyPatchLogic.BuildContextMismatchMessage("test.cs", hunk, fileLines, adjustedStart: 0);

        msg.Should().Contain("context mismatch");
    }
}

public sealed class NotebookCellDiagnosticTests
{
    [Fact]
    public void BuildCellNotFoundMessage_ListsAvailableCellIds()
    {
        var notebook = new NotebookDocument
        {
            Cells = new List<NotebookCell>
            {
                new() { Id = "cell-abc", CellType = "code" },
                new() { Id = null, CellType = "markdown" },
                new() { Id = "custom-id", CellType = "code" },
            }
        };

        var msg = NotebookToolHandlers.BuildCellNotFoundMessage(notebook, "nonexistent-id");

        msg.Should().Contain("not found");
        msg.Should().Contain("[诊断]");
        msg.Should().Contain("cell-abc");
        msg.Should().Contain("cell-1");
        msg.Should().Contain("custom-id");
        msg.Should().Contain("3 个 cell");
    }

    [Fact]
    public void BuildCellNotFoundMessage_TooManyCells_TruncatesList()
    {
        var cells = new List<NotebookCell>();
        for (int i = 0; i < 25; i++)
        {
            cells.Add(new NotebookCell { Id = $"cell-{i}", CellType = "code" });
        }

        var notebook = new NotebookDocument { Cells = cells };
        var msg = NotebookToolHandlers.BuildCellNotFoundMessage(notebook, "nonexistent");

        msg.Should().Contain("25 个 cell");
        msg.Should().Contain("还有 5 个 cell");
    }
}
