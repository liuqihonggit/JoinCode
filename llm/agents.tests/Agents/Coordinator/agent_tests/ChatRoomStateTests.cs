namespace Core.Tests.Agents.Coordinator;

public sealed class ChatRoomStateTests {
    private static ChatRoomState CreateState(int maxMessageCount = 1000) {
        return new ChatRoomState {
            Info = new TeamInfo {
                TeamId = "team_test",
                TeamName = "测试群",
            },
            MaxMessageCount = maxMessageCount,
        };
    }

    private static TeamMessage CreateMessage(string messageId, DateTime? timestamp = null, MessageVisibility visibility = MessageVisibility.Public) {
        return new TeamMessage {
            MessageId = messageId,
            TeamId = "team_test",
            SenderId = "sender",
            Content = $"消息 {messageId}",
            MessageType = "text",
            Timestamp = timestamp ?? DateTime.UtcNow,
            Visibility = visibility,
        };
    }

    [Fact]
    public void AddMessage_NewMessage_ReturnsTrue() {
        var state = CreateState();
        var msg = CreateMessage("msg1");

        var (newState, added) = state.TryAddMessage(msg);

        added.Should().BeTrue();
        newState.MessageCount.Should().Be(1);
    }

    [Fact]
    public void AddMessage_DuplicateMessageId_ReturnsFalse() {
        var state = CreateState();
        var msg = CreateMessage("msg1");

        state = state.TryAddMessage(msg).State;
        var (newState, added) = state.TryAddMessage(msg);

        added.Should().BeFalse();
        newState.MessageCount.Should().Be(1);
    }

    [Fact]
    public void NeedsCleanup_BelowMax_ReturnsFalse() {
        var state = CreateState(maxMessageCount: 5);
        for (var i = 0; i < 5; i++) {
            state = state.TryAddMessage(CreateMessage($"msg{i}")).State;
        }

        state.NeedsCleanup.Should().BeFalse();
    }

    [Fact]
    public void NeedsCleanup_AboveMax_ReturnsTrue() {
        var state = CreateState(maxMessageCount: 5);
        for (var i = 0; i < 6; i++) {
            state = state.TryAddMessage(CreateMessage($"msg{i}")).State;
        }

        state.NeedsCleanup.Should().BeTrue();
    }

    [Fact]
    public void CleanupOldMessages_RemovesOldestMessages() {
        var baseTime = new DateTime(2026, 1, 1);
        var state = CreateState(maxMessageCount: 3);
        for (var i = 0; i < 5; i++) {
            state = state.TryAddMessage(CreateMessage($"msg{i}", baseTime.AddMinutes(i))).State;
        }

        var (newState, removed) = state.CleanupOldMessages();

        removed.Should().Be(2);
        newState.MessageCount.Should().Be(3);
        newState.Messages.Should().NotContainKey("msg0");
        newState.Messages.Should().NotContainKey("msg1");
        newState.Messages.Should().ContainKey("msg2");
        newState.Messages.Should().ContainKey("msg3");
        newState.Messages.Should().ContainKey("msg4");
    }

    [Fact]
    public void CleanupOldMessages_BelowMax_ReturnsZero() {
        var state = CreateState(maxMessageCount: 10);
        state = state.TryAddMessage(CreateMessage("msg1")).State;

        var (_, removed) = state.CleanupOldMessages();

        removed.Should().Be(0);
    }

    [Fact]
    public void GetMessages_ReturnsByTimestampDescending() {
        var baseTime = new DateTime(2026, 1, 1);
        var state = CreateState();
        state = state.TryAddMessage(CreateMessage("old", baseTime)).State;
        state = state.TryAddMessage(CreateMessage("new", baseTime.AddHours(1))).State;

        var messages = state.GetMessages();

        messages[0].MessageId.Should().Be("new");
        messages[1].MessageId.Should().Be("old");
    }

    [Fact]
    public void GetMessages_WithLimit_ReturnsLimitedCount() {
        var state = CreateState();
        for (var i = 0; i < 10; i++) {
            state = state.TryAddMessage(CreateMessage($"msg{i}", DateTime.UtcNow.AddMinutes(i))).State;
        }

        var messages = state.GetMessages(limit: 3);

        messages.Should().HaveCount(3);
    }

    [Fact]
    public void GetMessages_WithVisibility_FiltersByVisibility() {
        var state = CreateState();
        state = state.TryAddMessage(CreateMessage("public1", visibility: MessageVisibility.Public)).State;
        state = state.TryAddMessage(CreateMessage("system1", visibility: MessageVisibility.System)).State;
        state = state.TryAddMessage(CreateMessage("admin1", visibility: MessageVisibility.AdminOnly)).State;

        var publicMsgs = state.GetMessages(MessageVisibility.Public);
        var systemMsgs = state.GetMessages(MessageVisibility.System);
        var adminMsgs = state.GetMessages(MessageVisibility.AdminOnly);

        publicMsgs.Should().ContainSingle(m => m.MessageId == "public1");
        systemMsgs.Should().ContainSingle(m => m.MessageId == "system1");
        adminMsgs.Should().ContainSingle(m => m.MessageId == "admin1");
    }

    [Fact]
    public void LastMessageAt_NoMessages_ReturnsNull() {
        var state = CreateState();

        state.LastMessageAt.Should().BeNull();
    }

    [Fact]
    public void LastMessageAt_WithMessages_ReturnsLatestTimestamp() {
        var baseTime = new DateTime(2026, 1, 1);
        var state = CreateState();
        state = state.TryAddMessage(CreateMessage("old", baseTime)).State;
        state = state.TryAddMessage(CreateMessage("new", baseTime.AddHours(2))).State;
        state = state.TryAddMessage(CreateMessage("mid", baseTime.AddHours(1))).State;

        state.LastMessageAt.Should().Be(baseTime.AddHours(2));
    }
}
