using Api.LLM.Fallback;
using System.Runtime.CompilerServices;

namespace Llm.Tests.Adapters.Fallback;

public class StreamingFallbackDecoratorTests
{
    private static IQueryService CreateMockStreamingService(
        bool shouldFail,
        Exception? exception = null)
    {
        var mock = new Mock<IQueryService>();

        if (shouldFail)
        {
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
        }
        else
        {
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
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return new StreamEvent(MessageRole.Assistant, "hello", "test-model");
        yield return new StreamEvent(MessageRole.Assistant, " world", "test-model");
    }

    private static IAsyncEnumerable<StreamEvent> FailStreamAsync(
        Exception exception, CancellationToken ct)
    {
        return FailStreamCoreAsync(exception, ct);
    }

    private static async IAsyncEnumerable<StreamEvent> FailStreamCoreAsync(
        Exception exception,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        yield return new StreamEvent(MessageRole.Assistant, "partial", "test-model");
        throw exception;
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WhenStreamingSucceeds_ReturnsStreamEvents()
    {
        var inner = CreateMockStreamingService(shouldFail: false);
        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig
        {
            StreamWatchdogEnabled = false
        });

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList()))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(2);
        events[0].Content.Should().Be("hello");
        events[1].Content.Should().Be(" world");
        decorator.LastRequestFellBack.Should().BeFalse();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WhenStreamingFails_FallsBackToNonStreaming()
    {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new TimeoutException("Stream timeout"));

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig
        {
            StreamWatchdogEnabled = false
        });

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList()))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(1);
        events[0].Content.Should().Be("fallback response");
        decorator.LastRequestFellBack.Should().BeTrue();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WhenFallbackDisabled_ThrowsException()
    {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new TimeoutException("Stream timeout"));

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig
        {
            Enabled = false,
            StreamWatchdogEnabled = false
        });

        var act = async () =>
        {
            await foreach (var _ in decorator.GetStreamEventContentsAsync(new MessageList()))
            {
            }
        };

        await act.Should().ThrowAsync<TimeoutException>();
        decorator.LastRequestFellBack.Should().BeFalse();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_On503Error_TriggersFallback()
    {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new HttpRequestException("Unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable));

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig
        {
            StreamWatchdogEnabled = false
        });

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList()))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(1);
        decorator.LastRequestFellBack.Should().BeTrue();
    }

    [Fact]
    public async Task OnStreamingFallback_EventIsRaised_OnFallback()
    {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new TimeoutException());

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig
        {
            StreamWatchdogEnabled = false
        });

        var fallbackTriggered = false;
        decorator.OnStreamingFallback += () => fallbackTriggered = true;

        await foreach (var _ in decorator.GetStreamEventContentsAsync(new MessageList()))
        {
        }

        fallbackTriggered.Should().BeTrue();
    }

    [Fact]
    public async Task GetApiMessageContentsAsync_DelegatesToInner()
    {
        var inner = CreateMockStreamingService(shouldFail: false);
        var decorator = new StreamingFallbackDecorator(inner);

        var result = await decorator.GetApiMessageContentsAsync(new MessageList());

        result.Should().HaveCount(1);
        result[0].Content.Should().Be("non-streaming response");
        decorator.LastRequestFellBack.Should().BeFalse();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_FallbackEvents_HaveStreamingFallbackMetadata()
    {
        var inner = CreateMockStreamingService(
            shouldFail: true,
            exception: new TimeoutException());

        var decorator = new StreamingFallbackDecorator(inner, new StreamingFallbackConfig
        {
            StreamWatchdogEnabled = false
        });

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList()))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(1);
        events[0].Metadata.Should().ContainKey("StreamingFallback");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WhenBothFail_ThrowsAggregateException()
    {
        var mock = new Mock<IQueryService>();
        mock.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Returns(FailStreamAsync(new TimeoutException("stream failed"), CancellationToken.None));

        mock.Setup(s => s.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("non-stream also failed"));

        var decorator = new StreamingFallbackDecorator(mock.Object, new StreamingFallbackConfig
        {
            StreamWatchdogEnabled = false
        });

        var act = async () =>
        {
            await foreach (var _ in decorator.GetStreamEventContentsAsync(new MessageList()))
            {
            }
        };

        await act.Should().ThrowAsync<AggregateException>();
    }
}
