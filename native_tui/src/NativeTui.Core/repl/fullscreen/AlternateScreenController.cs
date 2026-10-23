namespace NativeTui.Core.Repl;

/// <summary>
/// 备用屏（DEC 1049）全屏模式控制器 — 对齐基准 ink AlternateScreen.tsx（33-79）：
/// 进入 = 1049h（含清屏语义）+ 2J 清屏 + H 光标 home + 鼠标跟踪组合启用（50 行）；
/// 退出 = 对称反序（鼠标组合禁用 → 1049l 恢复主屏，55 行 — 进入序的严格逆序）。
/// 鼠标组合对齐基准 termio/dec.ts（51-60）：1000 按钮按下/释放/滚轮 + 1002 拖拽 +
/// 1003 任意移动（悬停）+ 1006 SGR 十进制编码（进入序 1000→1002→1003→1006，
/// 退出反序 1006→1003→1002→1000 — 滚轮 + 点击/拖拽选择 + 悬停全能力）。
/// 焦点上报（DEC 1004）与光标隐藏（DEC 25）由外壳 Run 启动时统一处理，本类不重复。
/// 终端写入经构造注入的 writeAction 委托（Core 不持终端 IO — 对齐 clipboard 注入先例）；
/// 幂等防御：重复 Enter / 未激活 Exit 均 no-op（防双进入序列残留 — 基准 ink.tsx:962
/// "ENTER_ALT_SCREEN 终端侧幂等"语义的状态机前置拦截）。
/// </summary>
public sealed class AlternateScreenController
{
    /// <summary>进入序列 — 1049 + 清屏 + 光标 home + 鼠标组合（基准 AlternateScreen.tsx:50
    /// "ENTER_ALT_SCREEN + \x1B[2J\x1B[H + ENABLE_MOUSE_TRACKING" + dec.ts:51-55 四模式顺序）。</summary>
    private static readonly string EnterSequence =
        DecPrivateMode.Enable(DecPrivateModeKind.AlternateScreen)
        + Csi.EraseScreen()
        + Csi.CursorPosition(1, 1)
        + DecPrivateMode.Enable(DecPrivateModeKind.MouseClickTracking)
        + DecPrivateMode.Enable(DecPrivateModeKind.MouseEventTracking)
        + DecPrivateMode.Enable(DecPrivateModeKind.MouseAnyEventTracking)
        + DecPrivateMode.Enable(DecPrivateModeKind.MouseSgrEncoding);

    /// <summary>退出序列 — 鼠标组合反序禁用 + 1049 恢复主屏（基准 AlternateScreen.tsx:55
    /// "DISABLE_MOUSE_TRACKING + EXIT_ALT_SCREEN" + dec.ts:56-60 反序：1006→1003→1002→1000）。</summary>
    private static readonly string ExitSequence =
        DecPrivateMode.Disable(DecPrivateModeKind.MouseSgrEncoding)
        + DecPrivateMode.Disable(DecPrivateModeKind.MouseAnyEventTracking)
        + DecPrivateMode.Disable(DecPrivateModeKind.MouseEventTracking)
        + DecPrivateMode.Disable(DecPrivateModeKind.MouseClickTracking)
        + DecPrivateMode.Disable(DecPrivateModeKind.AlternateScreen);

    private readonly Action<string>? _writeAction;
    private bool _active;

    /// <summary>构造 — 终端写委托（宿主注入 output.Write 直写；null = 空操作降级 —
    /// 状态机照常运转仅不发射序列，纯逻辑可测，对齐 clipboard null 降级先例）。</summary>
    /// <param name="writeAction">终端写入口（序列整段单次发射 — 宿主保证线程安全）。</param>
    public AlternateScreenController(Action<string>? writeAction) => _writeAction = writeAction;

    /// <summary>备用屏激活态（进入后 true — 宿主分派/测试断言面）。</summary>
    public bool IsActive => _active;

    /// <summary>进入备用屏 — 1049 + 清屏复位 + 鼠标跟踪组合（全屏独占语义：主屏内容保留，
    /// 备用屏从干净画布起步）。幂等：已激活重复 Enter no-op。</summary>
    public void Enter()
    {
        if (_active)
        {
            return;
        }

        _active = true;
        Write(EnterSequence);
    }

    /// <summary>退出备用屏 — 鼠标组合反序禁用 + 1049 恢复主屏（对称恢复：主屏内容原样回归）。
    /// 幂等：未激活重复 Exit no-op（WriteExit 兜底路径可安全重入）。</summary>
    public void Exit()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        Write(ExitSequence);
    }

    /// <summary>进出切换（对齐 TranscriptController.Toggle 形态 — 激活翻转入/退出）。</summary>
    public void Toggle()
    {
        if (_active)
        {
            Exit();
        }
        else
        {
            Enter();
        }
    }

    /// <summary>序列发射 — 委托 null 时空操作（降级契约：状态机与 IO 分离）。</summary>
    private void Write(string sequence) => _writeAction?.Invoke(sequence);
}
