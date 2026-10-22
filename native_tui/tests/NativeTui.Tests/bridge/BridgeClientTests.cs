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
}
