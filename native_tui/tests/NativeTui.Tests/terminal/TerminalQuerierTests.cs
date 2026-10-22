namespace NativeTui.Tests.Terminal;

/// <summary>
/// 终端查询路由器测试 — 基准 ink/terminal-querier.ts + parse-keypress.ts:484-487：
/// 发送查询序列 → 注册 pending 等待 → 回复经 Route 命中同 Kind resolve；无等待方/超时兜底。
/// </summary>
public sealed class TerminalQuerierTests
{
    /// <summary>查询 → 回复路由命中 → 任务完成得响应；序列已写输出。</summary>
    [Fact]
    public async Task Query_RoutedReply_CompletesWithResponse()
    {
        var output = new StringWriter();
        using var querier = new TerminalQuerier(output);

        var task = querier.QueryAsync("\u001b[c", TerminalResponseKind.Da1, TimeSpan.FromSeconds(5));

        Assert.Contains("\u001b[c", output.ToString());
        var routed = querier.Route(new TerminalResponse(TerminalResponseKind.Da1, "\u001b[?1;2c", [1, 2], null));
        Assert.True(routed);
        var response = await task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(response);
        Assert.Equal([1, 2], response.Numbers);
    }

    /// <summary>无等待方回复 — Route 返回 false（静默丢弃不误 resolve）。</summary>
    [Fact]
    public void Route_NoPendingQuery_ReturnsFalse()
    {
        using var querier = new TerminalQuerier(new StringWriter());

        Assert.False(querier.Route(new TerminalResponse(TerminalResponseKind.KittyFlags, "\u001b[?1u", [1], null)));
    }

    /// <summary>超时兜底 — 无回复时任务完成得 null（探测不可靠时无害降级，对齐基准 querier 超时）。</summary>
    [Fact]
    public async Task Query_NoReply_TimesOutToNull()
    {
        using var querier = new TerminalQuerier(new StringWriter());

        var response = await querier.QueryAsync("\u001b[c", TerminalResponseKind.Da1, TimeSpan.FromMilliseconds(50));

        Assert.Null(response);
    }

    /// <summary>同 Kind 二次查询 — 旧条目被覆盖后旧回复不误 resolve 新等待（一次性条目 Route 即移除）。</summary>
    [Fact]
    public async Task RepeatedQuery_ReplyRoutesToLatestOnly()
    {
        using var querier = new TerminalQuerier(new StringWriter());

        var first = querier.QueryAsync("\u001b[c", TerminalResponseKind.Da1, TimeSpan.FromMilliseconds(50));
        var second = querier.QueryAsync("\u001b[c", TerminalResponseKind.Da1, TimeSpan.FromSeconds(5));

        // 第二次查询已覆盖条目 — 第一条回复路由给第二次等待
        Assert.True(querier.Route(new TerminalResponse(TerminalResponseKind.Da1, "\u001b[?1;0c", [1, 0], null)));
        var secondResponse = await second.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(secondResponse);
        Assert.Equal([1, 0], secondResponse.Numbers);

        // 第一次等待超时得 null（其回复已被路由给第二次）
        Assert.Null(await first);
    }
}
