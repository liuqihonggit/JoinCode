namespace Infra.Tests.HotSpot;

using Infrastructure.HotSpot;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;

public sealed class ContractChangeBroadcasterTests
{
    private readonly HotFileDetector _detector = new();
    private readonly FakeMailbox _mailbox = new();
    private readonly IContractChangeBroadcaster _sut;

    public ContractChangeBroadcasterTests()
    {
        _sut = new ContractChangeBroadcaster(_detector, _mailbox);
    }

    [Fact]
    public async Task Broadcast_HotFileChange_ShouldNotifyAllDependentWorkers()
    {
        var sent = await _sut.BroadcastContractChangeAsync("captain", "src/Abstractions/IFoo.cs", ["w1", "w2", "w3"]);

        sent.Should().Be(3);
        _mailbox.SentMessages.Should().HaveCount(3);
        _mailbox.SentMessages.Select(m => m.ToAgentId).Should().BeEquivalentTo(["w1", "w2", "w3"]);
    }

    [Fact]
    public async Task Broadcast_NormalFileChange_ShouldNotNotify()
    {
        var sent = await _sut.BroadcastContractChangeAsync("captain", "src/utils/helper.cs", ["w1", "w2"]);

        sent.Should().Be(0, "非热文件不广播");
        _mailbox.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Broadcast_EmptyWorkerList_ShouldNotNotify()
    {
        var sent = await _sut.BroadcastContractChangeAsync("captain", "src/Abstractions/IFoo.cs", []);

        sent.Should().Be(0);
        _mailbox.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Broadcast_DuplicateWorkers_ShouldDeduplicate()
    {
        var sent = await _sut.BroadcastContractChangeAsync("captain", "src/Abstractions/IFoo.cs", ["w1", "w1", "w2", "w2"]);

        sent.Should().Be(2, "重复Worker去重");
        _mailbox.SentMessages.Should().HaveCount(2);
    }

    [Fact]
    public async Task Broadcast_MessageContent_ShouldContainFilePathAndCaptain()
    {
        await _sut.BroadcastContractChangeAsync("captain", "src/Abstractions/IFoo.cs", ["w1"]);

        var msg = _mailbox.SentMessages[0];
        msg.FromAgentId.Should().Be("captain");
        msg.ToAgentId.Should().Be("w1");
        msg.StructuredType.Should().Be(TeammateMessageType.ContractChanged);
        msg.Content.Should().Contain("IFoo.cs");
        msg.Content.Should().Contain("captain");
        msg.Content.Should().Contain("git pull");
    }

    [Fact]
    public async Task Broadcast_NullCaptainId_ShouldThrow()
    {
        var act = () => _sut.BroadcastContractChangeAsync("", "src/Abstractions/IFoo.cs", ["w1"]);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
