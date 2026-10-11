
namespace Core.Tests.Memdir;

/// <summary>
/// MemoryPaths + TeamMemoryPaths 纯路径计算确定性测试
/// 验证 Path.Combine 拼接逻辑,不依赖时序/IO
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class MemoryPathsTests {
    private const string Base = "/test/memdir/base";

    private static MemoryPaths CreateSut(string storagePath = Base)
        => new(Options.Create(new MemdirOptions { StoragePath = storagePath }));

    private static TeamMemoryPaths CreateTeamSut(string storagePath = Base)
        => new(Options.Create(new MemdirOptions { StoragePath = storagePath }));

    // === MemoryPaths.GetBaseMemoryDirectory ===

    [Fact]
    public async Task GetBaseMemoryDirectory_ReturnsConfiguredStoragePath() {
        await using var sut = CreateSut("/custom/base");
        sut.GetBaseMemoryDirectory().Should().Be("/custom/base");
    }

    // === MemoryPaths.GetUserMemoryDirectory ===

    [Fact]
    public async Task GetUserMemoryDirectory_WithExplicitId_CombinesBaseUsersId() {
        await using var sut = CreateSut();
        var result = sut.GetUserMemoryDirectory("alice");
        result.Should().Be(Path.Combine(Base, "users", "alice"));
    }

    [Fact]
    public async Task GetUserMemoryDirectory_WithNullId_FallsBackToDefault() {
        await using var sut = CreateSut();
        var result = sut.GetUserMemoryDirectory(null);
        result.Should().Be(Path.Combine(Base, "users", "default"));
    }

    // === MemoryPaths.GetProjectMemoryDirectory ===

    [Fact]
    public async Task GetProjectMemoryDirectory_WithExplicitId_CombinesBaseProjectsId() {
        await using var sut = CreateSut();
        var result = sut.GetProjectMemoryDirectory("proj-1");
        result.Should().Be(Path.Combine(Base, "projects", "proj-1"));
    }

    [Fact]
    public async Task GetProjectMemoryDirectory_WithNullId_FallsBackToDefault() {
        await using var sut = CreateSut();
        var result = sut.GetProjectMemoryDirectory(null);
        result.Should().Be(Path.Combine(Base, "projects", "default"));
    }

    // === MemoryPaths.GetMemoryDirectoryByType ===

    [Fact]
    public async Task GetMemoryDirectoryByType_User_ReturnsUserDirectory() {
        await using var sut = CreateSut();
        var result = sut.GetMemoryDirectoryByType(MemoryType.User, "alice");
        result.Should().Be(Path.Combine(Base, "users", "alice"));
    }

    [Fact]
    public async Task GetMemoryDirectoryByType_Feedback_ReturnsUserFeedbackSubdirectory() {
        await using var sut = CreateSut();
        var result = sut.GetMemoryDirectoryByType(MemoryType.Feedback, "alice");
        result.Should().Be(Path.Combine(Base, "users", "alice", "feedback"));
    }

    [Fact]
    public async Task GetMemoryDirectoryByType_Project_ReturnsProjectDirectory() {
        await using var sut = CreateSut();
        var result = sut.GetMemoryDirectoryByType(MemoryType.Project, "proj-1");
        result.Should().Be(Path.Combine(Base, "projects", "proj-1"));
    }

    [Fact]
    public async Task GetMemoryDirectoryByType_Reference_ReturnsBaseReferencesDirectory() {
        await using var sut = CreateSut();
        var result = sut.GetMemoryDirectoryByType(MemoryType.Reference);
        // Reference 不依赖 contextId,固定为 base/references
        result.Should().Be(Path.Combine(Base, "references"));
    }

    [Fact]
    public async Task GetMemoryDirectoryByType_Reference_IgnoresContextId() {
        await using var sut = CreateSut();
        var withContext = sut.GetMemoryDirectoryByType(MemoryType.Reference, "ignored");
        var withoutContext = sut.GetMemoryDirectoryByType(MemoryType.Reference);
        withContext.Should().Be(withoutContext);
    }

    // === MemoryPaths.GetMemoryFilePath ===

    [Fact]
    public async Task GetMemoryFilePath_AppendsJsonExtension() {
        await using var sut = CreateSut();
        var result = sut.GetMemoryFilePath("mem-001", MemoryType.User, "alice");
        result.Should().Be(Path.Combine(Base, "users", "alice", "mem-001.json"));
    }

    [Fact]
    public async Task GetMemoryFilePath_ForReference_CombinesReferencesWithJson() {
        await using var sut = CreateSut();
        var result = sut.GetMemoryFilePath("ref-abc", MemoryType.Reference);
        result.Should().Be(Path.Combine(Base, "references", "ref-abc.json"));
    }

    [Fact]
    public async Task GetMemoryFilePath_ForFeedback_CombinesFeedbackSubdirWithJson() {
        await using var sut = CreateSut();
        var result = sut.GetMemoryFilePath("fb-1", MemoryType.Feedback, "bob");
        result.Should().Be(Path.Combine(Base, "users", "bob", "feedback", "fb-1.json"));
    }

    // === TeamMemoryPaths ===

    [Fact]
    public async Task TeamMemoryPaths_GetTeamMemoryDirectory_CombinesBaseTeamsTeamId() {
        await using var sut = CreateTeamSut();
        var result = sut.GetTeamMemoryDirectory("team-42");
        // TeamMemoryPaths 在 StoragePath 下追加 "team-memories"
        var expectedBase = Path.Combine(Base, "team-memories");
        result.Should().Be(Path.Combine(expectedBase, "teams", "team-42"));
    }

    [Fact]
    public async Task TeamMemoryPaths_GetTeamSharedDirectory_AppendsSharedSubdir() {
        await using var sut = CreateTeamSut();
        var result = sut.GetTeamSharedDirectory("team-42");
        var expectedBase = Path.Combine(Base, "team-memories");
        result.Should().Be(Path.Combine(expectedBase, "teams", "team-42", "shared"));
    }

    [Fact]
    public async Task TeamMemoryPaths_GetTeamMemberDirectory_AppendsMembersUserId() {
        await using var sut = CreateTeamSut();
        var result = sut.GetTeamMemberDirectory("team-42", "alice");
        var expectedBase = Path.Combine(Base, "team-memories");
        result.Should().Be(Path.Combine(expectedBase, "teams", "team-42", "members", "alice"));
    }

    [Fact]
    public async Task TeamMemoryPaths_GetTeamSharedDirectory_IsSubdirOfTeamMemoryDirectory() {
        await using var sut = CreateTeamSut();
        var teamDir = sut.GetTeamMemoryDirectory("team-1");
        var sharedDir = sut.GetTeamSharedDirectory("team-1");
        sharedDir.Should().StartWith(teamDir);
        sharedDir.Should().EndWith(Path.Combine("shared"));
    }

    [Fact]
    public async Task TeamMemoryPaths_GetTeamMemberDirectory_IsSubdirOfTeamMemoryDirectory() {
        await using var sut = CreateTeamSut();
        var teamDir = sut.GetTeamMemoryDirectory("team-1");
        var memberDir = sut.GetTeamMemberDirectory("team-1", "user-x");
        memberDir.Should().StartWith(teamDir);
        memberDir.Should().Contain("members");
        memberDir.Should().Contain("user-x");
    }

    // === 确定性:相同输入→相同输出 ===

    [Fact]
    public async Task GetMemoryFilePath_Deterministic_SameInputProducesSameOutput() {
        await using var sut = CreateSut();
        var r1 = sut.GetMemoryFilePath("m1", MemoryType.User, "u1");
        var r2 = sut.GetMemoryFilePath("m1", MemoryType.User, "u1");
        r1.Should().Be(r2);
    }

    [Fact]
    public async Task GetMemoryDirectoryByType_Deterministic_SameInputProducesSameOutput() {
        await using var sut = CreateSut();
        var r1 = sut.GetMemoryDirectoryByType(MemoryType.Project, "p1");
        var r2 = sut.GetMemoryDirectoryByType(MemoryType.Project, "p1");
        r1.Should().Be(r2);
    }
}
