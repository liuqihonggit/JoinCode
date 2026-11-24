namespace NativeTui.Tests.Bridge;

/// <summary>
/// bridge 客户端停止接线测试（M8-8 后续）— BridgeClient.StopBackgroundTask 写 stdin
/// stop_task 请求（对齐 Submit/Interrupt 的 BridgeProtocol.WriteRequest 发送形态；
/// 即发即返不等回执 — cancelled 终态事件由泵回推）+ BridgeEngine 适配器 IReplEngine
/// 接口多态转发（bridge 模式 x 停止的 ReplShell 消费面）。
/// internal 构造注入 StringWriter/StringReader（无子进程 — 协议行单测，真进程级走 Demo --bridge-selftest）。
/// </summary>
public sealed class BridgeClientTests
{
    /// <summary>构造测试客户端（stdin 捕获到 StringWriter，stdout 空流 — 泵立即 Eof 收尾）。</summary>
    private static (BridgeClient Client, StringWriter Stdin) CreateClient()
    {
        var stdin = new StringWriter();
        var client = new BridgeClient(stdin, new StringReader(string.Empty));
        return (client, stdin);
    }

    /// <summary>StopBackgroundTask — 写一行 stop_task JSON（kind/taskId camelCase 逐字段）。</summary>
    [Fact]
    public void StopBackgroundTask_WritesStopTaskRequestLine()
    {
        var (client, stdin) = CreateClient();

        client.StopBackgroundTask("fork", "fork-1");

        Assert.Equal("{\"type\":\"stop_task\",\"kind\":\"fork\",\"taskId\":\"fork-1\"}\n", stdin.ToString());
    }

    /// <summary>StopBackgroundTask 第二发 — teammate 形态独立成行（×2 kind/taskId 序列化正确性 +
    /// 多次停止请求不互相覆盖，newline 分格协议语义）。</summary>
    [Fact]
    public void StopBackgroundTask_SecondRequest_AppendsSeparateLine()
    {
        var (client, stdin) = CreateClient();

        client.StopBackgroundTask("fork", "fork-1");
        client.StopBackgroundTask("teammate", "teammate-2");

        Assert.Equal(
            "{\"type\":\"stop_task\",\"kind\":\"fork\",\"taskId\":\"fork-1\"}\n{\"type\":\"stop_task\",\"kind\":\"teammate\",\"taskId\":\"teammate-2\"}\n",
            stdin.ToString());
    }

    /// <summary>BridgeEngine 适配器 — IReplEngine.StopBackgroundTask 接口多态调用经转发写子进程 stdin
    /// （bridge 模式 ReplShell 的 x 停止委托接线面：engine.StopBackgroundTask → client → 协议行）。</summary>
    [Fact]
    public void BridgeEngine_StopBackgroundTask_ForwardsToClientStdin()
    {
        var stdin = new StringWriter();
        using var engine = new BridgeEngine(new BridgeClient(stdin, new StringReader(string.Empty)));
        var replEngine = (IReplEngine)engine;

        replEngine.StopBackgroundTask("teammate", "tm-9");

        Assert.Equal("{\"type\":\"stop_task\",\"kind\":\"teammate\",\"taskId\":\"tm-9\"}\n", stdin.ToString());
    }

    /// <summary>Submit 带图片附件（TASK066 板块 G G4）— 写一行 submit JSON 含 images 数组
    /// （base64/mediaType/filename camelCase 逐字段；Default 上下文转义非 ASCII — 断言用 ASCII）。</summary>
    [Fact]
    public void Submit_WithImages_WritesSubmitRequestWithImagesField()
    {
        var (client, stdin) = CreateClient();

        client.Submit("look at this", [new BridgeImageAttachment("aGk=", "image/png", "shot.png")], queryId: "q-1");

        Assert.Equal(
            "{\"type\":\"submit\",\"text\":\"look at this\",\"images\":[{\"base64\":\"aGk=\",\"mediaType\":\"image/png\",\"filename\":\"shot.png\"}],\"queryId\":\"q-1\"}\n",
            stdin.ToString());
    }

