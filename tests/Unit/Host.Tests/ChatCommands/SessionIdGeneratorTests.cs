namespace Host.Tests.ChatCommands;

/// <summary>
/// SessionIdGenerator 格式测试（T10）— 统一会话 ID 格式：
/// {yyyyMMdd-HHmm}-{项目名}-{分支}-parent-{ObjectId全局递增数}。
/// 回归背景：sessions 目录此前混杂 default/session-*/t6-e2e-*/GUID 等多种命名；
/// 用户规范要求日期+项目+分支+父子标记+ObjectId 序号五段式。
/// </summary>
public sealed class SessionIdGeneratorTests
{
    [Fact]
    public void Generate_ParentRole_FollowsUserFormat()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sid-proj");
        var at = new DateTime(2026, 8, 22, 7, 12, 0, DateTimeKind.Utc);

        var sessionId = SessionIdGenerator.Generate(dir, at);

        // 非 git 目录 → 分支回退 no-branch；末段为 ObjectId 全局递增数（纯数字）
        sessionId.Should().MatchRegex(@"^20260822-0712-sid-proj-no-branch-parent-\d+$");
    }

    [Fact]
    public void Generate_SequenceIsGloballyIncrementing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sid-seq");
        var at = new DateTime(2026, 8, 22, 7, 13, 0, DateTimeKind.Utc);

        var first = SessionIdGenerator.Generate(dir, at);
        var second = SessionIdGenerator.Generate(dir, at);

        var firstSeq = long.Parse(first.Split('-').Last());
        var secondSeq = long.Parse(second.Split('-').Last());
        secondSeq.Should().BeGreaterThan(firstSeq, "ObjectId 全局序列必须严格递增");
    }

    [Fact]
    public void Generate_SameMinute_DistinctBySequence()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sid-min");
        var at = new DateTime(2026, 8, 22, 7, 14, 0, DateTimeKind.Utc);

        var a = SessionIdGenerator.Generate(dir, at);
        var b = SessionIdGenerator.Generate(dir, at);

        a.Should().NotBe(b, "同分钟多次生成靠 ObjectId 序号区分，不得冲突");
    }
}
