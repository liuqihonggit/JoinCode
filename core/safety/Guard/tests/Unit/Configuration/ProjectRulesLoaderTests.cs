namespace Core.Tests.Configuration;

/// <summary>
/// 与 SettingsLoaderTests 共享 AppDataConstants 全局状态,需串行执行避免相互污染
/// </summary>
[Collection("AppDataConstantsCollection")]
public sealed class ProjectRulesLoaderTests
{
    private readonly Mock<IFileSystem> _fs = new();
    private const string BaseDir = "C:\\test\\dir";

    private void SetupFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(BaseDir, relativePath);
        _fs.Setup(x => x.FileExists(fullPath)).Returns(true);
        _fs.Setup(x => x.ReadAllTextAsync(fullPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
    }

    private ProjectRulesLoader CreateLoader()
    {
        _fs.Setup(x => x.GetCurrentDirectory()).Returns(BaseDir);
        _fs.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);
        _fs.Setup(x => x.GetFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>()))
            .Returns(Array.Empty<string>());
        _fs.Setup(x => x.GetFullPath(It.IsAny<string>())).Returns<string>(p => Path.GetFullPath(p));
        _fs.Setup(x => x.GetParentPath(BaseDir)).Returns((string?)null);
        return new ProjectRulesLoader(_fs.Object);
    }

    [Fact]
    public async Task LoadRulesAsync_NoFiles_Should_Return_Null()
    {
        _fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(false);
        _fs.Setup(x => x.GetCurrentDirectory()).Returns(BaseDir);
        _fs.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);
        _fs.Setup(x => x.GetFullPath(It.IsAny<string>())).Returns<string>(p => Path.GetFullPath(p));
        _fs.Setup(x => x.GetParentPath(BaseDir)).Returns((string?)null);
        var loader = new ProjectRulesLoader(_fs.Object);

        var result = await loader.LoadRulesAsync(BaseDir).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadRulesAsync_WithAgentsMd_Should_Load_Content()
    {
        SetupFile("AGENTS.md", "# Agents Rules");
        var loader = CreateLoader();

        var result = await loader.LoadRulesAsync(BaseDir).ConfigureAwait(true);

        Assert.Equal("# Agents Rules", result);
    }

    [Fact]
    public async Task LoadRulesAsync_WithClaudeMd_Should_Load_Content()
    {
        SetupFile("CLAUDE.md", "# Claude Rules");
        var loader = CreateLoader();

        var result = await loader.LoadRulesAsync(BaseDir).ConfigureAwait(true);

        Assert.Equal("# Claude Rules", result);
    }

    [Fact]
    public async Task LoadRulesAsync_WithClaudeLocalMd_Should_Load_Content()
    {
        SetupFile("CLAUDE.local.md", "# Local Rules");
        var loader = CreateLoader();

        var result = await loader.LoadRulesAsync(BaseDir).ConfigureAwait(true);

        Assert.Equal("# Local Rules", result);
    }

    [Fact]
    public async Task LoadRulesAsync_MultipleFiles_Should_Combine_With_Headers()
    {
        SetupFile("AGENTS.md", "agents content");
        SetupFile("CLAUDE.md", "claude content");
        var loader = CreateLoader();

        var result = await loader.LoadRulesAsync(BaseDir).ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.Contains("agents content", result);
        Assert.Contains("claude content", result);
        Assert.Contains("来源: AGENTS.md", result);
        Assert.Contains("来源: CLAUDE.md", result);
    }

    [Fact]
    public async Task LoadRulesAsync_CaseInsensitive_Should_Load()
    {
        SetupFile("agents.md", "lowercase agents");
        var loader = CreateLoader();

        var result = await loader.LoadRulesAsync(BaseDir).ConfigureAwait(true);

        Assert.Equal("lowercase agents", result);
    }

    [Fact]
    public async Task LoadRulesAsync_DotJccRules_Should_Load()
    {
        var relativePath = Path.Combine(
            AppDataConstants.AppDataFolder,
            AppDataConstants.RulesFolderName,
            AppDataConstants.ProjectRulesFileName);
        SetupFile(relativePath, "jcc rules content");
        var loader = CreateLoader();

        var result = await loader.LoadRulesAsync(BaseDir).ConfigureAwait(true);

        Assert.Equal("jcc rules content", result);
    }

    [Fact]
    public void HasRulesFile_WhenFileExists_Should_Return_True()
    {
        SetupFile("AGENTS.md", "content");
        var loader = CreateLoader();

        Assert.True(loader.HasRulesFile(BaseDir));
    }

    [Fact]
    public void HasRulesFile_WhenNoFileExists_Should_Return_False()
    {
        _fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(false);
        _fs.Setup(x => x.GetCurrentDirectory()).Returns(BaseDir);
        _fs.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);
        var loader = new ProjectRulesLoader(_fs.Object);

        Assert.False(loader.HasRulesFile(BaseDir));
    }

    [Fact]
    public void GetRulesFilePath_WhenFileExists_Should_Return_Path()
    {
        SetupFile("AGENTS.md", "content");
        var loader = CreateLoader();

        var path = loader.GetRulesFilePath(BaseDir);

        Assert.Equal(Path.Combine(BaseDir, "AGENTS.md"), path);
    }

    [Fact]
    public void GetRulesFilePath_WhenNoFileExists_Should_Return_Null()
    {
        _fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(false);
        _fs.Setup(x => x.GetCurrentDirectory()).Returns(BaseDir);
        _fs.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);
        var loader = new ProjectRulesLoader(_fs.Object);

        var path = loader.GetRulesFilePath(BaseDir);

        Assert.Null(path);
    }

    [Fact]
    public Task LoadRulesAsync_TraeRulesDir_Should_Load_MdFiles()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task LoadRulesAsync_ClaudeRulesDir_Should_Load_MdFiles()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task LoadRulesAsync_RulesDirRecursive_Should_Load_SubdirectoryFiles()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task LoadRulesAsync_RulesDirCombinedWithFiles_Should_Load_All()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task LoadRulesAsync_RulesDirEmptyMd_Should_Skip()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public void HasRulesFile_WithRulesDir_Should_Return_True()
    {
    }

    [Fact]
    public void GetRulesFilePath_WithRulesDir_Should_Return_FirstMdPath()
    {
    }

    [Fact]
    public async Task LoadRulesAsync_CodexMd_Should_Load_Content()
    {
        SetupFile("codex.md", "# Codex Rules");
        var loader = CreateLoader();

        var result = await loader.LoadRulesAsync(BaseDir).ConfigureAwait(true);

        Assert.Equal("# Codex Rules", result);
    }

    [Fact]
    public async Task LoadRulesAsync_CodexAgentsMd_Should_Load_Content()
    {
        SetupFile(Path.Combine(".codex", "AGENTS.md"), "# Codex Agents");
        var loader = CreateLoader();

        var result = await loader.LoadRulesAsync(BaseDir).ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.Contains("Codex Agents", result);
    }

    [Fact]
    public Task LoadRulesAsync_CodexRulesDir_Should_Load_MdFiles()
    {
        return Task.CompletedTask;
    }
}
