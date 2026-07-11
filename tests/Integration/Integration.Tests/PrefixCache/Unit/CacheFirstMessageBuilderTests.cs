namespace Integration.Tests.PrefixCache.Unit;

public sealed class CacheFirstMessageBuilderTests
{
    [Fact]
    public void BuildMessages_FollowsStrictOrder_PrefixLogPendingUser()
    {
        var prefix = new ImmutablePrefix("System prompt", [],
        [
            new ApiMessage(MessageRole.User, "example"),
            new ApiMessage(MessageRole.Assistant, "response")
        ]);

        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "History Q1"));
        log.Append(new ApiMessage(MessageRole.Assistant, "History A1"));

        const string pendingUser = "Current question";

        var messages = CacheFirstMessageBuilder.BuildMessages(prefix, log, pendingUser);

        messages.Should().HaveCount(6);

        messages[0].Role.Should().Be(MessageRole.System);
        messages[0].Content.Should().Be("System prompt");

        messages[1].Role.Should().Be(MessageRole.User);
        messages[1].Content.Should().Be("example");

        messages[2].Role.Should().Be(MessageRole.Assistant);
        messages[2].Content.Should().Be("response");

        messages[3].Role.Should().Be(MessageRole.User);
        messages[3].Content.Should().Be("History Q1");

        messages[4].Role.Should().Be(MessageRole.Assistant);
        messages[4].Content.Should().Be("History A1");

        messages[5].Role.Should().Be(MessageRole.User);
        messages[5].Content.Should().Be("Current question");
    }

    [Fact]
    public void BuildMessages_WithoutPendingUser_ExcludesLastUserMessage()
    {
        var prefix = new ImmutablePrefix("System", [], []);
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "Q1"));

        var messages = CacheFirstMessageBuilder.BuildMessages(prefix, log, null);

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be(MessageRole.System);
        messages[1].Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public void BuildMessages_PrefixStableAcrossMultipleCalls()
    {
        var prefix = new ImmutablePrefix("System prompt", [], []);
        var log1 = new AppendOnlyLog();
        log1.Append(new ApiMessage(MessageRole.User, "Q1"));

        var log2 = new AppendOnlyLog();
        log2.Append(new ApiMessage(MessageRole.User, "Q1"));
        log2.Append(new ApiMessage(MessageRole.Assistant, "A1"));
        log2.Append(new ApiMessage(MessageRole.User, "Q2"));

        var msgs1 = CacheFirstMessageBuilder.BuildMessages(prefix, log1, null);
        var msgs2 = CacheFirstMessageBuilder.BuildMessages(prefix, log2, null);

        msgs1[0].Content.Should().Be(msgs2[0].Content,
            "system prompt must be identical across turns");

        msgs1[1].Content.Should().Be(msgs2[1].Content,
            "first history message must be identical across turns");
    }

    [Fact]
    public void BuildMessages_WithDynamicContext_InjectsAfterStaticPrefix()
    {
        var prefix = new ImmutablePrefix("System prompt", [], []);
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "Q1"));

        const string dynamicContext = "Language: zh-CN\nMemory: some content";

        var messages = CacheFirstMessageBuilder.BuildMessages(prefix, log, "Hello", dynamicContext);

        messages[0].Role.Should().Be(MessageRole.System);
        messages[0].Content.Should().Be("System prompt");

        messages[1].Role.Should().Be(MessageRole.System, "dynamic context is a system message with CacheBreak");
        messages[1].Content.Should().Contain("Language: zh-CN");
        messages[1].Metadata.Should().ContainKey("CacheBreak");
        messages[1].Metadata!["CacheBreak"].GetBoolean().Should().Be(true);

        messages[2].Role.Should().Be(MessageRole.User);
        messages[2].Content.Should().Be("Q1");
    }

    [Fact]
    public void BuildMessages_DynamicContextChange_DoesNotAffectStaticPrefix()
    {
        var prefix = new ImmutablePrefix("System prompt", [], []);
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "Q1"));

        var msgs1 = CacheFirstMessageBuilder.BuildMessages(prefix, log, "Hello", "Dynamic v1");
        var msgs2 = CacheFirstMessageBuilder.BuildMessages(prefix, log, "Hello", "Dynamic v2");

        msgs1[0].Content.Should().Be(msgs2[0].Content,
            "static system prompt must be identical regardless of dynamic context changes");
    }

    [Fact]
    public void BuildMessages_DynamicContextUnchanged_NoCacheBreak()
    {
        var prefix = new ImmutablePrefix("System prompt", [], []);
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "Q1"));

        var messages = CacheFirstMessageBuilder.BuildMessages(prefix, log, "Hello", "Dynamic context", dynamicContextChanged: false);

        messages[1].Role.Should().Be(MessageRole.System);
        messages[1].Content.Should().Contain("Dynamic context");
        messages[1].Metadata.Should().BeNull("unchanged dynamic context should not have CacheBreak metadata");
    }

    [Fact]
    public void BuildMessages_DynamicContextChanged_HasCacheBreak()
    {
        var prefix = new ImmutablePrefix("System prompt", [], []);
        var log = new AppendOnlyLog();
        log.Append(new ApiMessage(MessageRole.User, "Q1"));

        var messages = CacheFirstMessageBuilder.BuildMessages(prefix, log, "Hello", "Dynamic context", dynamicContextChanged: true);

        messages[1].Role.Should().Be(MessageRole.System);
        messages[1].Metadata.Should().ContainKey("CacheBreak");
        messages[1].Metadata!["CacheBreak"].GetBoolean().Should().Be(true);
    }
}
