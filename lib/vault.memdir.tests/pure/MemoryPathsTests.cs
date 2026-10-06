
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
    public void GetBaseMemoryDirectory_ReturnsConfiguredStoragePath() {
        using var sut = CreateSut("/custom/base");
        sut.GetBaseMemoryDirectory().Should().Be("/custom/base");
    }

    // === MemoryPaths.GetUserMemoryDirectory ===

    [Fact]
    public void GetUserMemoryDirectory_WithExplicitId_CombinesBaseUsersId() {
        using var sut = CreateSut();
        var result = sut.GetUserMemoryDirectory("alice");
        result.Should().Be(Path.Combine(Base, "users", "alice"));
    }

    [Fact]
    public void GetUserMemoryDirectory_WithNullId_FallsBackToDefault() {
        using var sut = CreateSut();
        var result = sut.GetUserMemoryDirectory(null);
        result.Should().Be(Path.Combine(Base, "users", "default"));
    }

    // === MemoryPaths.GetProjectMemoryDirectory ===

    [Fact]
    public void GetProjectMemoryDirectory_WithExplicitId_CombinesBaseProjectsId() {
        using var sut = CreateSut();
        var result = sut.GetProjectMemoryDirectory("proj-1");
        result.Should().Be(Path.Combine(Base, "projects", "proj-1"));
    }

    [Fact]
    public void GetProjectMemoryDirectory_WithNullId_FallsBackToDefault() {
        using var sut = CreateSut();
        var result = sut.GetProjectMemoryDirectory(null);
        result.Should().Be(Path.Combine(Base, "projects", "default"));
    }

    // === MemoryPaths.GetMemoryDirectoryByType ===

    [Fact]
    public void GetMemoryDirectoryByType_User_ReturnsUserDirectory() {
        using var sut = CreateSut();
        var result = sut.GetMemoryDirectoryByType(MemoryType.User, "alice");
        result.Should().Be(Path.Combine(Base, "users", "alice"));
    }

    [Fact]
    public void GetMemoryDirectoryByType_Feedback_ReturnsUserFeedbackSubdirectory() {
        using var sut = CreateSut();
        var result = sut.GetMemoryDirectoryByType(MemoryType.Feedback, "alice");
        result.Should().Be(Path.Combine(Base, "users", "alice", "feedback"));
    }

    [Fact]
    public void GetMemoryDirectoryByType_Project_ReturnsProjectDirectory() {
        using var sut = CreateSut();
        var result = sut.GetMemoryDirectoryByType(MemoryType.Project, "proj-1");
        result.Should().Be(Path.Combine(Base, "projects", "proj-1"));
    }

    [Fact]
    public void GetMemoryDirectoryByType_Reference_ReturnsBaseReferencesDirectory() {
        using var sut = CreateSut();
        var result = sut.GetMemoryDirectoryByType(MemoryType.Reference);
        // Reference 不依赖 contextId,固定为 base/references
        result.Should().Be(Path.Combine(Base, "references"));
    }

    [Fact]
    public void GetMemoryDirectoryByType_Reference_IgnoresContextId() {
        using var sut = CreateSut();
        var withContext = sut.GetMemoryDirectoryByType(MemoryType.Reference, "ignored");
        var withoutContext = sut.GetMemoryDirectoryByType(MemoryType.Reference);
        withContext.Should().Be(withoutContext);
    }

    // === MemoryPaths.GetMemoryFilePath ===

    [Fact]
    public void GetMemoryFilePath_AppendsJsonExtension() {
        using var sut = CreateSut();
        var result = sut.GetMemoryFilePath("mem-001", MemoryType.User, "alice");
        result.Should().Be(Path.Combine(Base, "users", "alice", "mem-001.json"));
    }

    [Fact]
    public void GetMemoryFilePath_ForReference_CombinesReferencesWithJson() {
        using var sut = CreateSut();
        var result = sut.GetMemoryFilePath("ref-abc", MemoryType.Reference);
        result.Should().Be(Path.Combine(Base, "references", "ref-abc.json"));
    }

    [Fact]
    public void GetMemoryFilePath_ForFeedback_CombinesFeedbackSubdirWithJson() {
        using var sut = CreateSut();
        var result = sut.GetMemoryFilePath("fb-1", MemoryType.Feedback, "bob");
        result.Should().Be(Path.Combine(Base, "users", "bob", "feedback", "fb-1.json"));
    }

    // === TeamMemoryPaths ===

    [Fact]
    public void TeamMemoryPaths_GetTeamMemoryDirectory_CombinesBaseTeamsTeamId() {
        using var sut = CreateTeamSut();
        var result = sut.GetTeamMemoryDirectory("team-42");
        // TeamMemoryPaths 在 StoragePath 下追加 "team-memories"
        var expectedBase = Path.Combine(Base, "team-memories");
        result.Should().Be(Path.Combine(expectedBase, "teams", "team-42"));
    }

    [Fact]
    public void TeamMemoryPaths_GetTeamSharedDirectory_AppendsSharedSubdir() {
        using var sut = CreateTeamSut();
        var result = sut.GetTeamSharedDirectory("team-42");
        var expectedBase = Path.Combine(Base, "team-memories");
        result.Should().Be(Path.Combine(expectedBase, "teams", "team-42", "shared"));
    }

    [Fact]
    public void TeamMemoryPaths_GetTeamMemberDirectory_AppendsMembersUserId() {
        using var sut = CreateTeamSut();
        var result = sut.GetTeamMemberDirectory("team-42", "alice");
        var expectedBase = Path.Combine(Base, "team-memories");
        result.Should().Be(Path.Combine(expectedBase, "teams", "team-42", "members", "alice"));
    }

    [Fact]
    public void TeamMemoryPaths_GetTeamSharedDirectory_IsSubdirOfTeamMemoryDirectory() {
        using var sut = CreateTeamSut();
        var teamDir = sut.GetTeamMemoryDirectory("team-1");
        var sharedDir = sut.GetTeamSharedDirectory("team-1");
        sharedDir.Should().StartWith(teamDir);
        sharedDir.Should().EndWith(Path.Combine("shared"));
    }

    [Fact]
    public void TeamMemoryPaths_GetTeamMemberDirectory_IsSubdirOfTeamMemoryDirectory() {
        using var sut = CreateTeamSut();
        var teamDir = sut.GetTeamMemoryDirectory("team-1");
        var memberDir = sut.GetTeamMemberDirectory("team-1", "user-x");
        memberDir.Should().StartWith(teamDir);
        memberDir.Should().Contain("members");
        memberDir.Should().Contain("user-x");
    }

    // === 确定性:相同输入→相同输出 ===

    [Fact]
    public void GetMemoryFilePath_Deterministic_SameInputProducesSameOutput() {
        using var sut = CreateSut();
        var r1 = sut.GetMemoryFilePath("m1", MemoryType.User, "u1");
        var r2 = sut.GetMemoryFilePath("m1", MemoryType.User, "u1");
        r1.Should().Be(r2);
    }

    [Fact]
    public void GetMemoryDirectoryByType_Deterministic_SameInputProducesSameOutput() {
        using var sut = CreateSut();
        var r1 = sut.GetMemoryDirectoryByType(MemoryType.Project, "p1");
        var r2 = sut.GetMemoryDirectoryByType(MemoryType.Project, "p1");
        r1.Should().Be(r2);
    }
}
