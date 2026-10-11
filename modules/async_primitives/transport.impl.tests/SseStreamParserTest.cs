namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// SseStreamParser 确定性测试 — 用 MemoryStream 喂入 SSE 帧文本，不依赖网络/HTTP/进程。
/// </summary>
public class SseStreamParserTest {
    private static async Task<List<SseEvent>> ParseAllAsync(string sse, CancellationToken ct = default) {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var result = new List<SseEvent>();
        await foreach (var ev in SseStreamParser.ParseAsync(stream, ct).ConfigureAwait(false)) {
            result.Add(ev);
        }
        return result;
    }

    /// <summary>单事件: event:/data: 拼接，空行终结。</summary>
    [Fact]
    public async Task ParseAsync_SingleEvent_ReturnsOneEvent() {
        var events = await ParseAllAsync("event: ping\ndata: hello\n\n");

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("ping");
        events[0].Data.Should().Be("hello");
        events[0].Id.Should().BeNull();
    }

    /// <summary>id: 字段被解析到 Id 属性。</summary>
    [Fact]
    public async Task ParseAsync_WithId_ParsesIdField() {
        var events = await ParseAllAsync("id: 42\nevent: update\ndata: payload\n\n");

        events.Should().HaveCount(1);
        events[0].Id.Should().Be("42");
        events[0].EventType.Should().Be("update");
        events[0].Data.Should().Be("payload");
    }

    /// <summary>多行 data: 用换行拼接成单个 Data 字符串。</summary>
    [Fact]
    public async Task ParseAsync_MultiLineData_JoinsWithNewline() {
        var events = await ParseAllAsync("data: line1\ndata: line2\ndata: line3\n\n");

        events.Should().HaveCount(1);
        events[0].Data.Should().Be("line1\r\nline2\r\nline3");
    }

    /// <summary>空行分隔多个独立事件。</summary>
    [Fact]
    public async Task ParseAsync_MultipleEvents_SeparatedByBlankLines() {
        var events = await ParseAllAsync("event: a\ndata: 1\n\nevent: b\ndata: 2\n\n");

        events.Should().HaveCount(2);
        events[0].EventType.Should().Be("a");
        events[0].Data.Should().Be("1");
        events[1].EventType.Should().Be("b");
        events[1].Data.Should().Be("2");
    }

    /// <summary>注释行（: 开头）不匹配任何字段前缀，被自然跳过，不产生事件。</summary>
    [Fact]
    public async Task ParseAsync_CommentLine_Skipped() {
        var events = await ParseAllAsync(": keep-alive comment\nevent: ping\ndata: ok\n\n");

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("ping");
        events[0].Data.Should().Be("ok");
    }

    /// <summary>无 event: 字段时 EventType 为空字符串（实现未默认 "message"）。</summary>
    [Fact]
    public async Task ParseAsync_NoEventField_EventTypeIsEmpty() {
        var events = await ParseAllAsync("data: only-data\n\n");

        events.Should().HaveCount(1);
        events[0].EventType.Should().BeEmpty();
        events[0].Data.Should().Be("only-data");
    }

    /// <summary>retry: 字段不被解析（实现未处理），不影响事件产出。</summary>
    [Fact]
    public async Task ParseAsync_RetryField_IgnoredWithoutError() {
        var events = await ParseAllAsync("retry: 5000\nevent: ping\ndata: ok\n\n");

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("ping");
        events[0].Data.Should().Be("ok");
    }

    /// <summary>空流不产出任何事件。</summary>
    [Fact]
    public async Task ParseAsync_EmptyStream_ProducesNoEvents() {
        var events = await ParseAllAsync("");

        events.Should().BeEmpty();
    }

    /// <summary>不完整帧（无终结空行）在流结束时作为最后一个事件产出。</summary>
    [Fact]
    public async Task ParseAsync_IncompleteFrame_FlushedAtEof() {
        var events = await ParseAllAsync("event: partial\ndata: unfinished");

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("partial");
        events[0].Data.Should().Be("unfinished");
    }

    /// <summary>仅空白行不产出事件（dataBuilder 为空时空行被忽略）。</summary>
    [Fact]
    public async Task ParseAsync_OnlyBlankLines_ProducesNoEvents() {
        var events = await ParseAllAsync("\n\n\n");

        events.Should().BeEmpty();
    }

    /// <summary>event: 字段值带前导空格被 Trim。</summary>
    [Fact]
    public async Task ParseAsync_EventFieldWithSpace_Trimmed() {
        var events = await ParseAllAsync("event:   spaced  \ndata: x\n\n");

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("spaced");
    }
}
