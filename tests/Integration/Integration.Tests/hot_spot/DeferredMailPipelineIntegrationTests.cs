
namespace Integration.Tests.HotSpot;

/// <summary>
/// 延迟邮件全链路集成测试 — 验证 subAgent 报告 → IntentReporter 分流 → DeferredMailService 投递 → Agent 消费 的完整链路
/// </summary>
[Trait("Category", "Integration")]
public sealed class DeferredMailPipelineIntegrationTests
{
    private const string WorkerId = "worker-1";
    private const string CaptainId = "captain";

    private static JoinCode.Abstractions.Models.Agent.FileModifyIntent CreateIntent(
        string filePath,
        JoinCode.Abstractions.Models.Agent.ModifyIntent intent,
        JoinCode.Abstractions.Models.Agent.MailMarker marker) =>
        new()
        {
            FilePath = filePath,
            Intent = intent,
            WorkerId = WorkerId,
            ReportedAt = DateTimeOffset.UtcNow,
            Marker = marker
        };

    private static Infrastructure.HotSpot.IntentReporter CreateReporter(
        Mock<IIntentCollector> intentCollector,
        Mock<IHotFileDetector> hotFileDetector,
        Mock<IMailbox> mailbox,
        Infrastructure.HotSpot.DeferredMailService deferredMailService) =>
        new(intentCollector.Object, hotFileDetector.Object, mailbox.Object, deferredMailService);

    /// <summary>
    /// 链路: subAgent 报告 TestFileConflict → IntentReporter 分流延迟投递 → DeferredMailService.DeferAsync
    /// → Agent 空闲 FlushOnTaskEnd 立即消费 → 邮件 Marker 保持正确
    /// </summary>
    [Fact]
    public async Task DeferredMail_Pipeline_TestFileConflict_Should_Defer_And_Consume_On_Flush()
    {
        var deferredMailService = new Infrastructure.HotSpot.DeferredMailService();
        var intentCollector = new Mock<IIntentCollector>();
        var hotFileDetector = new Mock<IHotFileDetector>();
        hotFileDetector.Setup(d => d.IsHotFile(It.IsAny<string>())).Returns(false);
        var mailbox = new Mock<IMailbox>();

        var reporter = CreateReporter(intentCollector, hotFileDetector, mailbox, deferredMailService);

        var intent = CreateIntent("src/Service.cs",
            JoinCode.Abstractions.Models.Agent.ModifyIntent.InternalChange,
            JoinCode.Abstractions.Models.Agent.MailMarker.TestFileConflict);
        await reporter.ReportModifyIntentsAsync(WorkerId, CaptainId, [intent]);

        var immature = deferredMailService.TickTurns(CaptainId);
        Assert.Empty(immature);

        var consumed = deferredMailService.FlushOnTaskEnd(CaptainId);
        Assert.Single(consumed);
        Assert.Equal(JoinCode.Abstractions.Models.Agent.MailMarker.TestFileConflict, consumed[0].Marker);
        Assert.Equal(CaptainId, consumed[0].To);
        Assert.Equal(WorkerId, consumed[0].From);

        var afterConsume = deferredMailService.FlushOnTaskEnd(CaptainId);
        Assert.Empty(afterConsume);
    }

    /// <summary>
    /// 链路: subAgent 报告热文件 ContractChange → IntentReporter 实时 IMailbox 通知队长 → 不走延迟邮件
    /// </summary>
    [Fact]
    public async Task HotFile_ContractChange_Should_Notify_Mailbox_RealTime_Not_Defer()
    {
        var deferredMailService = new Infrastructure.HotSpot.DeferredMailService();
        var intentCollector = new Mock<IIntentCollector>();
        var hotFileDetector = new Mock<IHotFileDetector>();
        hotFileDetector.Setup(d => d.IsHotFile(It.IsAny<string>())).Returns(true);
        var mailbox = new Mock<IMailbox>();

        var reporter = CreateReporter(intentCollector, hotFileDetector, mailbox, deferredMailService);

        var intent = CreateIntent("src/HotFile.cs",
            JoinCode.Abstractions.Models.Agent.ModifyIntent.ContractChange,
            JoinCode.Abstractions.Models.Agent.MailMarker.HotFileConflict);
        await reporter.ReportModifyIntentsAsync(WorkerId, CaptainId, [intent]);

        mailbox.Verify(m => m.SendAsync(CaptainId, It.IsAny<JoinCode.Abstractions.Models.Agent.CoordinatorMessage>(), It.IsAny<CancellationToken>()), Times.Once);

        var deferred = deferredMailService.FlushOnTaskEnd(CaptainId);
        Assert.Empty(deferred);
    }

