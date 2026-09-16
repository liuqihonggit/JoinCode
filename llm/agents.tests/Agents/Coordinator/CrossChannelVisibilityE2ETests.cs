namespace Core.Tests.Agents.Coordinator;

/// <summary>
/// 跨通道可见性路由 E2E 测试 — 验证 InProcess + NamedPipe(用 InProcessMailbox 模拟) 混合成员下，
/// AdminOnly/Private/Hidden/Public 可见性正确路由 — ADR 0109 决策8 + ADR 0111 决策6。
/// </summary>
public sealed class CrossChannelVisibilityE2ETests
{
    private static async Task<List<CoordinatorMessage>> ReceiveWithTimeoutAsync(
        IAsyncEnumerable<CoordinatorMessage> stream, int timeoutMs = 500)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var result = new List<CoordinatorMessage>();
        try
        {
            await foreach (var msg in stream.WithCancellation(cts.Token))
            {
                result.Add(msg);
                if (result.Count >= 1) break;
            }
        }
        catch (OperationCanceledException) { }
        return result;
    }

    private static async Task SetupHubAsync(
        MailboxHub hub, InProcessMailbox inProcess, InProcessMailbox namedPipe,
        params (string AgentId, MailboxKind Kind, ChatRoomRole Role)[] agents)
    {
        foreach (var (agentId, kind, role) in agents)
        {
            if (kind == MailboxKind.InProcess)
                await inProcess.RegisterAgentAsync(agentId);
            else
                await namedPipe.RegisterAgentAsync(agentId);
            await hub.RegisterAgentAsync(agentId, kind, role: role);
        }
        await Task.Delay(100);
    }

    [Fact]
    public async Task AdminOnly_MixedChannels_OnlyAdminsOnBothChannelsReceive()
    {
        await using var inProcessMailbox = new InProcessMailbox();
        await using var namedPipeMailbox = new InProcessMailbox();

        var hub = new MailboxHub(inProcessMailbox);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);

        await SetupHubAsync(hub, inProcessMailbox, namedPipeMailbox,
            ("admin_inproc", MailboxKind.InProcess, ChatRoomRole.Admin),
            ("member_inproc", MailboxKind.InProcess, ChatRoomRole.Member),
            ("admin_pipe", MailboxKind.NamedPipe, ChatRoomRole.Owner),
            ("member_pipe", MailboxKind.NamedPipe, ChatRoomRole.Member));

        var message = new CoordinatorMessage
        {
            FromAgentId = "sender",
            ToAgentId = "broadcast",
            MessageType = "text",
            Content = "管理员通知",
            Visibility = MessageVisibility.AdminOnly,
        };

        await hub.BroadcastAsync(message, MessageVisibility.AdminOnly);
        await Task.Delay(200);

        var adminInProcMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("admin_inproc", CancellationToken.None));
        var adminPipeMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("admin_pipe", CancellationToken.None));
        var memberInProcMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("member_inproc", CancellationToken.None));
        var memberPipeMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("member_pipe", CancellationToken.None));

        adminInProcMsgs.Should().HaveCount(1);
        adminInProcMsgs[0].Content.Should().Be("管理员通知");
        adminPipeMsgs.Should().HaveCount(1);
        adminPipeMsgs[0].Content.Should().Be("管理员通知");
        memberInProcMsgs.Should().BeEmpty();
        memberPipeMsgs.Should().BeEmpty();
    }

    [Fact]
    public async Task Private_MixedChannels_OnlyTargetAgentReceives()
    {
        await using var inProcessMailbox = new InProcessMailbox();
        await using var namedPipeMailbox = new InProcessMailbox();

        var hub = new MailboxHub(inProcessMailbox);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);

        await SetupHubAsync(hub, inProcessMailbox, namedPipeMailbox,
            ("agent_a", MailboxKind.InProcess, ChatRoomRole.Member),
            ("agent_b", MailboxKind.NamedPipe, ChatRoomRole.Member),
            ("agent_c", MailboxKind.InProcess, ChatRoomRole.Member));

        var message = new CoordinatorMessage
        {
            FromAgentId = "agent_a",
            ToAgentId = "agent_b",
            MessageType = "text",
            Content = "私信",
            Visibility = MessageVisibility.Private,
        };

        await hub.BroadcastAsync(message, MessageVisibility.Private);
        await Task.Delay(200);

        var agentBMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("agent_b", CancellationToken.None));
        var agentCMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("agent_c", CancellationToken.None));

        agentBMsgs.Should().HaveCount(1);
        agentBMsgs[0].Content.Should().Be("私信");
        agentCMsgs.Should().BeEmpty();
    }

    [Fact]
    public async Task Hidden_MixedChannels_NoAgentReceives()
    {
        await using var inProcessMailbox = new InProcessMailbox();
        await using var namedPipeMailbox = new InProcessMailbox();

        var hub = new MailboxHub(inProcessMailbox);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);

        await SetupHubAsync(hub, inProcessMailbox, namedPipeMailbox,
            ("agent_a", MailboxKind.InProcess, ChatRoomRole.Member),
            ("agent_b", MailboxKind.NamedPipe, ChatRoomRole.Member));

        var message = new CoordinatorMessage
        {
            FromAgentId = "agent_a",
            ToAgentId = "broadcast",
            MessageType = "text",
            Content = "隐藏消息",
            Visibility = MessageVisibility.Hidden,
        };

        await hub.BroadcastAsync(message, MessageVisibility.Hidden);
        await Task.Delay(200);

        var agentAMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("agent_a", CancellationToken.None));
        var agentBMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("agent_b", CancellationToken.None));

        agentAMsgs.Should().BeEmpty();
        agentBMsgs.Should().BeEmpty();
    }

    [Fact]
    public async Task Public_MixedChannels_AllAgentsOnBothChannelsReceive()
    {
        await using var inProcessMailbox = new InProcessMailbox();
        await using var namedPipeMailbox = new InProcessMailbox();

        var hub = new MailboxHub(inProcessMailbox);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);

        await SetupHubAsync(hub, inProcessMailbox, namedPipeMailbox,
            ("agent_inproc", MailboxKind.InProcess, ChatRoomRole.Member),
            ("agent_pipe", MailboxKind.NamedPipe, ChatRoomRole.Member));

        var message = new CoordinatorMessage
        {
            FromAgentId = "sender",
            ToAgentId = "broadcast",
            MessageType = "text",
            Content = "公开广播",
            Visibility = MessageVisibility.Public,
        };

        await hub.BroadcastAsync(message, MessageVisibility.Public);
        await Task.Delay(200);

        var inProcMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("agent_inproc", CancellationToken.None));
        var pipeMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("agent_pipe", CancellationToken.None));

        inProcMsgs.Should().HaveCount(1);
        inProcMsgs[0].Content.Should().Be("公开广播");
        pipeMsgs.Should().HaveCount(1);
        pipeMsgs[0].Content.Should().Be("公开广播");
    }

    [Fact]
    public async Task SystemNotice_MixedChannels_AllAgentsReceive()
    {
        await using var inProcessMailbox = new InProcessMailbox();
        await using var namedPipeMailbox = new InProcessMailbox();

        var hub = new MailboxHub(inProcessMailbox);
        hub.RegisterChannel(MailboxKind.NamedPipe, namedPipeMailbox);

        await SetupHubAsync(hub, inProcessMailbox, namedPipeMailbox,
            ("agent_inproc", MailboxKind.InProcess, ChatRoomRole.Member),
            ("agent_pipe", MailboxKind.NamedPipe, ChatRoomRole.Member));

        var message = new CoordinatorMessage
        {
            FromAgentId = "system",
            ToAgentId = "broadcast",
            MessageType = "system_notice",
            Content = "成员加入",
            Visibility = MessageVisibility.System,
        };

        await hub.BroadcastAsync(message, MessageVisibility.System);
        await Task.Delay(200);

        var inProcMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("agent_inproc", CancellationToken.None));
        var pipeMsgs = await ReceiveWithTimeoutAsync(hub.ReceiveAsync("agent_pipe", CancellationToken.None));

        inProcMsgs.Should().HaveCount(1);
        inProcMsgs[0].Content.Should().Be("成员加入");
        pipeMsgs.Should().HaveCount(1);
        pipeMsgs[0].Content.Should().Be("成员加入");
    }
}
