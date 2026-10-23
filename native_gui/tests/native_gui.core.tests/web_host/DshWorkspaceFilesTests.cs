namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// workspaceFiles 文件树测试（TASK058 P2-B1）：list/read/readBytes/stat 四一元方法，
/// 根目录 = 会话 cwd（SessionInfo.ProjectPath），路径限制在 cwd 内防穿越，version 用 size-mtime 合成。
/// </summary>
public sealed class DshWorkspaceFilesTests {
    /// <summary>构造带文件树的会话（cwd 下建子目录与文本文件）</summary>
    private static async Task<(DshHostServices Services, string SessionId, string Cwd)> CreateSessionWithFilesAsync() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        await fs.WriteAllTextAsync("/test/proj/readme.md", "# hello\nline2\nline3\n");
        await fs.WriteAllTextAsync("/test/proj/app/main.cs", "Console.WriteLine(1);\n");
        fs.CreateDirectory("/test/proj/docs");
        return (services, sessionId, "/test/proj");
    }

    private static JsonElement FilesArgs(string sessionId, string path) => JsonSerializer.SerializeToElement(new {
        workspaceFileScopeId = sessionId,
        path,
    });

    [Fact]
    public async Task List_ReturnsEntriesSortedWithTypes() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/list", FilesArgs(sessionId, ""));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("path").GetString().Should().Be("");
        var entries = value.GetProperty("entries");
        entries.GetArrayLength().Should().Be(3);
        entries[0].GetProperty("name").GetString().Should().Be("app");
        entries[0].GetProperty("type").GetString().Should().Be("directory");
        entries[1].GetProperty("name").GetString().Should().Be("docs");
        entries[2].GetProperty("name").GetString().Should().Be("readme.md");
        entries[2].GetProperty("type").GetString().Should().Be("file");
        value.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Read_ReturnsTextWithOffsetAndEof() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/read", FilesArgs(sessionId, "readme.md"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("text").GetString().Should().Contain("# hello");
        value.GetProperty("lines").GetInt32().Should().Be(4);
        value.GetProperty("eof").GetBoolean().Should().BeTrue();
        value.GetProperty("absolutePath").GetString().Should().Contain("readme.md");
        value.GetProperty("version").GetString().Should().NotBeNullOrEmpty("version=size-mtime 合成");
    }

    /// <summary>
    /// readBytes 走 multipart 二进制附件协议：value.data 必须是 null 占位，
    /// 原始字节挂在 result.Binary（宿主序列化为 bytes-0 Blob 并由前端回填 Uint8Array）。
    /// 之前返回 base64 字符串会被前端 zod 判 invalid_type（expected Uint8Array）。
    /// </summary>
    [Fact]
    public async Task ReadBytes_ReturnsNullPlaceholderAndBinaryAttachment() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/readBytes", FilesArgs(sessionId, "readme.md"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null, "二进制字段需 null 占位供 multipart 回填");
        value.GetProperty("eof").GetBoolean().Should().BeTrue();
        var binary = result.Binary ?? throw new Xunit.Sdk.XunitException("binary 附件缺失：前端只能解析 multipart 二进制");
        binary.Path.Should().Equal("data");
        var expected = Encoding.UTF8.GetBytes(await services.FileSystem.ReadAllTextAsync("/test/proj/readme.md"));
        binary.Bytes.Should().Equal(expected);
    }

    [Fact]
    public async Task Stat_ReturnsAbsolutePathAndVersion() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/stat", FilesArgs(sessionId, "app/main.cs"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("absolutePath").GetString().Should().Contain("main.cs");
        value.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Read_PathTraversal_ReturnsStableError() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/read", FilesArgs(sessionId, "../../etc/passwd"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("workspaceFiles/rejected");
        result.Error?.Message.Should().Contain("工作区外", "路径穿越必须被拦截并给出引导");
    }

    [Fact]
    public async Task Read_MissingFile_ReturnsNotFound() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/read", FilesArgs(sessionId, "no-such.txt"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("workspaceFiles/not-found");
    }

    [Fact]
    public async Task List_AbsoluteCwdPath_ReturnsEntries() {
        // DSH 前端文件树把 state.root（绝对路径）原样放进 path 发送，list 必须接受。
        var (services, sessionId, cwd) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/list", FilesArgs(sessionId, cwd));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("path").GetString().Should().Be(cwd, "path 回显前端请求值");
        value.GetProperty("entries").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task List_AbsoluteChildDirectory_ReturnsEntries() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/list", FilesArgs(sessionId, "/test/proj/app"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var entries = result.Value.GetValueOrDefault().GetProperty("entries");
        entries.GetArrayLength().Should().Be(1);
        entries[0].GetProperty("name").GetString().Should().Be("main.cs");
    }

    [Fact]
    public async Task Read_AbsolutePathInsideRoot_ReturnsText() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/read", FilesArgs(sessionId, "/test/proj/readme.md"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetProperty("text").GetString().Should().Contain("# hello");
    }

    [Fact]
    public async Task Read_AbsolutePathOutsideRoot_ReturnsRejected() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/read", FilesArgs(sessionId, "/test/other/secret.txt"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("workspaceFiles/rejected");
        result.Error?.Message.Should().Contain("工作区外");
    }

    [Fact]
    public async Task Read_AbsolutePathTraversal_ReturnsRejected() {
        var (services, sessionId, _) = await CreateSessionWithFilesAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("workspaceFiles/read", FilesArgs(sessionId, "/test/proj/../../etc/passwd"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("workspaceFiles/rejected");
    }
}
