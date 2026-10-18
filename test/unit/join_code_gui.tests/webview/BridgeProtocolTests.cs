namespace JoinCode.Gui.Tests.WebView;

/// <summary>
/// WebUI 桥接协议测试（ADR 0141）：
/// ① 全消息类型 C# record → JSON → record 往返等值（防双侧字段漂移）；
/// ② 判别字段 "type" 存在且取值与 [JsonDerivedType] 注册一致；
/// ③ 未知 type / 非法 JSON 的 TryParse fail-closed（返回 false 不抛出）。
/// </summary>
public sealed class BridgeProtocolTests {
    public static TheoryData<BridgeMessage> AllMessages => new() {
        new WebReadyMessage(),
        new ChatSendMessage("帮我重构这个函数"),
        new PermissionResponseMessage("req-1", true),
        new SmokePongMessage("nonce-abc"),
        new AppInitMessage("dark", "0.2.0-rc.2"),
        new ChatDeltaMessage("文本增量"),
        new ChatDoneMessage(false),
        new ChatDoneMessage(true),
        new ChatStopMessage(),
        new PermissionRequestMessage("req-1", "bash", "允许在当前目录执行 shell 命令？"),
        new SmokePingMessage("nonce-abc"),
        new AppErrorMessage("未知消息类型", "检查 webui/js/bridge.js 的 type 注册"),
        new SessionsListMessage([new SessionSummaryDto("s1", "重构桥接层"), new SessionSummaryDto("s2", "s2")]),
        new SessionSelectMessage("s1"),
        new WebSessionsRenderedMessage(2),
        new ChatHistoryMessage([new ChatHistoryEntry("user", "你好", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local)), new ChatHistoryEntry("assistant", "你好！有什么可以帮你的？", new DateTime(2026, 1, 1, 12, 0, 30, DateTimeKind.Local))]),
        new ChatHistoryMessage([]),
    };

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void Roundtrip_SerializeThenTryParse_EqualToOriginal(BridgeMessage message) {
        var json = BridgeProtocol.Serialize(message);
        BridgeProtocol.TryParse(json, out var parsed).Should().BeTrue($"消息 {message.GetType().Name} 应可解析");
        if (parsed is null) {
            throw new InvalidOperationException($"TryParse 返回 true 但 parsed 为 null（协议实现违反 NotNullWhen 契约）：{message.GetType().Name}");
        }
        AssertEquivalent(message, parsed);
    }

    /// <summary>结构等价：含集合字段的消息逐元素比较（record 相等性对集合是引用比较，roundtrip 后必然不同）</summary>
    private static void AssertEquivalent(BridgeMessage expected, BridgeMessage actual) {
        switch (expected) {
            case SessionsListMessage sessions:
                actual.Should().BeOfType<SessionsListMessage>().Subject.Sessions.Should().Equal(sessions.Sessions, "会话摘要须逐项相等");
                return;
            case ChatHistoryMessage history:
                actual.Should().BeOfType<ChatHistoryMessage>().Subject.Messages.Should().Equal(history.Messages, "历史条目须逐项相等");
                return;
            default:
                actual.Should().Be(expected, "往返后应等值（record 结构相等）");
                return;
        }
    }

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void Serialize_ContainsTypeDiscriminator(BridgeMessage message) {
        BridgeProtocol.Serialize(message).Should().Contain("\"type\":\"", "判别字段必须存在且为 camelCase");
    }

    [Fact]
    public void Serialize_TypeValues_MatchCamelCasePredicate() {
        BridgeProtocol.Serialize(new ChatSendMessage("x")).Should().Contain("\"type\":\"chat/send\"");
        BridgeProtocol.Serialize(new ChatStopMessage()).Should().Contain("\"type\":\"chat/stop\"");
        BridgeProtocol.Serialize(new SmokePingMessage("x")).Should().Contain("\"type\":\"smoke/ping\"");
        BridgeProtocol.Serialize(new WebReadyMessage()).Should().Contain("\"type\":\"web/ready\"");
    }

    [Fact]
    public void ChatDone_StoppedFlag_RoundtripPreservesValue() {
        BridgeProtocol.TryParse(BridgeProtocol.Serialize(new ChatDoneMessage(true)), out var stopped)
            .Should().BeTrue("消息应可解析");
        stopped.Should().BeOfType<ChatDoneMessage>().Subject.Stopped.Should().BeTrue("用户停止标记须往返保真（Web 侧据此渲染已停止）");

        BridgeProtocol.TryParse(BridgeProtocol.Serialize(new ChatDoneMessage(false)), out var completed)
            .Should().BeTrue("消息应可解析");
        completed.Should().BeOfType<ChatDoneMessage>().Subject.Stopped.Should().BeFalse("正常完成不得误标停止");
    }

    [Fact]
    public void ChatHistoryEntry_Roundtrip_PreservesTimestamp() {
        var timestamp = new DateTime(2026, 1, 2, 22, 15, 30, DateTimeKind.Local);
        var message = new ChatHistoryMessage([new ChatHistoryEntry("user", "你好", timestamp), new ChatHistoryEntry("assistant", "在的", timestamp.AddSeconds(5))]);
        BridgeProtocol.TryParse(BridgeProtocol.Serialize(message), out var parsed).Should().BeTrue("消息应可解析");
        var entries = parsed.Should().BeOfType<ChatHistoryMessage>().Subject.Messages;
        entries[0].Timestamp.Should().Be(timestamp, "时间戳须往返保真（Web 侧据此渲染 HH:mm）");
        entries[1].Timestamp.Should().Be(timestamp.AddSeconds(5));
    }

    [Fact]
    public void TryParse_UnknownType_ReturnsFalse() {        BridgeProtocol.TryParse("""{"type":"evil/msg","x":1}""", out var parsed).Should().BeFalse("未知 type 必须 fail-closed");
        parsed.Should().BeNull();
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsFalse() {
        BridgeProtocol.TryParse("not json at all", out var parsed).Should().BeFalse("非法 JSON 不得抛出");
        parsed.Should().BeNull();
    }
}
