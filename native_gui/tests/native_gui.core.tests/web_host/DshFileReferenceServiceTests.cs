namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// fileReferences/list 测试（TASK066 C6）：会话根目录下的 @ 引用补全，
/// 语义对齐 dsh-file-reference-local（路径前缀直接列目录、裸词模糊排序、排除构建产物、最多 20 条）。
/// </summary>
public sealed class DshFileReferenceServiceTests {
    private static async Task<(DshFileReferenceService Refs, string SessionId)> CreateServiceAsync() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var created = await services.Sessions.CreateAsync(null, "/repo", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;

        fs.CreateDirectory("/repo/src/util");
        fs.CreateDirectory("/repo/docs");
        fs.CreateDirectory("/repo/dist");
        fs.CreateDirectory("/repo/.hidden");
        await fs.WriteAllTextAsync("/repo/src/main.cs", "x");
        await fs.WriteAllTextAsync("/repo/src/util/helper.cs", "x");
        await fs.WriteAllTextAsync("/repo/docs/guide.md", "x");
        await fs.WriteAllTextAsync("/repo/dist/bundle.js", "x");
        await fs.WriteAllTextAsync("/repo/.hidden/secret.cs", "x");
        await fs.WriteAllTextAsync("/repo/README.md", "x");

        return (new DshFileReferenceService(services.Sessions, fs), sessionId);
    }

    [Fact]
    public async Task List_EmptyQuery_ListsRootDirectoryFirst() {
        var (refs, sessionId) = await CreateServiceAsync();

        var items = await refs.ListAsync(sessionId, "", CancellationToken.None);

        items.Select(item => item.Path).Should().Equal("docs", "src", "README.md");
        items[0].Kind.Should().Be("directory");
        items[2].Kind.Should().Be("file");
    }

    [Fact]
    public async Task List_PathQuery_ListsDirectoryChildren() {
        var (refs, sessionId) = await CreateServiceAsync();

        var items = await refs.ListAsync(sessionId, "src/", CancellationToken.None);

        items.Select(item => item.Path).Should().Equal("src/util", "src/main.cs");
        items[0].Kind.Should().Be("directory");
        items[1].Kind.Should().Be("file");
    }

    [Fact]
    public async Task List_BareQuery_RanksNamePrefixOverPathMatch() {
        var (refs, sessionId) = await CreateServiceAsync();

        var items = await refs.ListAsync(sessionId, "main", CancellationToken.None);

        items.Should().NotBeEmpty();
        items[0].Path.Should().Be("src/main.cs");
    }

    [Fact]
    public async Task List_HidesDotEntriesAndBuildDirectories() {
        var (refs, sessionId) = await CreateServiceAsync();

        var secret = await refs.ListAsync(sessionId, "secret", CancellationToken.None);
        var bundle = await refs.ListAsync(sessionId, "bundle", CancellationToken.None);
        var hiddenExplicit = await refs.ListAsync(sessionId, ".hidden/", CancellationToken.None);

        secret.Should().BeEmpty("隐藏目录内的文件默认不参与 @ 补全");
        bundle.Should().BeEmpty("dist 属构建产物，默认排除");
        hiddenExplicit.Select(item => item.Path).Should().Contain(".hidden/secret.cs");
    }

    [Fact]
    public async Task List_RejectsTraversalOutsideRoot() {
        var (refs, sessionId) = await CreateServiceAsync();

        var items = await refs.ListAsync(sessionId, "../", CancellationToken.None);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task List_UnknownSession_ThrowsWithGuidance() {
        var (refs, _) = await CreateServiceAsync();

        var error = await Assert.ThrowsAsync<DshFileReferenceException>(
            () => refs.ListAsync("missing-session", "", CancellationToken.None));

        error.Code.Should().Be("session/not-found");
        error.Message.Should().Contain("fileReferences/list");
    }
}
