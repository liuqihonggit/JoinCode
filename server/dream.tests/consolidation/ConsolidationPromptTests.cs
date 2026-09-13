
namespace Dream.Tests;

/// <summary>
/// 提示词构建器测试
/// </summary>
public sealed class ConsolidationPromptTests
{
    [Fact]
    public void BuildPrompt_ShouldContainMemoryRoot()
    {
        // Arrange
        const string memoryRoot = "/path/to/memory";
        const string transcriptDir = "/path/to/transcripts";

        // Act
        var prompt = ConsolidationPrompt.BuildPrompt(memoryRoot, transcriptDir);

        // Assert
        Assert.Contains(memoryRoot, prompt);
    }

    [Fact]
    public void BuildPrompt_ShouldContainTranscriptDir()
    {
        // Arrange
        const string memoryRoot = "/path/to/memory";
        const string transcriptDir = "/path/to/transcripts";

        // Act
        var prompt = ConsolidationPrompt.BuildPrompt(memoryRoot, transcriptDir);

        // Assert
        Assert.Contains(transcriptDir, prompt);
    }

    [Fact]
    public void BuildPrompt_ShouldContainAllFourPhases()
    {
        // Arrange
        const string memoryRoot = "/path/to/memory";
        const string transcriptDir = "/path/to/transcripts";

        // Act
        var prompt = ConsolidationPrompt.BuildPrompt(memoryRoot, transcriptDir);

        // Assert
        Assert.Contains("Phase 1", prompt);
        Assert.Contains("Phase 2", prompt);
        Assert.Contains("Phase 3", prompt);
        Assert.Contains("Phase 4", prompt);
        Assert.Contains("Orient", prompt);
        Assert.Contains("Gather", prompt);
        Assert.Contains("Consolidate", prompt);
        Assert.Contains("Prune", prompt);
    }

    [Fact]
    public void BuildPrompt_ShouldContainEntrypointName()
    {
        // Arrange
        const string memoryRoot = "/path/to/memory";
        const string transcriptDir = "/path/to/transcripts";

        // Act
        var prompt = ConsolidationPrompt.BuildPrompt(memoryRoot, transcriptDir);

        // Assert
        Assert.Contains(ConsolidationPrompt.EntrypointName, prompt);
    }

    [Fact]
    public void BuildPrompt_WithExtraContext_ShouldContainExtra()
    {
        // Arrange
        const string memoryRoot = "/path/to/memory";
        const string transcriptDir = "/path/to/transcripts";
        const string extra = "Additional context here";

        // Act
        var prompt = ConsolidationPrompt.BuildPrompt(memoryRoot, transcriptDir, extra);

        // Assert
        Assert.Contains("Additional context", prompt);
        Assert.Contains(extra, prompt);
    }

    [Fact]
    public void BuildPrompt_WithoutExtraContext_ShouldNotContainAdditionalContextSection()
    {
        // Arrange
        const string memoryRoot = "/path/to/memory";
        const string transcriptDir = "/path/to/transcripts";

        // Act
        var prompt = ConsolidationPrompt.BuildPrompt(memoryRoot, transcriptDir);

        // Assert
        Assert.DoesNotContain("## Additional context", prompt);
    }

    [Fact]
    public void BuildExtraContext_ShouldContainSessionCount()
    {
        // Arrange
        var sessionIds = new[] { "session1", "session2", "session3" };
        const string toolConstraints = "Tool constraints";

        // Act
        var extra = ConsolidationPrompt.BuildExtraContext(sessionIds, toolConstraints);

        // Assert
        Assert.Contains("3", extra);
        Assert.Contains("session1", extra);
        Assert.Contains("session2", extra);
        Assert.Contains("session3", extra);
    }

    [Fact]
    public void BuildExtraContext_WithEmptySessions_ShouldShowNone()
    {
        // Arrange
        var sessionIds = Array.Empty<string>();
        const string toolConstraints = "Tool constraints";

        // Act
        var extra = ConsolidationPrompt.BuildExtraContext(sessionIds, toolConstraints);

        // Assert
        Assert.Contains("(none)", extra);
    }

    [Fact]
    public void BuildExtraContext_ShouldContainToolConstraints()
    {
        // Arrange
        var sessionIds = new[] { "session1" };
        const string toolConstraints = "Tool constraints here";

        // Act
        var extra = ConsolidationPrompt.BuildExtraContext(sessionIds, toolConstraints);

        // Assert
        Assert.Contains(toolConstraints, extra);
    }

    [Fact]
    public void ToolConstraints_ShouldContainReadOnlyCommands()
    {
        // Arrange & Act
        var constraints = ConsolidationPrompt.ToolConstraints;

        // Assert
        Assert.Contains("ls", constraints);
        Assert.Contains("find", constraints);
        Assert.Contains("grep", constraints);
        Assert.Contains("cat", constraints);
        Assert.Contains("read-only", constraints);
    }

    [Fact]
    public void BuildPrompt_ShouldContainMaxEntrypointLines()
    {
        // Arrange
        const string memoryRoot = "/path/to/memory";
        const string transcriptDir = "/path/to/transcripts";

        // Act
        var prompt = ConsolidationPrompt.BuildPrompt(memoryRoot, transcriptDir);

        // Assert
        Assert.Contains(ConsolidationPrompt.MaxEntrypointLines.ToString(), prompt);
    }
}
