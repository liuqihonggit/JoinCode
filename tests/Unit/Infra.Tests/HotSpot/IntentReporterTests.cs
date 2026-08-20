namespace Infra.Tests.HotSpot;

using Infrastructure.HotSpot;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;

public sealed class IntentReporterTests
{
    private readonly IntentCollector _collector = new();
    private readonly HotFileDetector _detector = new();
    private readonly FakeMailbox _mailbox = new();
    private readonly IIntentReporter _sut;

    public IntentReporterTests()
    {
        _sut = new IntentReporter(_collector, _detector, _mailbox);
    }

    private static FileModifyIntent MakeIntent(string path, ModifyIntent intent, string workerId = "w1") =>
        new() { FilePath = path, Intent = intent, WorkerId = workerId, ReportedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task ReportModifyIntentsAsync_HotFileContractChange_ShouldSendMailToCaptain()
    {
        var intent = MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange);

        await _sut.ReportModifyIntentsAsync("w1", "captain", [intent]);

        _mailbox.SentMessages.Should().HaveCount(1);
        var msg = _mailbox.SentMessages[0];
        msg.FromAgentId.Should().Be("w1");
        msg.ToAgentId.Should().Be("captain");
        msg.StructuredType.Should().Be(TeammateMessageType.IntentReport);
        msg.Content.Should().Contain("IFoo.cs");
    }

    [Fact]
    public async Task ReportModifyIntentsAsync_NormalFileContractChange_ShouldNotSendMail()
    {
        var intent = MakeIntent("src/utils/helper.cs", ModifyIntent.ContractChange);

        await _sut.ReportModifyIntentsAsync("w1", "captain", [intent]);

        _mailbox.SentMessages.Should().BeEmpty("非热文件不上报邮箱");
        _collector.GetIntents("src/utils/helper.cs").Should().HaveCount(1, "但仍收集到IntentCollector");
    }

    [Fact]
    public async Task ReportModifyIntentsAsync_HotFileInternalChange_ShouldNotSendMail()
    {
        var intent = MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.InternalChange);

        await _sut.ReportModifyIntentsAsync("w1", "captain", [intent]);

        _mailbox.SentMessages.Should().BeEmpty("内部修改不上报邮箱");
        _collector.GetIntents("src/Abstractions/IFoo.cs").Should().HaveCount(1, "但仍收集到IntentCollector");
    }

    [Fact]
    public async Task ReportModifyIntentsAsync_MixedIntents_ShouldOnlyMailHotContract()
    {
        var intents = new List<FileModifyIntent>
        {
            MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange),
            MakeIntent("src/Abstractions/IBar.cs", ModifyIntent.ContractChange),
            MakeIntent("src/utils/helper.cs", ModifyIntent.ContractChange),
            MakeIntent("src/Abstractions/IBaz.cs", ModifyIntent.InternalChange)
        };

        await _sut.ReportModifyIntentsAsync("w1", "captain", intents);

        _mailbox.SentMessages.Should().HaveCount(1, "只有2个热文件契约改合并为1条消息");
        _mailbox.SentMessages[0].Content.Should().Contain("IFoo.cs");
        _mailbox.SentMessages[0].Content.Should().Contain("IBar.cs");
        _mailbox.SentMessages[0].Content.Should().NotContain("helper.cs");
        _collector.GetAllIntents().Should().HaveCount(4, "全部收集到IntentCollector");
    }

    [Fact]
    public async Task ReportModifyIntentsAsync_EmptyIntents_ShouldNotSendMail()
    {
        await _sut.ReportModifyIntentsAsync("w1", "captain", []);

        _mailbox.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportModifyIntentsAsync_NullWorkerId_ShouldThrow()
    {
        var act = () => _sut.ReportModifyIntentsAsync("", "captain", [MakeIntent("a.cs", ModifyIntent.ContractChange)]);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReportModifyIntentsAsync_PayloadContainsFilesList()
    {
        await _sut.ReportModifyIntentsAsync("w1", "captain", [
            MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange),
            MakeIntent("src/Abstractions/IBar.cs", ModifyIntent.ContractChange)
        ]);

        var msg = _mailbox.SentMessages[0];
        msg.Content.Should().Contain("IFoo.cs");
        msg.Content.Should().Contain("IBar.cs");
        msg.Content.Should().Contain("w1");
    }
}

internal sealed class FakeMailbox : IMailbox
{
    public List<CoordinatorMessage> SentMessages { get; } = [];

    public void RegisterAgent(string agentId, string? sessionId = null) { }
    public void UnregisterAgent(string agentId) { }
    public Task<bool> SendAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        return Task.FromResult(true);
    }
    public Task BroadcastAsync(CoordinatorMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public IAsyncEnumerable<CoordinatorMessage> ReceiveAsync(string agentId, CancellationToken cancellationToken = default) => AsyncEnumerable.Empty<CoordinatorMessage>();
    public IReadOnlyCollection<string> GetRegisteredAgents() => [];
    public string? GetSessionId(string agentId) => null;
}
