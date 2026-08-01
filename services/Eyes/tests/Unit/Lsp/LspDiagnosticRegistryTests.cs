namespace Eyes.Tests;

public class LspDiagnosticRegistryTests
{
    private readonly LspDiagnosticRegistry _registry;
    private readonly Mock<IClockService> _clock;

    public LspDiagnosticRegistryTests()
    {
        _clock = new Mock<IClockService>();
        _clock.Setup(c => c.GetUtcNowOffset()).Returns(DateTimeOffset.UtcNow);
        _registry = new LspDiagnosticRegistry(_clock.Object);
    }

    [Fact]
    public void RegisterPending_EmptyFiles_DoesNothing()
    {
        _registry.RegisterPending("server", []);
        _registry.PendingCount.Should().Be(0);
    }

    [Fact]
    public void RegisterPending_WithFiles_IncrementsPendingCount()
    {
        var files = CreateFiles("file:///a.cs", "error in a");
        _registry.RegisterPending("server", files);
        _registry.PendingCount.Should().Be(1);
    }

    [Fact]
    public void CheckPending_NoPending_ReturnsEmpty()
    {
        var result = _registry.CheckPending();
        result.Should().BeEmpty();
    }

    [Fact]
    public void CheckPending_WithPending_ReturnsDiagnostics()
    {
        var files = CreateFiles("file:///a.cs", "error in a");
        _registry.RegisterPending("server", files);

        var result = _registry.CheckPending();
        result.Should().HaveCount(1);
        result[0].ServerName.Should().Be("server");
    }

    [Fact]
    public void CheckPending_AfterCheck_PendingCleared()
    {
        var files = CreateFiles("file:///a.cs", "error in a");
        _registry.RegisterPending("server", files);

        _registry.CheckPending();
        _registry.PendingCount.Should().Be(0);
    }

    [Fact]
    public void CheckPending_DuplicateDiagnostic_Deduplicates()
    {
        var files1 = CreateFiles("file:///a.cs", "same error");
        var files2 = CreateFiles("file:///a.cs", "same error");
        _registry.RegisterPending("server1", files1);
        _registry.RegisterPending("server2", files2);

        var result = _registry.CheckPending();
        result.Should().HaveCount(1);
        result[0].Files[0].Diagnostics.Should().HaveCount(1);
    }

    [Fact]
    public void CheckPending_DifferentDiagnostics_KeepsBoth()
    {
        var files1 = CreateFiles("file:///a.cs", "error 1");
        var files2 = CreateFiles("file:///a.cs", "error 2");
        _registry.RegisterPending("server1", files1);
        _registry.RegisterPending("server2", files2);

        var result = _registry.CheckPending();
        result.Should().HaveCount(1);
        result[0].Files[0].Diagnostics.Should().HaveCount(2);
    }

    [Fact]
    public void ClearAll_ClearsPending()
    {
        var files = CreateFiles("file:///a.cs", "error");
        _registry.RegisterPending("server", files);

        _registry.ClearAll();
        _registry.PendingCount.Should().Be(0);
    }

    [Fact]
    public void ResetAll_ClearsPendingAndDelivered()
    {
        var files = CreateFiles("file:///a.cs", "error");
        _registry.RegisterPending("server", files);
        _registry.CheckPending();

        _registry.ResetAll();

        _registry.PendingCount.Should().Be(0);
        _registry.RegisterPending("server", files);
        var result = _registry.CheckPending();
        result.Should().HaveCount(1);
    }

    [Fact]
    public void ClearDeliveredForFile_RemovesDeliveredTracking()
    {
        var files = CreateFiles("file:///a.cs", "error");
        _registry.RegisterPending("server", files);
        _registry.CheckPending();

        _registry.ClearDeliveredForFile("file:///a.cs");

        _registry.RegisterPending("server", files);
        var result = _registry.CheckPending();
        result.Should().HaveCount(1);
    }

    [Fact]
    public void CheckPending_MultipleServers_CombinesServerNames()
    {
        var files1 = CreateFiles("file:///a.cs", "error 1");
        var files2 = CreateFiles("file:///b.cs", "error 2");
        _registry.RegisterPending("server1", files1);
        _registry.RegisterPending("server2", files2);

        var result = _registry.CheckPending();
        result.Should().HaveCount(1);
        result[0].ServerName.Should().Contain("server1");
        result[0].ServerName.Should().Contain("server2");
    }

    [Fact]
    public void CheckPending_VolumeLimits_TruncatesPerFile()
    {
        var diagnostics = Enumerable.Range(0, 15)
            .Select(i => new LspDiagnosticItem { Message = $"error {i}", Severity = "Error" })
            .ToList();
        var files = new List<LspDiagnosticFile>
        {
            new() { Uri = "file:///a.cs", Diagnostics = diagnostics }
        };

        _registry.RegisterPending("server", files);
        var result = _registry.CheckPending();
        result[0].Files[0].Diagnostics.Should().HaveCount(10);
    }

    [Fact]
    public void CheckPending_SeveritySorting_ErrorsFirst()
    {
        var diagnostics = new List<LspDiagnosticItem>
        {
            new() { Message = "hint", Severity = "Hint" },
            new() { Message = "error", Severity = "Error" },
            new() { Message = "warning", Severity = "Warning" }
        };
        var files = new List<LspDiagnosticFile>
        {
            new() { Uri = "file:///a.cs", Diagnostics = diagnostics }
        };

        _registry.RegisterPending("server", files);
        var result = _registry.CheckPending();
        result[0].Files[0].Diagnostics[0].Message.Should().Be("error");
        result[0].Files[0].Diagnostics[1].Message.Should().Be("warning");
        result[0].Files[0].Diagnostics[2].Message.Should().Be("hint");
    }

    [Fact]
    public void PendingCount_MultipleRegistrations_ReturnsCorrectCount()
    {
        var files1 = CreateFiles("file:///a.cs", "error 1");
        var files2 = CreateFiles("file:///b.cs", "error 2");
        _registry.RegisterPending("server", files1);
        _registry.RegisterPending("server", files2);

        _registry.PendingCount.Should().Be(2);
    }

    private static List<LspDiagnosticFile> CreateFiles(string uri, string message)
    {
        return
        [
            new LspDiagnosticFile
            {
                Uri = uri,
                Diagnostics =
                [
                    new LspDiagnosticItem
                    {
                        Message = message,
                        Severity = "Error",
                        Range = new LspRange()
                    }
                ]
            }
        ];
    }
}
