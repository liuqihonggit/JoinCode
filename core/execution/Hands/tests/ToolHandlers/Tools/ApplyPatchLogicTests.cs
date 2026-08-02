namespace Core.Tests;

public sealed class ApplyPatchLogicTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly ApplyPatchLogic _logic;
    private const string WorkingDir = "/test";

    public ApplyPatchLogicTests()
    {
        _logic = new ApplyPatchLogic(_fs);
    }

    [Fact]
    public async Task ApplyAsync_SingleHunk_AddsLine()
    {
        var filePath = CreateFile("line1\nline2\nline3");
        var fileName = Path.GetFileName(filePath);

        var patch = $"""
            --- a/{fileName}
            +++ b/{fileName}
            @@ -1,3 +1,4 @@
             line1
            +inserted
             line2
             line3
            """;

        var result = await _logic.ApplyAsync(patch, dryRun: false, workingDirectory: WorkingDir, cancellationToken: CancellationToken.None);

        Assert.True(result.Success, $"Expected success but got: {result.ErrorMessage}, Details: {string.Join("; ", result.Details)}");
        Assert.Equal(1, result.FilesModified);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("inserted", content);
    }

    [Fact]
    public async Task ApplyAsync_SingleHunk_RemovesLine()
    {
        var filePath = CreateFile("line1\nremove-me\nline3");
        var fileName = Path.GetFileName(filePath);

        var patch = $"""
            --- a/{fileName}
            +++ b/{fileName}
            @@ -1,3 +1,2 @@
             line1
            -remove-me
             line3
            """;

        var result = await _logic.ApplyAsync(patch, dryRun: false, workingDirectory: WorkingDir, cancellationToken: CancellationToken.None);

        Assert.True(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.DoesNotContain("remove-me", content);
    }

    [Fact]
    public async Task ApplyAsync_SingleHunk_ReplacesLine()
    {
        var filePath = CreateFile("old-content\nline2");
        var fileName = Path.GetFileName(filePath);

        var patch = $"""
            --- a/{fileName}
            +++ b/{fileName}
            @@ -1,2 +1,2 @@
            -old-content
            +new-content
             line2
            """;

        var result = await _logic.ApplyAsync(patch, dryRun: false, workingDirectory: WorkingDir, cancellationToken: CancellationToken.None);

        Assert.True(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("new-content", content);
        Assert.DoesNotContain("old-content", content);
    }

    [Fact]
    public async Task ApplyAsync_ContextMismatch_FailsPerFile()
    {
        var filePath = CreateFile("different-content\nline2");
        var fileName = Path.GetFileName(filePath);

        var patch = $"""
            --- a/{fileName}
            +++ b/{fileName}
            @@ -1,2 +1,2 @@
            -old-content
            +new-content
             line2
            """;

        var result = await _logic.ApplyAsync(patch, dryRun: false, workingDirectory: WorkingDir, cancellationToken: CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, result.FilesFailed);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("different-content", content);
    }

    [Fact]
    public async Task ApplyAsync_DryRun_DoesNotModifyFile()
    {
        var filePath = CreateFile("line1\nline2");
        var fileName = Path.GetFileName(filePath);

        var patch = $"""
            --- a/{fileName}
            +++ b/{fileName}
            @@ -1,2 +1,2 @@
            -line1
            +replaced
             line2
            """;

        var result = await _logic.ApplyAsync(patch, dryRun: true, workingDirectory: WorkingDir, cancellationToken: CancellationToken.None);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.FilesWouldModify);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("line1", content);
        Assert.DoesNotContain("replaced", content);
    }

    [Fact]
    public async Task ApplyAsync_MultipleHunksSameFile_AllApplied()
    {
        var filePath = CreateFile("aaa\nbbb\nccc\nddd\neee");
        var fileName = Path.GetFileName(filePath);

        var patch = $"""
            --- a/{fileName}
            +++ b/{fileName}
            @@ -1,3 +1,3 @@
            -aaa
            +AAA
             bbb
             ccc
            @@ -4,2 +4,2 @@
            -ddd
            +DDD
             eee
            """;

        var result = await _logic.ApplyAsync(patch, dryRun: false, workingDirectory: WorkingDir, cancellationToken: CancellationToken.None);

        Assert.True(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("AAA", content);
        Assert.Contains("DDD", content);
    }

    [Fact]
    public async Task ApplyAsync_SecondHunkFails_FileUnchanged()
    {
        var filePath = CreateFile("aaa\nbbb\nccc\nddd\neee");
        var fileName = Path.GetFileName(filePath);

        var patch = $"""
            --- a/{fileName}
            +++ b/{fileName}
            @@ -1,3 +1,3 @@
            -aaa
            +AAA
             bbb
             ccc
            @@ -4,2 +4,2 @@
            -wrong
            +DDD
             eee
            """;

        var result = await _logic.ApplyAsync(patch, dryRun: false, workingDirectory: WorkingDir, cancellationToken: CancellationToken.None);

        Assert.False(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("aaa", content);
        Assert.DoesNotContain("AAA", content);
    }

    [Fact]
    public async Task ApplyAsync_MultipleFiles_AllApplied()
    {
        var file1 = CreateFile("content1");
        var file2 = CreateFile("content2");
        var name1 = Path.GetFileName(file1);
        var name2 = Path.GetFileName(file2);

        var patch = $"""
            --- a/{name1}
            +++ b/{name1}
            @@ -1,1 +1,1 @@
            -content1
            +CONTENT1
            --- a/{name2}
            +++ b/{name2}
            @@ -1,1 +1,1 @@
            -content2
            +CONTENT2
            """;

        var result = await _logic.ApplyAsync(patch, dryRun: false, workingDirectory: WorkingDir, cancellationToken: CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.FilesModified);
    }

    [Fact]
    public async Task ApplyAsync_FileNotFound_Fails()
    {
        var patch = """
            --- a/nonexistent.txt
            +++ b/nonexistent.txt
            @@ -1,1 +1,1 @@
            -old
            +new
            """;

        var result = await _logic.ApplyAsync(patch, dryRun: false, workingDirectory: WorkingDir, cancellationToken: CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, result.FilesFailed);
    }

    [Fact]
    public async Task ApplyAsync_EmptyPatch_Fails()
    {
        var result = await _logic.ApplyAsync("", dryRun: false, cancellationToken: CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ApplyAsync_NoValidHunks_Fails()
    {
        var result = await _logic.ApplyAsync("some random text\nno patch here", dryRun: false, cancellationToken: CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ApplyAsync_NullPatch_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _logic.ApplyAsync(null!, dryRun: false, cancellationToken: CancellationToken.None));
    }

    private string CreateFile(string content)
    {
        var filePath = $"/test/test_{Guid.NewGuid():N}.txt";
        _fs.WriteAllText(filePath, content);
        return filePath;
    }
}
