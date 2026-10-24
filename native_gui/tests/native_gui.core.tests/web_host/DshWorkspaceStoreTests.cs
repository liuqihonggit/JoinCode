// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 工作区注册表测试（ADR 0143 M2）：目录收养（同路径去重）、显示名取目录名、
/// 重启后仍在（换实例重读同一文件系统）、顺序调整、会话归属与归档/置顶集合，
/// 以及分发器 <c>workspace/*</c> 路由的参数校验与引导错误。
/// </summary>
public sealed class DshWorkspaceStoreTests {
    /// <summary>双平台绝对路径（Linux: /demo/proj-a；Windows: 盘根 C://demo//proj-a）— CI gui-core(ubuntu) 恒可跑</summary>
    private static readonly string RootA = Path.GetFullPath("/demo/proj-a");
    private static readonly string RootB = Path.GetFullPath("/demo/proj-b");

    /// <summary>建一个带两个已存在目录的存储</summary>
    private static async Task<(DshWorkspaceStore Store, InMemoryFileSystem FileSystem)> CreateStoreAsync() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(RootA);
        fs.CreateDirectory(RootB);
        return (new DshWorkspaceStore(fs, "/test/dsh-workspaces.json"), fs);
    }

    [Fact]
    public async Task Create_AdoptsDirectory_UsesFolderNameAsTitle() {
        var (store, _) = await CreateStoreAsync();
        var (workspace, created) = await store.CreateAsync(RootA);
        created.Should().BeTrue();
        workspace.Path.Should().Be(RootA);
        workspace.Title.Should().Be("proj-a");
        workspace.WorkspaceId.Should().NotBeNullOrWhiteSpace();
        workspace.SessionIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_SameDirectoryTwice_ReturnsSameWorkspace() {
        var (store, _) = await CreateStoreAsync();
        var first = await store.CreateAsync(RootA);
        var second = await store.CreateAsync(RootA + Path.DirectorySeparatorChar);
        second.Created.Should().BeFalse();
        second.Workspace.WorkspaceId.Should().Be(first.Workspace.WorkspaceId);
        (await store.ReadBaselineAsync()).Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Registry_SurvivesRestart_NewInstanceReadsSameFile() {
        var (store, fs) = await CreateStoreAsync();
        await store.CreateAsync(RootA);
        var restarted = new DshWorkspaceStore(fs, "/test/dsh-workspaces.json");
        var baseline = await restarted.ReadBaselineAsync();
        baseline.Items.Should().HaveCount(1);
        baseline.Items[0].Path.Should().Be(RootA);
    }

    [Fact]
    public async Task Delete_Rename_InsertBefore_UpdateRegistry() {
        var (store, _) = await CreateStoreAsync();
        var a = (await store.CreateAsync(RootA)).Workspace;
        var b = (await store.CreateAsync(RootB)).Workspace;
        (await store.RenameAsync(a.WorkspaceId, "重命名后")).Should().NotBeNull();
        var order = await store.InsertBeforeAsync(b.WorkspaceId, a.WorkspaceId);
        order.Should().Equal(b.WorkspaceId, a.WorkspaceId);
        (await store.DeleteAsync(a.WorkspaceId)).Should().BeTrue();
        (await store.DeleteAsync(a.WorkspaceId)).Should().BeFalse();
        var baseline = await store.ReadBaselineAsync();
        baseline.Items.Should().ContainSingle();
        baseline.Items[0].WorkspaceId.Should().Be(b.WorkspaceId);
    }

    [Fact]
    public async Task AttachSession_InsertSessionBefore_OrdersSessionsInsideWorkspace() {
        var (store, _) = await CreateStoreAsync();
        var workspace = (await store.CreateAsync(RootA)).Workspace;
        await store.AttachSessionAsync(workspace.WorkspaceId, "s1");
        var updated = await store.AttachSessionAsync(workspace.WorkspaceId, "s2")
            ?? throw new InvalidOperationException("AttachSessionAsync 未返回工作区");
        updated.SessionIds.Should().Equal("s1", "s2");
        var reordered = await store.InsertSessionBeforeAsync(workspace.WorkspaceId, "s2", "s1")
            ?? throw new InvalidOperationException("InsertSessionBeforeAsync 未返回工作区");
        reordered.SessionIds.Should().Equal("s2", "s1");
    }

    [Fact]
    public async Task Archive_Pin_Collections_AreExclusiveAndPersisted() {
        var (store, _) = await CreateStoreAsync();
        (await store.PinAsync("s1")).Should().Equal("s1");
        (await store.ArchiveAsync("s1")).Should().Equal("s1");
        (await store.PinAsync("s1")).Should().Equal("s1");
        (await store.ArchiveAsync("s2")).Should().Equal("s1", "s2");
        (await store.UnarchiveAsync("s1")).Should().Equal("s2");
        (await store.UnpinAsync("s1")).Should().BeEmpty();
    }

    [Fact]
    public async Task MutateUnknownWorkspace_ReturnsNull() {
        var (store, _) = await CreateStoreAsync();
        (await store.RenameAsync("missing", "x")).Should().BeNull();
        (await store.InsertBeforeAsync("missing", null)).Should().BeNull();
    }

    [Fact]
    public async Task Dispatcher_WorkspaceCreate_ValidatesPathAndDeduplicates() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(RootA);
        var dispatcher = new DshRpcDispatcher(new DshHostServices(fs, "/test/dsh-workspaces.json"));

        var missing = await dispatcher.HandleAsync("workspace/create", PathArgs(new DshWorkspacePathArgs()));
        missing.Ok.Should().BeFalse();
        ErrorCode(missing.Error).Should().Be("workspace/rejected");

        var notExist = await dispatcher.HandleAsync("workspace/create", PathArgs(new DshWorkspacePathArgs { Path = @"C:\nope" }));
        notExist.Ok.Should().BeFalse();
        ErrorCode(notExist.Error).Should().Be("workspace/rejected");

        var created = await dispatcher.HandleAsync("workspace/create", PathArgs(new DshWorkspacePathArgs { Path = RootA }));
        created.Ok.Should().BeTrue(created.Error?.Message);
        created.Value.GetValueOrDefault().GetProperty("created").GetBoolean().Should().BeTrue();

        var again = await dispatcher.HandleAsync("workspace/create", PathArgs(new DshWorkspacePathArgs { Path = RootA }));
        again.Value.GetValueOrDefault().GetProperty("created").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Dispatcher_UnknownMethod_FailsWithGuidance() {
        var dispatcher = new DshRpcDispatcher(new DshHostServices(new InMemoryFileSystem(), "/test/dsh-workspaces.json"));
        var result = await dispatcher.HandleAsync("session/send", default);
        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new InvalidOperationException("失败信封必须带 error");
        ErrorCode(error).Should().Be("unimplemented");
        error.Message.Should().Contain("session/send");
    }

    /// <summary>把 workspace/create 参数包成 DSH 生成的远程契约形状（具名参数在 request 下）</summary>
    private static JsonElement PathArgs(DshWorkspacePathArgs value) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshWorkspacePathArgs> { Request = value },
            DshJsonContext.Default.DshRequestEnvelopeDshWorkspacePathArgs);

    /// <summary>失败信封的错误码（Error 可能为 null 时给出断言失败信息）</summary>
    private static string ErrorCode(DshRpcError? error) => error?.Code ?? "missing-error";
}
