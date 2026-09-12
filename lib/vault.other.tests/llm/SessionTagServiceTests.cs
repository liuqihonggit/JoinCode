// 测试使用真实文件系统创建临时工作目录
#pragma warning disable JCC9001, JCC9002
namespace Core.Tests.LLM;

public sealed class SessionTagServiceTests
{
    private static SessionTagService CreateService()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"tag_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        var fileOp = new InMemoryFileOperationService();
        return new SessionTagService(Options.Create(new MemdirOptions { StoragePath = tempPath }), fileOp);
    }

    [Fact]
    public void AddTag_ShouldAddTag()
    {
        var service = CreateService();

        var added = service.AddTag("session1", "important");

        added.Should().BeTrue();
        service.GetTags("session1").Should().Contain("important");
    }

    [Fact]
    public void AddTag_SameTagTwice_ShouldReturnFalse()
    {
        var service = CreateService();

        service.AddTag("session1", "important");
        var added = service.AddTag("session1", "important");

        added.Should().BeFalse();
        service.GetTags("session1").Should().HaveCount(1);
    }

    [Fact]
    public void AddTag_NullSessionId_ShouldThrow()
    {
        var service = CreateService();

        var act = () => service.AddTag(null!, "tag");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTag_NullTag_ShouldThrow()
    {
        var service = CreateService();

        var act = () => service.AddTag("session1", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveTag_ShouldRemoveTag()
    {
        var service = CreateService();
        service.AddTag("session1", "important");

        var removed = service.RemoveTag("session1", "important");

        removed.Should().BeTrue();
        service.GetTags("session1").Should().BeEmpty();
    }

    [Fact]
    public void RemoveTag_NonExistentTag_ShouldReturnFalse()
    {
        var service = CreateService();

        var removed = service.RemoveTag("session1", "nonexistent");

        removed.Should().BeFalse();
    }

    [Fact]
    public void GetTags_UnknownSession_ShouldReturnEmpty()
    {
        var service = CreateService();

        service.GetTags("unknown").Should().BeEmpty();
    }

    [Fact]
    public void GetTags_ShouldBeSorted()
    {
        var service = CreateService();

        service.AddTag("session1", "zebra");
        service.AddTag("session1", "alpha");
        service.AddTag("session1", "middle");

        var tags = service.GetTags("session1");
        tags.Should().BeInAscendingOrder();
    }

    [Fact]
    public void GetAllTags_ShouldReturnAllSessions()
    {
        var service = CreateService();

        service.AddTag("session1", "tag1");
        service.AddTag("session2", "tag2");

        var allTags = service.GetAllTags();
        allTags.Should().ContainKey("session1");
        allTags.Should().ContainKey("session2");
    }

    [Fact]
    public void AddTag_CaseInsensitive_ShouldNotDuplicate()
    {
        var service = CreateService();

        service.AddTag("session1", "Important");
        var added = service.AddTag("session1", "important");

        added.Should().BeFalse();
        service.GetTags("session1").Should().HaveCount(1);
    }

    [Fact]
    public void RemoveTag_WhenLastTagRemoved_ShouldRemoveSession()
    {
        var service = CreateService();
        service.AddTag("session1", "only-tag");

        service.RemoveTag("session1", "only-tag");

        service.GetTags("session1").Should().BeEmpty();
        service.GetAllTags().Should().NotContainKey("session1");
    }
}
