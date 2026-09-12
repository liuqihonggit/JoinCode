namespace Infra.Tests.HotSpot;


public sealed class DeferredMailServiceTests
{
    private readonly IDeferredMailService _sut = new DeferredMailService();

    private static DeferredMail MakeMail(string to, int turns = 3, MailMarker marker = MailMarker.ResourceRefChange) =>
        new() { To = to, From = "captain", Subject = "test", Body = "body", OpenAfterTurns = turns, Marker = marker, CreatedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task DeferThenTick_MaturedAfterNTurns_ShouldReturnMail()
    {
        await _sut.DeferAsync(MakeMail("w1", turns: 3));

        _sut.TickTurns("w1").Should().BeEmpty();
        _sut.TickTurns("w1").Should().BeEmpty();
        var matured = _sut.TickTurns("w1");

        matured.Should().HaveCount(1);
        _sut.GetPending("w1").Should().BeEmpty();
    }

    [Fact]
    public async Task DeferThenTick_Default20Turns_ShouldMatureAfter20()
    {
        await _sut.DeferAsync(MakeMail("w1", turns: 20));

        for (int i = 0; i < 19; i++)
            _sut.TickTurns("w1").Should().BeEmpty($"第{i+1}轮不应到期");

        _sut.TickTurns("w1").Should().HaveCount(1, "第20轮到期");
    }

    [Fact]
    public async Task FlushOnTaskEnd_ShouldReturnAllPendingAndClear()
    {
        await _sut.DeferAsync(MakeMail("w1", turns: 100));
        await _sut.DeferAsync(MakeMail("w1", turns: 100));

        var all = _sut.FlushOnTaskEnd("w1");

        all.Should().HaveCount(2);
        _sut.GetPending("w1").Should().BeEmpty();
    }

    [Fact]
    public async Task GetPending_ShouldReturnNotMaturedMails()
    {
        await _sut.DeferAsync(MakeMail("w1", turns: 5));
        await _sut.DeferAsync(MakeMail("w1", turns: 10));

        _sut.GetPending("w1").Should().HaveCount(2);
        _sut.TickTurns("w1");
        _sut.GetPending("w1").Should().HaveCount(2, "Tick不移除未到期邮件");
    }

    [Fact]
    public async Task MultipleAgents_ShouldBeIsolated()
    {
        await _sut.DeferAsync(MakeMail("w1", turns: 1));
        await _sut.DeferAsync(MakeMail("w2", turns: 1));

        _sut.TickTurns("w1").Should().HaveCount(1);
        _sut.TickTurns("w2").Should().HaveCount(1);
        _sut.GetPending("w1").Should().BeEmpty();
        _sut.GetPending("w2").Should().BeEmpty();
    }

    [Fact]
    public async Task TickTurns_NoMails_ShouldReturnEmpty()
    {
        _sut.TickTurns("nonexistent").Should().BeEmpty();
    }

    [Fact]
    public async Task HighPriorityMail_ShouldBeMarkedCorrectly()
    {
        await _sut.DeferAsync(MakeMail("w1", turns: 1, marker: MailMarker.HotFileConflict));

        var matured = _sut.TickTurns("w1");
        matured[0].IsHighPriority.Should().BeTrue();
        matured[0].Marker.Should().Be(MailMarker.HotFileConflict);
    }

    [Fact]
    public async Task GetPending_WithMarkerFilter_ShouldReturnOnlyMailsContainingMarker()
    {
        await _sut.DeferAsync(MakeMail("w1", turns: 100, marker: MailMarker.HotFileConflict));
        await _sut.DeferAsync(MakeMail("w1", turns: 100, marker: MailMarker.TestFileConflict));
        await _sut.DeferAsync(MakeMail("w1", turns: 100, marker: MailMarker.HotFileConflict | MailMarker.ResourceRefChange));

        _sut.GetPending("w1", MailMarker.HotFileConflict).Should().HaveCount(2, "两封含 HotFileConflict");
        _sut.GetPending("w1", MailMarker.TestFileConflict).Should().HaveCount(1, "一封含 TestFileConflict");
        _sut.GetPending("w1", MailMarker.ResourceRefChange).Should().HaveCount(1, "一封含 ResourceRefChange");
        _sut.GetPending("w1").Should().HaveCount(3, "无过滤返回全部");
        _sut.GetPending("w1", MailMarker.None).Should().HaveCount(3, "None 过滤等同不过滤");
    }

    [Fact]
    public async Task FlushOnTaskEnd_WithMarkerFilter_ShouldRemoveOnlyMatchingAndKeepOthers()
    {
        await _sut.DeferAsync(MakeMail("w1", turns: 100, marker: MailMarker.HotFileConflict));
        await _sut.DeferAsync(MakeMail("w1", turns: 100, marker: MailMarker.TestFileConflict));

        var hot = _sut.FlushOnTaskEnd("w1", MailMarker.HotFileConflict);

        hot.Should().HaveCount(1);
        hot[0].Marker.Should().Be(MailMarker.HotFileConflict);
        _sut.GetPending("w1").Should().HaveCount(1, "未匹配的应保留");
        _sut.GetPending("w1")[0].Marker.Should().Be(MailMarker.TestFileConflict);
    }
}
