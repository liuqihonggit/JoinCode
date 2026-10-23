namespace NativeTui.Tests.Bridge;

/// <summary>
/// 流式项跨工具边界测试（TASK069 B-4 — 对齐基准 messages.ts:3003 新 content_block_start
/// 清空流式文本）：agent loop 真实序列 text→tool→text 时，工具调用开启即定格前段正文
/// 为 partial（新文本进新流式项 — 不追加到工具之前的旧项），落定无重复无错序。
/// </summary>
public sealed class StreamingItemToolBoundaryTests
{
    private readonly ReplView _view = new();
    private readonly SpinnerItem _spinner = new();
    private readonly BannerItem _banner = new("0.0.0", "占位模型", "API 按量计费", @"C:\placeholder");
    private readonly ReplBridgeDriver _driver;

    public StreamingItemToolBoundaryTests()
    {
        _driver = new ReplBridgeDriver(_view, _spinner, _banner);
        _view.Messages.Add(_banner);
        _view.Messages.Add(_spinner);
    }

    /// <summary>工具调用开启 — 已流式正文定格为 partial（工具前文本不丢、不吞并后续）。</summary>
    [Fact]
    public void ToolCall_AfterPartialText_FinalizesStreamingAsPartial()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.TokenDelta(6, "第一段"));

        _driver.Apply(new BridgeEvent.ToolCall("Bash", "{}"));

        Assert.Contains(_view.Messages.Items, static item => item is AssistantMessageItem { Text: "第一段" });
        // 工具项在 partial 之后
        var partialIndex = IndexOf(static item => item is AssistantMessageItem { Text: "第一段" });
        var toolIndex = IndexOf(static item => item is ToolCallItem);
        Assert.True(partialIndex < toolIndex, "partial 应在工具项之前");
    }

    /// <summary>工具后新文本进新流式项（B-4 核心 — 不追加到工具前的旧流式项）。</summary>
    [Fact]
    public void TextAfterTool_AppendsToNewStreamingItem_AfterTool()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.TokenDelta(6, "第一段"));
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "{}"));
        _driver.Apply(new BridgeEvent.ToolResult("完成", null, null, 0));

        _driver.Apply(new BridgeEvent.TokenDelta(6, "第二段"));

        // 落定前：流式项在工具结果之后
        var streamingIndex = IndexOf(static item => item is StreamingTextItem);
        var toolIndex = IndexOf(static item => item is ToolCallItem);
        Assert.True(toolIndex < streamingIndex, "新流式项应在工具项之后");

        // 落定：两段文本各归其位、无重复合并
        _driver.Apply(new BridgeEvent.AssistantMessage("第一段\n\n第二段"));
        Assert.Equal(0, CountOf(static item => item is StreamingTextItem));
        // 两条 AssistantMessage 各归其位：partial"第一段"（工具前 — 已流文本不丢）+
        // final"第一段\n\n第二段"（引擎 OnDone 发整回合全文 — 协议粒度限制，
        // final 含 partial 全文的视觉重复问题登记 TASK069 差异项归引擎域）
        Assert.Equal(2, CountOf(static item => item is AssistantMessageItem));
        Assert.Equal(1, CountOf(static item => item is AssistantMessageItem { Text: "第一段" }));
        Assert.Equal(1, CountOf(static item => item is AssistantMessageItem { Text: "第一段\n\n第二段" }));
        // 顺序：partial → 工具 → final
        Assert.True(IndexOf(static item => item is AssistantMessageItem { Text: "第一段" })
            < IndexOf(static item => item is ToolCallItem));
        Assert.True(IndexOf(static item => item is ToolCallItem)
            < IndexOf(static item => item is AssistantMessageItem { Text: "第一段\n\n第二段" }));
    }

    private int IndexOf(Func<IScrollItem, bool> predicate)
    {
        for (var i = 0; i < _view.Messages.Items.Count; i++)
        {
            if (predicate(_view.Messages.Items[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private int CountOf(Func<IScrollItem, bool> predicate)
    {
        var count = 0;
        for (var i = 0; i < _view.Messages.Items.Count; i++)
        {
            if (predicate(_view.Messages.Items[i]))
            {
                count++;
            }
        }

        return count;
    }
}
