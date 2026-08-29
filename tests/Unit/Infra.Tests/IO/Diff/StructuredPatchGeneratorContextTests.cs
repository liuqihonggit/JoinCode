namespace JoinCode.Infra.Tests.IO.Diff;

public sealed class StructuredPatchGeneratorContextTests
{
    [Fact]
    public void Generate_DefaultContextLines_IsFour()
    {
        var oldLines = Enumerable.Range(1, 20).Select(i => $"line{i}").ToArray();
        var newLines = oldLines.Select(l => l == "line10" ? "line10-CHANGED" : l).ToArray();
        var oldContent = string.Join("\n", oldLines) + "\n";
        var newContent = string.Join("\n", newLines) + "\n";

        var hunks = StructuredPatchGenerator.Generate("test.cs", oldContent, newContent);

        hunks.Should().HaveCount(1);
        var hunk = hunks[0];
        var lines = hunk.Lines.ToList();

        var changeIndex = lines.FindIndex(l => l.Type == PatchLineType.Removed);
        changeIndex.Should().BeGreaterThan(0);

        var above = lines.Take(changeIndex).ToList();
        above.Should().HaveCount(4);
        above.Should().OnlyContain(l => l.Type == PatchLineType.Context);

        var below = lines.Skip(changeIndex + 2).ToList();
        below.Should().HaveCount(4);
        below.Should().OnlyContain(l => l.Type == PatchLineType.Context);
    }

    [Fact]
    public void Generate_ChangeAtEndOfFile_IsIncluded()
    {
        var oldLines = Enumerable.Range(1, 20).Select(i => $"line{i}").ToArray();
        var newLines = oldLines.Select(l => l == "line20" ? "line20-CHANGED" : l).ToArray();
        var oldContent = string.Join("\n", oldLines) + "\n";
        var newContent = string.Join("\n", newLines) + "\n";

        var hunks = StructuredPatchGenerator.Generate("test.cs", oldContent, newContent);

        hunks.Should().HaveCount(1);
        var hunk = hunks[0];
        hunk.Lines.Should().Contain(l => l.Type == PatchLineType.Removed && l.Content == "line20");
        hunk.Lines.Should().Contain(l => l.Type == PatchLineType.Added && l.Content == "line20-CHANGED");
        hunk.Lines.Should().Contain(l => l.Type == PatchLineType.Removed);
    }

    [Fact]
    public void Generate_ChangeBeyondFirstContext_IsIncluded()
    {
        var oldLines = Enumerable.Range(1, 30).Select(i => $"line{i}").ToArray();
        var newLines = oldLines.Select(l => l == "line18" ? "line18-CHANGED" : l).ToArray();
        var oldContent = string.Join("\n", oldLines) + "\n";
        var newContent = string.Join("\n", newLines) + "\n";

        var hunks = StructuredPatchGenerator.Generate("test.cs", oldContent, newContent);

        hunks.Should().HaveCount(1);
        var hunk = hunks[0];
        hunk.Lines.Should().Contain(l => l.Type == PatchLineType.Removed && l.Content == "line18");
        hunk.Lines.Should().Contain(l => l.Type == PatchLineType.Added && l.Content == "line18-CHANGED");
    }
}
