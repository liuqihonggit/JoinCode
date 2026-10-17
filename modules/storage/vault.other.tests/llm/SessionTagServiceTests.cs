// 测试使用真实文件系统创建临时工作目录
#pragma warning disable JCC9002

// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Core.Tests.LLM;

[SuppressMessage("FileSystemAbstraction", "JCC9001", Justification = "会话标签服务测试需直接访问文件系统验证持久化")]

public sealed class SessionTagServiceTests {
    private static SessionTagService CreateService() {
        var tempPath = Path.Combine(Path.GetTempPath(), $"tag_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        var fileOp = new InMemoryFileOperationService();
        return new SessionTagService(Options.Create(new MemdirOptions { StoragePath = tempPath }), fileOp);
    }

    [Fact]
    public async Task AddTag_ShouldAddTag() {
        await using var service = CreateService();

        var added = service.AddTag("session1", "important");

        added.Should().BeTrue();
        service.GetTags("session1").Should().Contain("important");
    }

    [Fact]
    public async Task AddTag_SameTagTwice_ShouldReturnFalse() {
        await using var service = CreateService();

        service.AddTag("session1", "important");
        var added = service.AddTag("session1", "important");

        added.Should().BeFalse();
        service.GetTags("session1").Should().HaveCount(1);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task AddTag_NullSessionId_ShouldThrow() {
        await using var service = CreateService();

        var act = () => service.AddTag(null!, "tag");

        act.Should().Throw<ArgumentNullException>();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task AddTag_NullTag_ShouldThrow() {
        await using var service = CreateService();

        var act = () => service.AddTag("session1", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RemoveTag_ShouldRemoveTag() {
        await using var service = CreateService();
        service.AddTag("session1", "important");

        var removed = service.RemoveTag("session1", "important");

        removed.Should().BeTrue();
        service.GetTags("session1").Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveTag_NonExistentTag_ShouldReturnFalse() {
        await using var service = CreateService();

        var removed = service.RemoveTag("session1", "nonexistent");

        removed.Should().BeFalse();
    }

    [Fact]
    public async Task GetTags_UnknownSession_ShouldReturnEmpty() {
        await using var service = CreateService();

        service.GetTags("unknown").Should().BeEmpty();
    }

    [Fact]
    public async Task GetTags_ShouldBeSorted() {
        await using var service = CreateService();

        service.AddTag("session1", "zebra");
        service.AddTag("session1", "alpha");
        service.AddTag("session1", "middle");

        var tags = service.GetTags("session1");
        tags.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetAllTags_ShouldReturnAllSessions() {
        await using var service = CreateService();

        service.AddTag("session1", "tag1");
        service.AddTag("session2", "tag2");

        var allTags = service.GetAllTags();
        allTags.Should().ContainKey("session1");
        allTags.Should().ContainKey("session2");
    }

    [Fact]
    public async Task AddTag_CaseInsensitive_ShouldNotDuplicate() {
        await using var service = CreateService();

        service.AddTag("session1", "Important");
        var added = service.AddTag("session1", "important");

        added.Should().BeFalse();
        service.GetTags("session1").Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveTag_WhenLastTagRemoved_ShouldRemoveSession() {
        await using var service = CreateService();
        service.AddTag("session1", "only-tag");

        service.RemoveTag("session1", "only-tag");

        service.GetTags("session1").Should().BeEmpty();
        service.GetAllTags().Should().NotContainKey("session1");
    }
}