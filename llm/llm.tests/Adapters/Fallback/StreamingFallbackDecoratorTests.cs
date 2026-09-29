namespace Llm.Tests.Adapters.Fallback;

public class StreamingFallbackDecoratorTests {
    private static IQueryService CreateMockStreamingService(
        bool shouldFail,
        Exception? exception = null) {
        var mock = new Mock<IQueryService>();

        if (shouldFail) {
            mock.Setup(s => s.GetStreamEventContentsAsync(
                    It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                    It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
                .Returns((MessageList h, ChatOptions? s, IChatClient? k, CancellationToken ct) =>
                    FailStreamAsync(exception ?? new HttpRequestException("Stream error"), ct));

            mock.Setup(s => s.GetApiMessageContentsAsync(
                    It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                    It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApiMessage>
                {
                    new(MessageRole.Assistant, "fallback response")
                });
        } else {
            mock.Setup(s => s.GetStreamEventContentsAsync(
                    It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                    It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
                .Returns(SucceedStreamAsync());

            mock.Setup(s => s.GetApiMessageContentsAsync(
                    It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                    It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApiMessage>
                {
                    new(MessageRole.Assistant, "non-streaming response")
                });
        }

        return mock.Object;
    }

    private static async IAsyncEnumerable<StreamEvent> SucceedStreamAsync(
        [EnumeratorCancellation] CancellationToken ct = default) {
        await Task.Yield();
        yield return new StreamEvent(MessageRole.Assistant, "hello", "test-model");
        yield return new StreamEvent(MessageRole.Assistant, " world", "test-model");
    }

    private static IAsyncEnumerable<StreamEvent> FailStreamAsync(
        Exception exception, CancellationToken ct) {
        return FailStreamCoreAsync(exception, ct);
    }

    private static async IAsyncEnumerable<StreamEvent> FailStreamCoreAsync(
        Exception exception,
        [EnumeratorCancellation] CancellationToken ct) {
        await Task.Yield();
        yield return new StreamEvent(MessageRole.Assistant, "partial", "test-model");
        throw exception;
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WhenStreamingSucceeds_ReturnsStreamEvents() {
        var inner = CreateMockStreamingService(shouldFail: false);
        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList())) {
            events.Add(evt);
        }

        events.Should().HaveCount(2);
        events[0].Content.Should().Be("hello");
        events[1].Content.Should().Be(" world");
        decorator.LastRequestFellBack.Should().BeFalse();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WhenStreamingFails_FallsBackToNonStreaming() {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new TimeoutException("Stream timeout"));

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList())) {
            events.Add(evt);
        }

        events.Should().HaveCount(1);
        events[0].Content.Should().Be("fallback response");
        decorator.LastRequestFellBack.Should().BeTrue();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WhenFallbackDisabled_ThrowsException() {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new TimeoutException("Stream timeout"));

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig {
            Enabled = false,
            StreamWatchdogEnabled = false
        });

        var act = async () => {
            await foreach (var _ in decorator.GetStreamEventContentsAsync(new MessageList())) {
            }
        };

        await act.Should().ThrowAsync<TimeoutException>();
        decorator.LastRequestFellBack.Should().BeFalse();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_On503Error_TriggersFallback() {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new HttpRequestException("Unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable));

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList())) {
            events.Add(evt);
        }

        events.Should().HaveCount(1);
        decorator.LastRequestFellBack.Should().BeTrue();
    }

    [Fact]
    public async Task OnStreamingFallback_EventIsRaised_OnFallback() {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new TimeoutException());

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        var fallbackTriggered = false;
        decorator.OnStreamingFallback += () => fallbackTriggered = true;

        await foreach (var _ in decorator.GetStreamEventContentsAsync(new MessageList())) {
        }

        fallbackTriggered.Should().BeTrue();
    }

    [Fact]
    public async Task GetApiMessageContentsAsync_DelegatesToInner() {
        var inner = CreateMockStreamingService(shouldFail: false);
        var decorator = new StreamingFallbackDecorator(inner);

        var result = await decorator.GetApiMessageContentsAsync(new MessageList());

        result.Should().HaveCount(1);
        result[0].Content.Should().Be("non-streaming response");
        decorator.LastRequestFellBack.Should().BeFalse();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_FallbackEvents_HaveStreamingFallbackMetadata() {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new TimeoutException());

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList())) {
            events.Add(evt);
        }

