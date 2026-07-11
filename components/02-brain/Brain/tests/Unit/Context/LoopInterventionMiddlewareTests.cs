namespace Core.Context;

public sealed class LoopInterventionMiddlewareTests
{
    [Fact]
    public async Task NoLoop_TransparentPassthrough()
    {
        var middleware = CreateMiddleware();
        var nextEvents = new[]
        {
            ChatStreamEvent.Text("正常输出"),
            ChatStreamEvent.Done(new TokenUsage(1, 1), "test-model")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        events.Should().HaveCount(2);
        events[0].Content.Should().Be("正常输出");
        events[1].Type.Should().Be(ChatStreamEventType.Complete);
        context.LoopTriggerCount.Should().Be(0);
    }

    [Fact]
    public async Task Level1_FirstTrigger_InjectsSoftInterventionPrompt()
    {
        var middleware = CreateMiddleware();
        var nextEvents = new[]
        {
            ChatStreamEvent.Text("一些文本"),
            ChatStreamEvent.LoopDetected(1, 10, "重复模式"),
            ChatStreamEvent.Done(new TokenUsage(1, 1), "test-model")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        events.Should().Contain(e => e.Type == ChatStreamEventType.Content && e.Content!.Contains("系统提示"));
        events.Should().Contain(e => e.Type == ChatStreamEventType.Content && e.Content == "一些文本");
        context.LoopTriggerCount.Should().Be(1);
    }

    [Fact]
    public async Task Level1_SecondTrigger_InjectsSoftInterventionPrompt()
    {
        var middleware = CreateMiddleware();
        var nextEvents = new[]
        {
            ChatStreamEvent.Text("一些文本"),
            ChatStreamEvent.LoopDetected(2, 10, "重复模式"),
            ChatStreamEvent.Done(new TokenUsage(1, 1), "test-model")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        events.Should().Contain(e => e.Type == ChatStreamEventType.Content && e.Content!.Contains("系统提示"));
        context.LoopTriggerCount.Should().Be(2);
    }

    [Fact]
    public async Task Level2_ThirdTrigger_HardTruncateAndRetry()
    {
        var queryService = new Mock<IQueryService>();
        queryService.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new[] { new StreamEvent { Content = "重连成功" } }));

        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetChatCompletionService()).Returns(queryService.Object);

        var loopDetector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 0, requiredRepeats: 3);
        var chunkProcessor = new ChatStreamChunkProcessor(loopDetector, new Mock<IChatUsageProcessor>().Object);

        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));

        var middleware = new LoopInterventionMiddleware(
            chatClient.Object, contextManager.Object, chunkProcessor,
            options: Options.Create(new LoopInterventionOptions()),
            logger: NullLogger<LoopInterventionMiddleware>.Instance);

        var nextEvents = new[]
        {
            ChatStreamEvent.Text("循环文本"),
            ChatStreamEvent.LoopDetected(3, 5, "重复模式")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        var textEvents = events.Where(e => e.Type == ChatStreamEventType.Content).Select(e => e.Content).ToList();
        textEvents.Should().Contain(c => c!.Contains("截断"));
        context.LoopTriggerCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task LoopDetectedEvent_NotForwardedToUser()
    {
        var middleware = CreateMiddleware();
        var nextEvents = new[]
        {
            ChatStreamEvent.Text("文本"),
            ChatStreamEvent.LoopDetected(1, 5, "模式"),
            ChatStreamEvent.Done(new TokenUsage(1, 1), "test-model")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        events.Should().NotContain(e => e.Type == ChatStreamEventType.LoopDetected);
    }

    [Fact]
    public async Task Level1_StreamContinuesAfterSoftIntervention()
    {
        var middleware = CreateMiddleware();
        var nextEvents = new[]
        {
            ChatStreamEvent.Text("第一段"),
            ChatStreamEvent.LoopDetected(1, 5, "模式"),
            ChatStreamEvent.Text("第二段"),
            ChatStreamEvent.Done(new TokenUsage(1, 1), "test-model")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        events.Should().Contain(e => e.Type == ChatStreamEventType.Content && e.Content == "第一段");
        events.Should().Contain(e => e.Type == ChatStreamEventType.Content && e.Content == "第二段");
        events.Should().Contain(e => e.Type == ChatStreamEventType.Complete);
    }

    [Fact]
    public async Task Level2_HardTruncate_StopsForwardingEvents()
    {
        var queryService = new Mock<IQueryService>();
        queryService.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new[] { new StreamEvent { Content = "重连成功" } }));

        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetChatCompletionService()).Returns(queryService.Object);

        var loopDetector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 0, requiredRepeats: 3);
        var chunkProcessor = new ChatStreamChunkProcessor(loopDetector, new Mock<IChatUsageProcessor>().Object);

        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));

        var middleware = new LoopInterventionMiddleware(
            chatClient.Object, contextManager.Object, chunkProcessor,
            progressTracker: null,
            options: Options.Create(new LoopInterventionOptions()),
            logger: NullLogger<LoopInterventionMiddleware>.Instance);

        var nextEvents = new[]
        {
            ChatStreamEvent.Text("循环文本"),
            ChatStreamEvent.LoopDetected(3, 5, "模式"),
            ChatStreamEvent.Text("这段不应该出现"),
            ChatStreamEvent.Done(new TokenUsage(1, 1), "test-model")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        events.Should().NotContain(e => e.Type == ChatStreamEventType.Content && e.Content == "这段不应该出现");
    }

    [Fact]
    public async Task TaskProgressed_Level2DowngradedToLevel1()
    {
        var progressTracker = new StubTaskProgressTracker(hasProgressed: true, completedCount: 5);

        var middleware = CreateMiddleware(progressTracker: progressTracker);
        var nextEvents = new[]
        {
            ChatStreamEvent.Text("循环文本"),
            ChatStreamEvent.LoopDetected(3, 5, "重复模式"),
            ChatStreamEvent.Text("后续文本"),
            ChatStreamEvent.Done(new TokenUsage(1, 1), "test-model")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        events.Should().Contain(e => e.Type == ChatStreamEventType.Content && e.Content!.Contains("系统提示"));
        events.Should().Contain(e => e.Type == ChatStreamEventType.Content && e.Content == "后续文本");
        context.HasTaskProgressed.Should().BeTrue();
        context.CurrentCompletedTodoCount.Should().Be(5);
    }

    [Fact]
    public async Task TaskNotProgressed_Level2RemainsLevel2()
    {
        var queryService = new Mock<IQueryService>();
        queryService.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new[] { new StreamEvent { Content = "重连成功" } }));

        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetChatCompletionService()).Returns(queryService.Object);

        var loopDetector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 0, requiredRepeats: 3);
        var chunkProcessor = new ChatStreamChunkProcessor(loopDetector, new Mock<IChatUsageProcessor>().Object);

        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));

        var progressTracker = new StubTaskProgressTracker(hasProgressed: false, completedCount: 2);

        var middleware = new LoopInterventionMiddleware(
            chatClient.Object, contextManager.Object, chunkProcessor,
            progressTracker: progressTracker,
            options: Options.Create(new LoopInterventionOptions()),
            logger: NullLogger<LoopInterventionMiddleware>.Instance);

        var nextEvents = new[]
        {
            ChatStreamEvent.Text("循环文本"),
            ChatStreamEvent.LoopDetected(3, 5, "重复模式")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        var textEvents = events.Where(e => e.Type == ChatStreamEventType.Content).Select(e => e.Content).ToList();
        textEvents.Should().Contain(c => c!.Contains("截断"));
        context.HasTaskProgressed.Should().BeFalse();
    }

    [Fact]
    public async Task NoProgressTracker_BehavesAsBefore()
    {
        var middleware = CreateMiddleware();
        var nextEvents = new[]
        {
            ChatStreamEvent.Text("一些文本"),
            ChatStreamEvent.LoopDetected(1, 10, "重复模式"),
            ChatStreamEvent.Done(new TokenUsage(1, 1), "test-model")
        };

        var context = CreateContext();
        var events = await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        events.Should().Contain(e => e.Type == ChatStreamEventType.Content && e.Content!.Contains("系统提示"));
        context.HasTaskProgressed.Should().BeFalse();
    }

    [Fact]
    public async Task Level2_Rewind_InsertsAuditMark()
    {
        var queryService = new Mock<IQueryService>();
        queryService.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new[] { new StreamEvent { Content = "重连成功" } }));

        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetChatCompletionService()).Returns(queryService.Object);

        var loopDetector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 0, requiredRepeats: 3);
        var chunkProcessor = new ChatStreamChunkProcessor(loopDetector, new Mock<IChatUsageProcessor>().Object);

        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));

        var middleware = new LoopInterventionMiddleware(
            chatClient.Object, contextManager.Object, chunkProcessor,
            options: Options.Create(new LoopInterventionOptions()),
            logger: NullLogger<LoopInterventionMiddleware>.Instance);

        var nextEvents = new[]
        {
            ChatStreamEvent.Text("循环文本"),
            ChatStreamEvent.LoopDetected(3, 5, "重复模式")
        };

        var context = CreateContext();
        await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        contextManager.Verify(c => c.AddSystemMessageAsync(
            It.Is<string>(s => s.Contains("系统撤回") && s.Contains("循环检测")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Level2_Rewind_AuditMarkDisabled_NoInsert()
    {
        var queryService = new Mock<IQueryService>();
        queryService.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new[] { new StreamEvent { Content = "重连成功" } }));

        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetChatCompletionService()).Returns(queryService.Object);

        var loopDetector = new OutputLoopDetector(minPatternLength: 5, checkInterval: 1, cooldownChars: 0, requiredRepeats: 3);
        var chunkProcessor = new ChatStreamChunkProcessor(loopDetector, new Mock<IChatUsageProcessor>().Object);

        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));

        var options = LoopInterventionOptionsBuilder.Create()
            .WithInsertRewindAuditMark(false)
            .WithCompactThreshold(10)
            .Build();

        var middleware = new LoopInterventionMiddleware(
            chatClient.Object, contextManager.Object, chunkProcessor,
            options: Options.Create(options),
            logger: NullLogger<LoopInterventionMiddleware>.Instance);

        var nextEvents = new[]
        {
            ChatStreamEvent.Text("循环文本"),
            ChatStreamEvent.LoopDetected(5, 5, "重复模式")
        };

        var context = CreateContext();
        await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        contextManager.Verify(c => c.AddSystemMessageAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Level3_Reset_PreservesLastUserMessage()
    {
        var contextManager = new Mock<IChatContextManager>();
        var userMessages = new MessageList
        {
            new ApiMessage(MessageRole.User, "帮我实现登录功能"),
            new ApiMessage(MessageRole.Assistant, "好的"),
            new ApiMessage(MessageRole.User, "请加上验证码"),
        };
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userMessages);
        contextManager.Setup(c => c.RewindToStartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.ClearHistory, 3, 0));
        contextManager.Setup(c => c.FoldIfNeededAsync(It.IsAny<ContextFoldDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextFoldResult { Folded = false, OriginalMessageCount = 3 });

        var chatClient = new Mock<IChatClient>();
        var chunkProcessor = new Mock<IChatStreamChunkProcessor>();
        chunkProcessor.Setup(c => c.CreateIterationState())
            .Returns(new IterationState());

        var middleware = new LoopInterventionMiddleware(
            chatClient.Object, contextManager.Object, chunkProcessor.Object,
            options: Options.Create(new LoopInterventionOptions()),
            logger: NullLogger<LoopInterventionMiddleware>.Instance);

        var nextEvents = new[]
        {
            ChatStreamEvent.Text("循环文本"),
            ChatStreamEvent.LoopDetected(5, 5, "重复模式")
        };

        var context = CreateContext();
        await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        contextManager.Verify(c => c.RewindToStartAsync(It.IsAny<CancellationToken>()), Times.Once);
        contextManager.Verify(c => c.AddUserMessageAsync("请加上验证码", It.IsAny<CancellationToken>()), Times.Once);
        contextManager.Verify(c => c.AddSystemMessageAsync(
            It.Is<string>(s => s.Contains("循环检测已重置")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Level3_Reset_NoUserMessage_FullReset()
    {
        var contextManager = new Mock<IChatContextManager>();
        var messages = new MessageList
        {
            new ApiMessage(MessageRole.System, "系统提示"),
            new ApiMessage(MessageRole.Assistant, "好的"),
        };
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);
        contextManager.Setup(c => c.RewindToStartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.ClearHistory, 2, 0));
        contextManager.Setup(c => c.FoldIfNeededAsync(It.IsAny<ContextFoldDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextFoldResult { Folded = false, OriginalMessageCount = 2 });

        var chatClient = new Mock<IChatClient>();
        var chunkProcessor = new Mock<IChatStreamChunkProcessor>();
        chunkProcessor.Setup(c => c.CreateIterationState())
            .Returns(new IterationState());

        var middleware = new LoopInterventionMiddleware(
            chatClient.Object, contextManager.Object, chunkProcessor.Object,
            options: Options.Create(new LoopInterventionOptions()),
            logger: NullLogger<LoopInterventionMiddleware>.Instance);

        var nextEvents = new[]
        {
            ChatStreamEvent.Text("循环文本"),
            ChatStreamEvent.LoopDetected(5, 5, "重复模式")
        };

        var context = CreateContext();
        await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        contextManager.Verify(c => c.RewindToStartAsync(It.IsAny<CancellationToken>()), Times.Once);
        contextManager.Verify(c => c.AddUserMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Level2_LastRetry_UsesSecondChanceTemperature()
    {
        var actualTemperatures = new List<float>();
        var queryService = new Mock<IQueryService>();
        queryService.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions?, IChatClient, CancellationToken>((_, opts, _, _) =>
            {
                if (opts is not null && opts.Temperature.HasValue) actualTemperatures.Add(opts.Temperature.Value);
            })
            .Returns(ToAsyncEnumerable(new[] { new StreamEvent { Content = "重连成功" } }));

        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetChatCompletionService()).Returns(queryService.Object);

        var chunkProcessor = new Mock<IChatStreamChunkProcessor>();
        var firstCall = true;
        chunkProcessor.Setup(c => c.CreateIterationState())
            .Returns(() =>
            {
                if (firstCall)
                {
                    firstCall = false;
                    return new IterationState();
                }
                return new IterationState();
            });

        chunkProcessor.Setup(c => c.ProcessChunk(It.IsAny<StreamEvent>(), It.IsAny<IterationState>()))
            .Returns((StreamEvent chunk, IterationState state) =>
            {
                state.FullResponse.Append(chunk.Content);
                return new StreamChunkResult
                {
                    Action = ChunkAction.Continue,
                    Events = [ChatStreamEvent.Text(chunk.Content ?? "")]
                };
            });

        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));

        var options = LoopInterventionOptionsBuilder.Create()
            .WithMaxRetryAttempts(2)
            .Build();

        var middleware = new LoopInterventionMiddleware(
            chatClient.Object, contextManager.Object, chunkProcessor.Object,
            options: Options.Create(options),
            logger: NullLogger<LoopInterventionMiddleware>.Instance);

        var nextEvents = new[]
        {
            ChatStreamEvent.Text("循环文本"),
            ChatStreamEvent.LoopDetected(3, 5, "重复模式")
        };

        var context = CreateContext();
        await CollectEventsAsync(middleware, context, nextEvents).ConfigureAwait(true);

        actualTemperatures.Should().HaveCountGreaterThanOrEqualTo(1);
        actualTemperatures[0].Should().Be(0.6f);
    }

    [Fact]
    public void Options_SecondChanceTemperature_DefaultIsLower()
    {
        var options = new LoopInterventionOptions();
        options.SecondChanceTemperature.Should().Be(0.3f);
        options.SecondChanceTemperature.Should().BeLessThan(options.RetryTemperature);
    }

    private static LoopInterventionMiddleware CreateMiddleware(
        Mock<IChatContextManager>? contextManagerMock = null,
        Mock<IChatClient>? chatClientMock = null,
        Mock<IChatStreamChunkProcessor>? chunkProcessorMock = null,
        ITaskProgressTracker? progressTracker = null)
    {
        var contextManager = contextManagerMock ?? new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));

        var chatClient = chatClientMock ?? new Mock<IChatClient>();
        var chunkProcessor = chunkProcessorMock ?? new Mock<IChatStreamChunkProcessor>();

        return new LoopInterventionMiddleware(
            chatClient.Object,
            contextManager.Object,
            chunkProcessor.Object,
            progressTracker: progressTracker,
            options: Options.Create(new LoopInterventionOptions()),
            logger: NullLogger<LoopInterventionMiddleware>.Instance);
    }

    private static ChatMiddlewareContext CreateContext() => new()
    {
        Message = "test",
        ToolUseContext = new ToolUseContext()
    };

    private static async Task<List<ChatStreamEvent>> CollectEventsAsync(
        LoopInterventionMiddleware middleware,
        ChatMiddlewareContext context,
        ChatStreamEvent[] nextEvents)
    {
        var events = new List<ChatStreamEvent>();

        await foreach (var evt in middleware.InvokeAsync(
            context,
            (ctx, ct) => ToAsyncEnumerable(nextEvents),
            CancellationToken.None))
        {
            events.Add(evt);
        }

        return events;
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
    }

    private sealed class StubTaskProgressTracker : ITaskProgressTracker
    {
        private readonly bool _hasProgressed;
        private readonly int _completedCount;

        public StubTaskProgressTracker(bool hasProgressed, int completedCount)
        {
            _hasProgressed = hasProgressed;
            _completedCount = completedCount;
        }

        public Task<int> GetCompletedTodoCountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_completedCount);

        public Task SnapshotCurrentProgressAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> HasProgressedSinceLastSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_hasProgressed);
    }
}