    /// <summary>Submit 多附件 — images 数组按提交顺序逐元素序列化（[Image #N] 投影序即数组序）。</summary>
    [Fact]
    public void Submit_WithMultipleImages_SerializesArrayInOrder()
    {
        var (client, stdin) = CreateClient();

        client.Submit("two shots",
            [new BridgeImageAttachment("aGk=", "image/png", "a.png"), new BridgeImageAttachment("eQ==", "image/jpeg", "b.jpg")],
            queryId: "q-2");

        Assert.Equal(
            "{\"type\":\"submit\",\"text\":\"two shots\",\"images\":[{\"base64\":\"aGk=\",\"mediaType\":\"image/png\",\"filename\":\"a.png\"},{\"base64\":\"eQ==\",\"mediaType\":\"image/jpeg\",\"filename\":\"b.jpg\"}],\"queryId\":\"q-2\"}\n",
            stdin.ToString());
    }

    /// <summary>Submit 空附件数组 — 归一为不带 images 字段，但 queryId 必带（ADR 0165 事件归因前提）。</summary>
    [Fact]
    public void Submit_EmptyImages_OmitsImagesFieldButCarriesQueryId()
    {
        var (client, stdin) = CreateClient();

        client.Submit("x", Array.Empty<BridgeImageAttachment>(), queryId: "q-3");

        Assert.Equal("{\"type\":\"submit\",\"text\":\"x\",\"queryId\":\"q-3\"}\n", stdin.ToString());
    }

    /// <summary>Submit null 附件 — 同空数组归一（不带 images 字段），queryId 仍必带。</summary>
    [Fact]
    public void Submit_NullImages_OmitsImagesFieldButCarriesQueryId()
    {
        var (client, stdin) = CreateClient();

        client.Submit("x", images: null, queryId: "q-4");

        Assert.Equal("{\"type\":\"submit\",\"text\":\"x\",\"queryId\":\"q-4\"}\n", stdin.ToString());
    }

    /// <summary>Submit 不传 queryId — 客户端本地生成并返回，写出的 queryId 与返回值一致
    /// （ADR 0165：前台必须立刻知道查询身份，不能等首个事件回来才知道）。</summary>
    [Fact]
    public void Submit_WithoutQueryId_GeneratesAndWritesIt()
    {
        var (client, stdin) = CreateClient();

        var queryId = client.Submit("x");

        Assert.False(string.IsNullOrWhiteSpace(queryId));
        Assert.Contains($"\"queryId\":\"{queryId}\"", stdin.ToString(), StringComparison.Ordinal);
    }

    /// <summary>BridgeEngine 适配器 — IReplEngine.Submit 双参接口多态调用经转发写子进程 stdin
    /// （G4 — bridge 模式 ReplShell 提交附件直达协议行，取代接口默认成员的图片丢弃路径）。</summary>
    [Fact]
    public void BridgeEngine_SubmitWithImages_ForwardsToClientStdin()
    {
        var stdin = new StringWriter();
        using var engine = new BridgeEngine(new BridgeClient(stdin, new StringReader(string.Empty)));
        var replEngine = (IReplEngine)engine;

        replEngine.Submit("two shots",
            [new BridgeImageAttachment("aGk=", "image/png", "a.png"), new BridgeImageAttachment("eQ==", "image/jpeg", "b.jpg")],
            queryId: "q-5");

        Assert.Equal(
            "{\"type\":\"submit\",\"text\":\"two shots\",\"images\":[{\"base64\":\"aGk=\",\"mediaType\":\"image/png\",\"filename\":\"a.png\"},{\"base64\":\"eQ==\",\"mediaType\":\"image/jpeg\",\"filename\":\"b.jpg\"}],\"queryId\":\"q-5\"}\n",
            stdin.ToString());
    }
}