        events.Should().HaveCount(1);
        events[0].Metadata.Should().ContainKey("StreamingFallback");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WhenBothFail_ThrowsAggregateException() {
        var mock = new Mock<IQueryService>();
        mock.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Returns(FailStreamAsync(new TimeoutException("stream failed"), CancellationToken.None));

        mock.Setup(s => s.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("non-stream also failed"));

        var decorator = new StreamingFallbackDecorator(mock.Object, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        var act = async () => {
            await foreach (var _ in decorator.GetStreamEventContentsAsync(new MessageList())) {
            }
        };

        await act.Should().ThrowAsync<AggregateException>();
    }

    #region ShouldFallback — 5异常类型分类 + 状态码匹配

    private static StreamingFallbackDecorator CreateDecorator(HashSet<int>? statusCodes = null) {
        var mock = new Mock<IQueryService>();
        return new StreamingFallbackDecorator(mock.Object, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false,
            FallbackStatusCodes = statusCodes ?? [529, 503, 502]
        });
    }

    [Fact]
    public void ShouldFallback_AlreadyCancelledToken_ReturnsFalse() {
        var decorator = CreateDecorator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        decorator.ShouldFallback(new TimeoutException(), cts.Token).Should().BeFalse();
    }

    [Fact]
    public void ShouldFallback_OperationCanceledWithOriginalToken_ReturnsFalse() {
        var decorator = CreateDecorator();
        using var cts = new CancellationTokenSource();

        decorator.ShouldFallback(new OperationCanceledException(cts.Token), cts.Token).Should().BeFalse();
    }

