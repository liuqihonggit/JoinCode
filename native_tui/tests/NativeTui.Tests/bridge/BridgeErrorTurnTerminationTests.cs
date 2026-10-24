// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace NativeTui.Tests.Bridge;

/// <summary>
/// Error 事件回合终态测试（TASK069 B-1/B-2 — 对齐基准 REPL.tsx:2951-2961 finally 兜底
/// "Always reset loading state"）：回合异常（API 错误/超时/空回复/工具拒绝）只发 Error 无终态事件时，
/// Error 分支自身承担回合收尾（EndTurn 语义）——IsBusy 必须复位，否则提交永久排队、Esc 无效、
/// 引擎死亡检测被 !IsBusy 挡住（功能性死锁，每次 API 错误必现）。
/// </summary>
public sealed class BridgeErrorTurnTerminationTests
{
    private readonly ReplView _view = new();
    private readonly SpinnerItem _spinner = new();
    private readonly BannerItem _banner = new("0.0.0", "占位模型", "API 按量计费", @"C:\placeholder");
    private readonly ReplBridgeDriver _driver;

    public BridgeErrorTurnTerminationTests()
    {
        _driver = new ReplBridgeDriver(_view, _spinner, _banner);
        _view.Messages.Add(_banner);
        _view.Messages.Add(_spinner);
    }

    /// <summary>Error 事件复位 IsBusy（B-1 核心 — 异常回合后 UI 不死锁）。</summary>
    [Fact]
    public void ErrorEvent_ResetsIsBusy()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        Assert.True(_driver.IsBusy);

        _driver.Apply(new BridgeEvent.Error("API 请求失败"));

        Assert.False(_driver.IsBusy);
        Assert.False(_view.Footer.Busy);
    }

    /// <summary>Error 前已流式的部分文本定格为 partial（已流文本不丢 — 对齐基准 [user, partial, error] 顺序）。</summary>
    [Fact]
    public void ErrorEvent_AfterPartialText_FinalizesStreamingAsPartial()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.TokenDelta(6, "已流的部分"));
        Assert.True(_driver.IsBusy);

        _driver.Apply(new BridgeEvent.Error("超时"));

        Assert.False(_driver.IsBusy);
        Assert.Contains(_view.Messages.Items, static item => item is AssistantMessageItem { Text: "已流的部分" });
        Assert.Contains(_view.Messages.Items, static item => item is ErrorMessageItem { Text: "超时" });
        // partial 在 error 之前（对齐基准顺序）
        var partialIndex = IndexOf(_view, static item => item is AssistantMessageItem { Text: "已流的部分" });
        var errorIndex = IndexOf(_view, static item => item is ErrorMessageItem { Text: "超时" });
        Assert.True(partialIndex < errorIndex);
    }

    /// <summary>Error 后 spinner 收尾（hidden — 空闲不闪烁）。</summary>
    [Fact]
    public void ErrorEvent_HidesSpinner()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        Assert.False(_spinner.Hidden);

        _driver.Apply(new BridgeEvent.Error("失败"));

        Assert.True(_spinner.Hidden);
    }

    /// <summary>Error 后可立即开新回合（TurnStarted 正常接管 — 死锁回归守卫）。</summary>
    [Fact]
    public void ErrorEvent_AllowsNewTurn()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.Error("失败"));

        _driver.Apply(new BridgeEvent.TurnStarted());
        Assert.True(_driver.IsBusy);
        _driver.Apply(new BridgeEvent.TurnCompleted());
        Assert.False(_driver.IsBusy);
    }

    /// <summary>重复 Error（无 TurnStarted）幂等 — 不重复追加错误消息之外的副作用（EndTurn 幂等）。</summary>
    [Fact]
    public void RepeatedError_Idempotent_NoBusyLeak()
    {
        _driver.Apply(new BridgeEvent.Error("第一次"));

        _driver.Apply(new BridgeEvent.Error("第二次"));

        Assert.False(_driver.IsBusy);
        Assert.Equal(2, _view.Messages.Items.Count(static item => item is ErrorMessageItem));
    }

    private static int IndexOf(ReplView view, Func<IScrollItem, bool> predicate)
    {
        for (var i = 0; i < view.Messages.Items.Count; i++)
        {
            if (predicate(view.Messages.Items[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
