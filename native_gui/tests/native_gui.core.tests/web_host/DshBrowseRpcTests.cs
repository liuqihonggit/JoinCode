namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// C6 补齐的一元方法分发测试：session/canOpenWorkspacePath、session/initializeDefaultModel、
/// directoryPicker/list|createDirectory、fileReferences/list 的线上形状与真实数据源。
/// </summary>
public sealed class DshBrowseRpcTests {
    private static async Task<(DshHostServices Services, string SessionId)> CreateServicesAsync(InMemoryFileSystem fs) {
        var services = new DshHostServices(fs, homeDirectory: "/home/user");
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var created = await services.Sessions.CreateAsync(null, "/repo", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        return (services, (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId);
    }

    [Fact]
    public async Task CanOpenWorkspacePath_ReturnsTrueOnDesktopHost() {
        var (services, _) = await CreateServicesAsync(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("session/canOpenWorkspacePath", default);

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetBoolean().Should().Be(OperatingSystem.IsWindows(), "宿主仅在 Windows 上具备资源管理器打开能力");
    }

    [Fact]
    public async Task InitializeDefaultModel_IsNoOpVoid() {
        var (services, _) = await CreateServicesAsync(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("session/initializeDefaultModel", default);

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task DirectoryPickerList_ReturnsHomeListing() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/home/user/projects");
        var (services, _) = await CreateServicesAsync(fs);
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("directoryPicker/list", default);

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("path").GetString().Should().Be("/home/user");
        value.GetProperty("home").GetString().Should().Be("/home/user");
        value.GetProperty("entries")[0].GetProperty("name").GetString().Should().Be("projects");
        value.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task DirectoryPickerCreateDirectory_CreatesUnderGivenParent() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/home/user");
        var (services, _) = await CreateServicesAsync(fs);
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("directoryPicker/createDirectory",
            JsonSerializer.SerializeToElement(new { path = "/home/user", name = "created-by-rpc" }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetString().Should().Be("/home/user/created-by-rpc");
        fs.DirectoryExists("/home/user/created-by-rpc").Should().BeTrue();
    }

    [Fact]
    public async Task DirectoryPickerCreateDirectory_ReportsExistingWithGuidance() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/home/user/taken");
        var (services, _) = await CreateServicesAsync(fs);
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("directoryPicker/createDirectory",
            JsonSerializer.SerializeToElement(new { path = "/home/user", name = "taken" }));

        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new Xunit.Sdk.XunitException("期望 directoryPicker/createDirectory 失败，但实际成功");
        error.Code.Should().Be("directory-exists");
        error.Message.Should().Contain("已存在");
    }

    [Fact]
    public async Task FileReferencesList_UsesSessionRootAndQuery() {
        await using var fs = new InMemoryFileSystem();
        var (services, sessionId) = await CreateServicesAsync(fs);
        await fs.WriteAllTextAsync("/repo/src.cs", "x");
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("fileReferences/list",
            JsonSerializer.SerializeToElement(new { agentId = sessionId, query = "sr" }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var items = result.Value.GetValueOrDefault();
        items[0].GetProperty("path").GetString().Should().Be("src.cs");
        items[0].GetProperty("kind").GetString().Should().Be("file");
    }

    [Fact]
    public async Task FileReferencesList_UnknownAgentReturnsGuidance() {
        var (services, _) = await CreateServicesAsync(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("fileReferences/list",
            JsonSerializer.SerializeToElement(new { agentId = "missing", query = "" }));

        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new Xunit.Sdk.XunitException("期望 fileReferences/list 对未知会话失败，但实际成功");
        error.Code.Should().Be("session/not-found");
    }
}
