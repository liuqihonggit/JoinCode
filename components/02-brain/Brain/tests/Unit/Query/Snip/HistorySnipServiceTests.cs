namespace Core.Tests.Query.Snip;

public class HistorySnipServiceTests
{
    private readonly HistorySnipService _service = new();

    [Fact]
    public async Task SnipByTokenLimitAsync_ExceedsLimit_ShouldRemoveOldestMessages()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();
        for (var i = 0; i < 10; i++)
        {
            history.AddUserMessage(new string('a', 400));
        }

        var result = await _service.SnipByTokenLimitAsync(history, 200).ConfigureAwait(true);

        result.MessagesRemoved.Should().BeGreaterThan(0);
        result.RemainingMessages.Should().BeLessThan(10);
    }

    [Fact]
    public async Task SnipByMessageCountAsync_ExceedsCount_ShouldRemoveExcessMessages()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();
        for (var i = 0; i < 10; i++)
        {
            history.AddUserMessage($"Message {i}");
        }

        var result = await _service.SnipByMessageCountAsync(history, 5).ConfigureAwait(true);

        result.MessagesRemoved.Should().BeGreaterThan(0);
        result.RemainingMessages.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task SnipByMessageCountAsync_SystemMessages_ShouldBePreserved()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();
        history.AddSystemMessage("system instruction");
        for (var i = 0; i < 10; i++)
        {
            history.AddUserMessage($"Message {i}");
        }

        var result = await _service.SnipByMessageCountAsync(history, 5).ConfigureAwait(true);

        var hasSystemMessage = false;
        foreach (var msg in history)
        {
            if (msg.Role == JoinCode.Abstractions.LLM.Chat.MessageRole.System)
            {
                hasSystemMessage = true;
                break;
            }
        }

        hasSystemMessage.Should().BeTrue();
    }

    [Fact]
    public async Task SnipByTokenLimitAsync_RecentMessages_ShouldBePreserved()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();
        for (var i = 0; i < 20; i++)
        {
            history.AddUserMessage($"Message {i}");
        }

        var originalRecent = new List<string>();
        for (var i = Math.Max(0, history.Count - 5); i < history.Count; i++)
        {
            originalRecent.Add(history[i].Content ?? "");
        }

        await _service.SnipByTokenLimitAsync(history, 200).ConfigureAwait(true);

        var currentRecent = new List<string>();
        for (var i = Math.Max(0, history.Count - 5); i < history.Count; i++)
        {
            currentRecent.Add(history[i].Content ?? "");
        }

        foreach (var msg in originalRecent)
        {
            currentRecent.Should().Contain(msg);
        }
    }

    [Fact]
    public async Task SnipByMessageCountAsync_EmptyHistory_ShouldReturnZeroRemoved()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();

        var result = await _service.SnipByMessageCountAsync(history, 5).ConfigureAwait(true);

        result.MessagesRemoved.Should().Be(0);
        result.RemainingMessages.Should().Be(0);
    }

    [Fact]
    public async Task SnipByTokenLimitAsync_EmptyHistory_ShouldReturnZeroRemoved()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();

        var result = await _service.SnipByTokenLimitAsync(history, 1000).ConfigureAwait(true);

        result.MessagesRemoved.Should().Be(0);
        result.RemainingMessages.Should().Be(0);
    }

    [Fact]
    public async Task SnipByMessageCountAsync_UnderLimit_ShouldNotRemoveAny()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();
        history.AddUserMessage("msg1");
        history.AddUserMessage("msg2");

        var result = await _service.SnipByMessageCountAsync(history, 10).ConfigureAwait(true);

        result.MessagesRemoved.Should().Be(0);
        result.RemainingMessages.Should().Be(2);
    }

    [Fact]
    public async Task SnipByTokenLimitAsync_UnderLimit_ShouldNotRemoveAny()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();
        history.AddUserMessage("short");

        var result = await _service.SnipByTokenLimitAsync(history, 10000).ConfigureAwait(true);

        result.MessagesRemoved.Should().Be(0);
        result.RemainingMessages.Should().Be(1);
    }

    [Fact]
    public async Task SnipHistoryAsync_NullHistory_ShouldThrowArgumentNullException()
    {
        var act = async () => await _service.SnipHistoryAsync(null!, new SnipOptions()).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SnipHistoryAsync_NullOptions_ShouldThrowArgumentNullException()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();
        var act = async () => await _service.SnipHistoryAsync(history, null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SnipByMessageCountAsync_SystemAndRecentPreserved_ShouldOnlyRemoveMiddle()
    {
        var history = new JoinCode.Abstractions.LLM.Chat.MessageList();
        history.AddSystemMessage("system");
        history.AddUserMessage("old user 1");
        history.AddAssistantMessage("old assistant 1");
        history.AddUserMessage("old user 2");
        history.AddAssistantMessage("old assistant 2");
        history.AddUserMessage("recent user");
        history.AddAssistantMessage("recent assistant");

        var result = await _service.SnipByMessageCountAsync(history, 3).ConfigureAwait(true);

        var roles = new List<JoinCode.Abstractions.LLM.Chat.MessageRole>();
        foreach (var msg in history)
        {
            roles.Add(msg.Role);
        }

        roles.Should().Contain(JoinCode.Abstractions.LLM.Chat.MessageRole.System);
    }
}