    /// <summary>
    /// 链路: subAgent 报告组合标记 TestFileConflict|ResourceRefChange → 延迟投递 → Marker 保持组合值
    /// </summary>
    [Fact]
    public async Task DeferredMail_Pipeline_CombinedMarker_Should_Preserve_Flags()
    {
        var deferredMailService = new Infrastructure.HotSpot.DeferredMailService();
        var intentCollector = new Mock<IIntentCollector>();
        var hotFileDetector = new Mock<IHotFileDetector>();
        var mailbox = new Mock<IMailbox>();

        var reporter = CreateReporter(intentCollector, hotFileDetector, mailbox, deferredMailService);

        var combinedMarker = JoinCode.Abstractions.Models.Agent.MailMarker.TestFileConflict
            | JoinCode.Abstractions.Models.Agent.MailMarker.ResourceRefChange;
        var intent = CreateIntent("src/Shared.cs",
            JoinCode.Abstractions.Models.Agent.ModifyIntent.InternalChange,
            combinedMarker);
        await reporter.ReportModifyIntentsAsync(WorkerId, CaptainId, [intent]);

        var consumed = deferredMailService.FlushOnTaskEnd(CaptainId);
        Assert.Single(consumed);
        Assert.Equal(combinedMarker, consumed[0].Marker);
        Assert.True(consumed[0].Marker.HasFlag(JoinCode.Abstractions.Models.Agent.MailMarker.TestFileConflict));
        Assert.True(consumed[0].Marker.HasFlag(JoinCode.Abstractions.Models.Agent.MailMarker.ResourceRefChange));
    }

    /// <summary>
    /// 链路: 多个 subAgent 报告不同标记 → 延迟投递多封 → 按标记过滤消费
    /// </summary>
    [Fact]
    public async Task DeferredMail_Pipeline_MultipleIntents_Should_Filter_By_Marker()
    {
        var deferredMailService = new Infrastructure.HotSpot.DeferredMailService();
        var intentCollector = new Mock<IIntentCollector>();
        var hotFileDetector = new Mock<IHotFileDetector>();
        var mailbox = new Mock<IMailbox>();

        var reporter = CreateReporter(intentCollector, hotFileDetector, mailbox, deferredMailService);

        var intents = new[]
        {
            CreateIntent("src/Test.cs",
                JoinCode.Abstractions.Models.Agent.ModifyIntent.InternalChange,
                JoinCode.Abstractions.Models.Agent.MailMarker.TestFileConflict),
            CreateIntent("src/Resource.cs",
                JoinCode.Abstractions.Models.Agent.ModifyIntent.InternalChange,
                JoinCode.Abstractions.Models.Agent.MailMarker.ResourceRefChange),
        };
        await reporter.ReportModifyIntentsAsync(WorkerId, CaptainId, intents);

        var testOnly = deferredMailService.FlushOnTaskEnd(CaptainId, JoinCode.Abstractions.Models.Agent.MailMarker.TestFileConflict);
        Assert.Single(testOnly);
        Assert.Equal(JoinCode.Abstractions.Models.Agent.MailMarker.TestFileConflict, testOnly[0].Marker);
    }

    /// <summary>
    /// 链路: 延迟邮件按轮次到期消费 — OpenAfterTurns=20, 前19轮返回空, 第20轮到期
    /// </summary>
    [Fact]
    public async Task DeferredMail_Pipeline_TickTurns_Should_Mature_After_Specified_Turns()
    {
        var deferredMailService = new Infrastructure.HotSpot.DeferredMailService();
        var intentCollector = new Mock<IIntentCollector>();
        var hotFileDetector = new Mock<IHotFileDetector>();
        var mailbox = new Mock<IMailbox>();

        var reporter = CreateReporter(intentCollector, hotFileDetector, mailbox, deferredMailService);

        var intent = CreateIntent("src/Lazy.cs",
            JoinCode.Abstractions.Models.Agent.ModifyIntent.InternalChange,
            JoinCode.Abstractions.Models.Agent.MailMarker.ResourceRefChange);
        await reporter.ReportModifyIntentsAsync(WorkerId, CaptainId, [intent]);

        for (var i = 0; i < 19; i++)
        {
            var immature = deferredMailService.TickTurns(CaptainId);
            Assert.Empty(immature);
        }

        var matured = deferredMailService.TickTurns(CaptainId);
        Assert.Single(matured);
        Assert.Equal(JoinCode.Abstractions.Models.Agent.MailMarker.ResourceRefChange, matured[0].Marker);
    }
}
