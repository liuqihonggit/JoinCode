namespace Core.Tests.Agents.Doctor;

using JoinCode.Abstractions.Interfaces.Doctor;

public class FileBasedReflexionMemoryTests
{
    [Fact]
    public async Task StoreAsync_CreatesFileInRuleDirectory()
    {
        var fs = new InMemoryFileSystem();
        var baseDir = "/test/reflexion";
        fs.CreateDirectory(baseDir);
        var memory = new FileBasedReflexionMemory(fs, baseDir);

        var patch = CreatePatch();
        var diagnostic = CreateDiagnostic();

        await memory.StoreAsync(patch, diagnostic, wasSuccessful: true);

        var files = fs.EnumerateFiles(Path.Combine(baseDir, "LoopDetected"), "*.json", SearchOption.TopDirectoryOnly);
        Assert.Single(files);
    }

    [Fact]
    public async Task RetrieveSimilarPatchesAsync_NoHistory_ReturnsEmpty()
    {
        var fs = new InMemoryFileSystem();
        var baseDir = "/test/reflexion";
        fs.CreateDirectory(baseDir);
        var memory = new FileBasedReflexionMemory(fs, baseDir);

        var diagnostic = CreateDiagnostic();
        var result = await memory.RetrieveSimilarPatchesAsync(diagnostic);

        Assert.Empty(result);
    }

    [Fact]
    public async Task RetrieveSimilarPatchesAsync_WithHistory_ReturnsSuccessfulPatches()
    {
        var fs = new InMemoryFileSystem();
        var baseDir = "/test/reflexion";
        fs.CreateDirectory(baseDir);
        var memory = new FileBasedReflexionMemory(fs, baseDir);

        var patch = CreatePatch();
        var diagnostic = CreateDiagnostic();

        await memory.StoreAsync(patch, diagnostic, wasSuccessful: true);

        var result = await memory.RetrieveSimilarPatchesAsync(diagnostic);

        Assert.Single(result);
        Assert.Equal(patch.TargetFilePath, result[0].TargetFilePath);
    }

    [Fact]
    public async Task RetrieveSimilarPatchesAsync_FailedPatchNotReturned()
    {
        var fs = new InMemoryFileSystem();
        var baseDir = "/test/reflexion";
        fs.CreateDirectory(baseDir);
        var memory = new FileBasedReflexionMemory(fs, baseDir);

        var patch = CreatePatch();
        var diagnostic = CreateDiagnostic();

        await memory.StoreAsync(patch, diagnostic, wasSuccessful: false);

        var result = await memory.RetrieveSimilarPatchesAsync(diagnostic);

        Assert.Empty(result);
    }

    [Fact]
    public async Task RetrieveSimilarPatchesAsync_RespectsMaxResults()
    {
        var fs = new InMemoryFileSystem();
        var baseDir = "/test/reflexion";
        fs.CreateDirectory(baseDir);
        var memory = new FileBasedReflexionMemory(fs, baseDir);

        var diagnostic = CreateDiagnostic();

        for (var i = 0; i < 5; i++)
        {
            var patch = CreatePatch(filePath: $"/src/File{i}.cs", description: $"Fix {i}");
            await memory.StoreAsync(patch, diagnostic, wasSuccessful: true);
            await Task.Delay(50);
        }

        var result = await memory.RetrieveSimilarPatchesAsync(diagnostic, maxResults: 2);

        Assert.True(result.Count <= 2);
        Assert.True(result.Count >= 1);
    }

    private static CodePatch CreatePatch(string filePath = "/src/Foo.cs", string description = "Fix loop detection")
    {
        return new CodePatch
        {
            TargetFilePath = filePath,
            PatchedContent = "class Fixed { }",
            Description = description,
            Confidence = 0.8
        };
    }

    private static DiagnosticReport CreateDiagnostic()
    {
        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.LoopDetected,
            Severity = DiagnosticSeverity.Warning,
            Description = "检测到循环 3 次"
        };
    }
}