    [Fact]
    public void ShouldFallback_HttpRequestExceptionWithMatchingStatusCode_ReturnsTrue() {
        var decorator = CreateDecorator();
        var ex = new HttpRequestException("overloaded", null, HttpStatusCode.ServiceUnavailable);

        decorator.ShouldFallback(ex, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void ShouldFallback_HttpRequestExceptionWithNonMatchingStatusCode_StillReturnsTrue() {
        var decorator = CreateDecorator();
        var ex = new HttpRequestException("not found", null, HttpStatusCode.NotFound);

        decorator.ShouldFallback(ex, CancellationToken.None).Should().BeTrue("HttpRequestException 本身触发 fallback");
    }

    [Fact]
    public void ShouldFallback_HttpRequestExceptionWithoutStatusCode_ReturnsTrue() {
        var decorator = CreateDecorator();
        var ex = new HttpRequestException("network error");

        decorator.ShouldFallback(ex, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void ShouldFallback_ApiExceptionWithMatchingStatusCode_ReturnsTrue() {
        var decorator = CreateDecorator();
        var ex = new JoinCode.Abstractions.Exceptions.ApiException("overloaded", statusCode: 529);

        decorator.ShouldFallback(ex, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void ShouldFallback_ApiExceptionWithNonMatchingStatusCode_ReturnsFalse() {
        var decorator = CreateDecorator();
        var ex = new JoinCode.Abstractions.Exceptions.ApiException("bad request", statusCode: 400);

        decorator.ShouldFallback(ex, CancellationToken.None).Should().BeFalse("ApiException 状态码不匹配且不在兜底类型列表");
    }

    [Fact]
    public void ShouldFallback_TimeoutException_ReturnsTrue() {
        var decorator = CreateDecorator();
        decorator.ShouldFallback(new TimeoutException(), CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void ShouldFallback_TaskCanceledException_ReturnsTrue() {
        var decorator = CreateDecorator();
        decorator.ShouldFallback(new TaskCanceledException(), CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void ShouldFallback_IOException_ReturnsTrue() {
        var decorator = CreateDecorator();
        decorator.ShouldFallback(new IOException(), CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void ShouldFallback_GenericException_ReturnsFalse() {
        var decorator = CreateDecorator();
        decorator.ShouldFallback(new InvalidOperationException(), CancellationToken.None).Should().BeFalse();
    }

    #endregion

    #region AdjustSettingsForNonStreaming — Math.Min 截断

    [Fact]
    public void AdjustSettingsForNonStreaming_NullSettings_ReturnsNull() {
        var decorator = CreateDecorator();
        decorator.AdjustSettingsForNonStreaming(null).Should().BeNull();
    }

    [Fact]
    public void AdjustSettingsForNonStreaming_NullMaxTokens_CapsToConfig() {
        var decorator = CreateDecorator();
        var settings = new ChatOptions { Temperature = 0.5f };

        var result = decorator.AdjustSettingsForNonStreaming(settings);

        result.Should().NotBeNull();
        result!.MaxTokens.Should().Be(64_000, "null MaxTokens 时取 MaxNonStreamingTokens");
        result.Temperature.Should().Be(0.5f);
    }

    [Fact]
    public void AdjustSettingsForNonStreaming_SmallMaxTokens_Preserved() {
        var decorator = CreateDecorator();
        var settings = new ChatOptions { MaxTokens = 100 };

        var result = decorator.AdjustSettingsForNonStreaming(settings);

        result!.MaxTokens.Should().Be(100, "Math.MinC100, 64000D = 100");
    }

    [Fact]
    public void AdjustSettingsForNonStreaming_LargeMaxTokens_Capped() {
        var decorator = CreateDecorator();
        var settings = new ChatOptions { MaxTokens = 100_000 };

        var result = decorator.AdjustSettingsForNonStreaming(settings);

        result!.MaxTokens.Should().Be(64_000, "Math.MinC100000, 64000D = 64000");
    }

    [Fact]
    public void AdjustSettingsForNonStreaming_PreservesAllOtherFields() {
        var decorator = CreateDecorator();
        var settings = new ChatOptions {
            FastModelId = "fast",
            Temperature = 0.7f,
            MaxTokens = 50,
            TopP = 0.9f,
            FrequencyPenalty = 0.1f,
            PresencePenalty = 0.2f,
            ToolChoice = ToolChoice.AutoInvoke,
            EffortLevel = EffortLevel.Medium,
            FastMode = true
        };

        var result = decorator.AdjustSettingsForNonStreaming(settings);

        result!.FastModelId.Should().Be("fast");
        result.Temperature.Should().Be(0.7f);
        result.TopP.Should().Be(0.9f);
        result.FrequencyPenalty.Should().Be(0.1f);
        result.PresencePenalty.Should().Be(0.2f);
        result.ToolChoice.Should().Be(ToolChoice.AutoInvoke);
        result.EffortLevel.Should().Be(EffortLevel.Medium);
        result.FastMode.Should().BeTrue();
    }

    #endregion

    #region ConvertToStreamEvents — 列表转换 + metadata 注入

    [Fact]
    public void ConvertToStreamEvents_EmptyList_ReturnsEmpty() {
        var result = StreamingFallbackDecorator.ConvertToStreamEvents([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ConvertToStreamEvents_SingleMessage_AddsStreamingFallbackAndStopFinishReason() {
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, "hello")
        };

        var result = StreamingFallbackDecorator.ConvertToStreamEvents(messages);

        result.Should().ContainSingle();
        result[0].Content.Should().Be("hello");
        result[0].Metadata.Should().ContainKey("StreamingFallback");
        result[0].Metadata!["StreamingFallback"].GetBoolean().Should().BeTrue();
        result[0].Metadata.Should().ContainKey("FinishReason");
        result[0].Metadata!["FinishReason"].GetString().Should().Be("stop");
    }

    [Fact]
    public void ConvertToStreamEvents_MultipleMessages_OnlyLastHasFinishReasonStop() {
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, "part1"),
            new(MessageRole.Assistant, "part2"),
            new(MessageRole.Assistant, "part3")
        };

        var result = StreamingFallbackDecorator.ConvertToStreamEvents(messages);

        result.Should().HaveCount(3);
        result[0].Metadata.Should().NotContainKey("FinishReason");
        result[1].Metadata.Should().NotContainKey("FinishReason");
        result[2].Metadata!["FinishReason"].GetString().Should().Be("stop");
    }

    [Fact]
    public void ConvertToStreamEvents_PreservesOriginalMetadata() {
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, "x", new Dictionary<string, JsonElement> {
                ["CustomKey"] = JsonElementHelper.FromString("custom")
            })
        };

        var result = StreamingFallbackDecorator.ConvertToStreamEvents(messages);

        result[0].Metadata.Should().ContainKey("CustomKey");
        result[0].Metadata!["CustomKey"].GetString().Should().Be("custom");
    }

    #endregion

    #region GetApiMessageContentsAsync — 参数委托确定性测试

    [Fact]
    public async Task GetApiMessageContentsAsync_PassesAllArguments_ToInnerService() {
        // 验证:非流式调用完全委托 inner,所有参数透传,返回同一引用,LastRequestFellBack=false
        var mock = new Mock<IQueryService>();
        var expectedMessages = new List<ApiMessage> {
            new(MessageRole.Assistant, "delegated response", new Dictionary<string, JsonElement> {
                ["k"] = JsonElementHelper.FromString("v")
            }, "model-x")
        };
        mock.Setup(s => s.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMessages);

        var decorator = new StreamingFallbackDecorator(mock.Object);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var settings = new ChatOptions { Temperature = 0.5f, MaxTokens = 100 };
        using var cts = new CancellationTokenSource();

        var result = await decorator.GetApiMessageContentsAsync(history, settings, null, cts.Token);

        // 返回 inner 的同一引用(纯委托,不包装)
        result.Should().BeSameAs(expectedMessages);
        result[0].Content.Should().Be("delegated response");
        result[0].ModelId.Should().Be("model-x");
        result[0].Metadata!["k"].GetString().Should().Be("v");
        decorator.LastRequestFellBack.Should().BeFalse("非流式不触发 fallback");

        // 验证所有参数透传给 inner
        mock.Verify(s => s.GetApiMessageContentsAsync(history, settings, null, cts.Token), Times.Once);
    }

    [Fact]
    public async Task GetApiMessageContentsAsync_ResetsLastRequestFellBack_ToFalse() {
        // 验证:即使前一次流式 fallback 设置了 LastRequestFellBack=true,非流式调用也复位为 false
        var mock = new Mock<IQueryService>();
        mock.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Returns(FailStreamAsync(new TimeoutException(), CancellationToken.None));
        mock.Setup(s => s.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new(MessageRole.Assistant, "fb")]);

        var decorator = new StreamingFallbackDecorator(mock.Object, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        // 先触发一次流式 fallback,使 LastRequestFellBack=true
        await foreach (var _ in decorator.GetStreamEventContentsAsync(new MessageList())) { }
        decorator.LastRequestFellBack.Should().BeTrue("流式 fallback 后应为 true");

        // 再调用非流式,应复位为 false
        await decorator.GetApiMessageContentsAsync(new MessageList());
        decorator.LastRequestFellBack.Should().BeFalse("非流式调用复位为 false");
    }

    #endregion

    #region ExecuteFallbackAsync — 间接触发确定性测试

    [Fact]
    public async Task ExecuteFallbackAsync_SetsLastRequestFellBack_AndRaisesEvent_AndConvertsOutput() {
        // 通过流式失败间接触发 private ExecuteFallbackAsync
        // 验证:LastRequestFellBack=true + OnStreamingFallback 事件 + ConvertToStreamEvents 输出
        var mock = new Mock<IQueryService>();
        mock.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Returns(FailStreamAsync(new TimeoutException("stream down"), CancellationToken.None));

        // 非流式 fallback 返回固定消息(含原始 metadata + modelId)
        var fallbackMessages = new List<ApiMessage> {
            new(MessageRole.Assistant, "fallback content", new Dictionary<string, JsonElement> {
                ["OriginalKey"] = JsonElementHelper.FromString("orig")
            }, "fallback-model")
        };
        mock.Setup(s => s.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fallbackMessages);

        var decorator = new StreamingFallbackDecorator(mock.Object, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        var fallbackTriggered = false;
        decorator.OnStreamingFallback += () => fallbackTriggered = true;

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList())) {
            events.Add(evt);
        }

        // ExecuteFallbackAsync 副作用
        decorator.LastRequestFellBack.Should().BeTrue("ExecuteFallbackAsync 设置 LastRequestFellBack=true");
        fallbackTriggered.Should().BeTrue("ExecuteFallbackAsync 触发 OnStreamingFallback 事件");

        // ConvertToStreamEvents 输出验证
        events.Should().ContainSingle();
        events[0].Role.Should().Be(MessageRole.Assistant);
        events[0].Content.Should().Be("fallback content");
        events[0].ModelId.Should().Be("fallback-model");
        // 注入 StreamingFallback=true metadata
        events[0].Metadata.Should().ContainKey("StreamingFallback");
        events[0].Metadata!["StreamingFallback"].GetBoolean().Should().BeTrue();
        // 最后一条消息注入 FinishReason=stop
        events[0].Metadata.Should().ContainKey("FinishReason");
        events[0].Metadata!["FinishReason"].GetString().Should().Be("stop");
        // 保留原始 metadata
        events[0].Metadata.Should().ContainKey("OriginalKey", "ConvertToStreamEvents 保留原始 metadata");
        events[0].Metadata!["OriginalKey"].GetString().Should().Be("orig");
    }

    [Fact]
    public async Task ExecuteFallbackAsync_WithMultipleMessages_OnlyLastHasFinishReasonStop() {
        // 验证 ExecuteFallbackAsync 通过 ConvertToStreamEvents 处理多条消息时,仅最后一条带 FinishReason=stop
        var mock = new Mock<IQueryService>();
        mock.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Returns(FailStreamAsync(new HttpRequestException("overload"), CancellationToken.None));

        mock.Setup(s => s.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new(MessageRole.Assistant, "part1"),
                new(MessageRole.Assistant, "part2"),
                new(MessageRole.Assistant, "part3")
            ]);

        var decorator = new StreamingFallbackDecorator(mock.Object, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList())) {
            events.Add(evt);
        }

        events.Should().HaveCount(3);
        events[0].Content.Should().Be("part1");
        events[1].Content.Should().Be("part2");
        events[2].Content.Should().Be("part3");
        // 仅最后一条带 FinishReason
        events[0].Metadata.Should().NotContainKey("FinishReason");
        events[1].Metadata.Should().NotContainKey("FinishReason");
        events[2].Metadata.Should().ContainKey("FinishReason");
        events[2].Metadata!["FinishReason"].GetString().Should().Be("stop");
        // 全部带 StreamingFallback=true
        events.Should().AllSatisfy(e => e.Metadata.Should().ContainKey("StreamingFallback"));
        decorator.LastRequestFellBack.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteFallbackAsync_WhenNonStreamingAlsoFails_ThrowsAggregateException() {
        // 验证:流式失败 + 非流式 fallback 也失败 → AggregateException(两个都失败)
        var mock = new Mock<IQueryService>();
        mock.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Returns(FailStreamAsync(new TimeoutException("stream failed"), CancellationToken.None));

        mock.Setup(s => s.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("non-stream also failed"));

        var decorator = new StreamingFallbackDecorator(mock.Object, new StreamingFallbackConfig {
            StreamWatchdogEnabled = false
        });

        var act = async () => {
            await foreach (var _ in decorator.GetStreamEventContentsAsync(new MessageList())) { }
        };

        await act.Should().ThrowAsync<AggregateException>();
        decorator.LastRequestFellBack.Should().BeTrue("fallback 仍标记为 true");
    }

    #endregion
}