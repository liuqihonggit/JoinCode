namespace Services.StepEvidence.Tests;

public sealed class CompleteStepToolHandlersTests
{
    private readonly CompleteStepToolHandlers _handler = new();

    [Fact]
    public async Task CompleteStep_WithValidEvidence_ReturnsSuccess()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "verification", Summary: "All tests passed", Command: "dotnet test"),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Implement feature X",
            result: "Feature X is now implemented and tested",
            evidence: evidence).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("signed off", result.GetTextContent());
        Assert.Contains("1 evidence item(s)", result.GetTextContent());
        Assert.Contains("verification", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_WithMultipleEvidence_ReturnsAllKinds()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "verification", Summary: "Tests pass", Command: "dotnet test"),
            new(Kind: "diff", Summary: "Added new method", Paths: ["src/Program.cs"]),
            new(Kind: "manual", Summary: "Manually verified output"),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Add feature",
            result: "Feature added",
            evidence: evidence).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("3 evidence item(s)", result.GetTextContent());
        Assert.Contains("verification, diff, manual", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_WithNotes_IncludesNotes()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "manual", Summary: "Checked output"),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Review code",
            result: "Code reviewed",
            evidence: evidence,
            notes: "Need follow-up on edge cases").ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("Need follow-up on edge cases", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_EmptyStep_ReturnsError()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "manual", Summary: "Done"),
        };

        var result = await _handler.CompleteStepAsync(
            step: "",
            result: "Something",
            evidence: evidence).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("step is required", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_EmptyResult_ReturnsError()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "manual", Summary: "Done"),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Step 1",
            result: "   ",
            evidence: evidence).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("result is required", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_NoEvidence_ReturnsError()
    {
        var result = await _handler.CompleteStepAsync(
            step: "Step 1",
            result: "Done",
            evidence: null).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("At least one evidence item is required", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_EmptyEvidenceList_ReturnsError()
    {
        var result = await _handler.CompleteStepAsync(
            step: "Step 1",
            result: "Done",
            evidence: []).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("At least one evidence item is required", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_InvalidKind_ReturnsError()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "invalid_kind", Summary: "Something"),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Step 1",
            result: "Done",
            evidence: evidence).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("invalid kind", result.GetTextContent());
        Assert.Contains("verification|diff|files|manual", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_EmptySummary_ReturnsError()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "manual", Summary: ""),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Step 1",
            result: "Done",
            evidence: evidence).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("summary is required", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_VerificationWithoutCommand_ReturnsError()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "verification", Summary: "Tests passed", Command: null),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Step 1",
            result: "Done",
            evidence: evidence).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("verification command is required", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_DiffWithoutPaths_ReturnsError()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "diff", Summary: "Changed code", Paths: null),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Step 1",
            result: "Done",
            evidence: evidence).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("diff evidence requires paths", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_FilesWithoutPaths_ReturnsError()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "files", Summary: "Created files", Paths: []),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Step 1",
            result: "Done",
            evidence: evidence).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("files evidence requires paths", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_DiffWithPaths_ReturnsSuccess()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "diff", Summary: "Modified method", Paths: ["src/Foo.cs", "src/Bar.cs"]),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Refactor code",
            result: "Code refactored",
            evidence: evidence).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("diff", result.GetTextContent());
    }

    [Fact]
    public async Task CompleteStep_FilesWithPaths_ReturnsSuccess()
    {
        var evidence = new List<StepEvidenceInput>
        {
            new(Kind: "files", Summary: "Created new files", Paths: ["src/NewFile.cs"]),
        };

        var result = await _handler.CompleteStepAsync(
            step: "Add new module",
            result: "Module added",
            evidence: evidence).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("files", result.GetTextContent());
    }
}
