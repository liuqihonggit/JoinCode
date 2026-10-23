namespace NativeTui.Core.Repl;

/// <summary>
/// 渲染帧级命中测试注册表（TASK066 板块 C 基建 C2）— 对齐基准 ink/hit-test.ts:18-41 hitTest。
/// 基准用 nodeCache（renderNodeToOutput 每帧填充的 DOM 节点 → 屏幕 rect 映射，hit-test.ts:8-10）
/// 自根向下递归查找包含 (col, row) 的最深节点；w2 无 React DOM 树 — 用「cell 归属表」替代 nodeCache：
/// 渲染期各 item 向本表声明自己的屏幕 rect（Register），查询期反向遍历取首个包含点。
/// 语义对等要点：
/// ① rect 为半开区间 [col, col+width) × [row, row+height) — 基准 :26-29 越界判定
///   （col &gt;= rect.x + rect.width 即出界，右/下边界不含）；
/// ② 后注册优先 = 后绘制兄弟优先 — 基准 :33-39 childNodes 反向遍历
///   （"Later siblings paint on top; reversed traversal returns topmost hit"），
///   w2 的注册序即绘制序（同一帧内 ScrollBox 自上而下逐项 Render），反向遍历即顶层优先；
/// ③ 帧级重建 — 基准 nodeCache 每帧重填（未渲染项不在表内即跳过，:12-13），
///   w2 由 ReplView 帧首 Clear() 对等实现，未注册区域命中返回 null；
/// ④ rect 为屏幕坐标且已含滚动平移 — 基准 :9-10（"rects are in screen coordinates with all
///   offsets (including scrollTop translation) already applied"），w2 由调用方传平移后的 frame 坐标对等。
/// 线程模型：主循环单线程渲染契约（Screen 同款 — 无锁，跨线程访问由上层禁止）。
/// 挂载点设计（主代理接线参考）：推荐 ReplView 持有本实例（渲染编排者，帧生命周期掌控者），
/// 不挂 Screen — Grid 层是纯帧缓冲无命中语义，挂 ReplView 避免反向依赖。接线帧序：
/// ReplView.Render 帧首 registry.Clear() → ScrollBox 逐项 Render 时按 IScrollItem.HitTestId
/// 非空调 registry.Register(id, itemRect)（rect 取该 item 的 frame 投影，Y 已含 scrollTop 平移）→
/// 鼠标 MouseEvent(Press/Release) 到达时 registry.HitTest(e.Row, e.Col) → 非空 id 分派到对应 item
/// （分派/冒泡策略对齐基准 dispatchClick hit-test.ts:49-89，属后续任务，本注册表只提供查找原语）。
/// </summary>
public sealed class HitTestRegistry
{
    /// <summary>命中矩形条目（注册序存储 — HitTest 反向遍历的序依据）。</summary>
    private readonly record struct HitRect(string Id, int Row, int Col, int Width, int Height);

    /// <summary>本帧注册条目清单（注册序 = 绘制序）。</summary>
    private readonly List<HitRect> _rects = [];

    /// <summary>本帧已注册条目数（帧健康检查/测试断言面 — 清空后应为 0）。</summary>
    public int Count => _rects.Count;

    /// <summary>
    /// 注册一个命中矩形 — 渲染期 item 声明自身屏幕 rect（row/col 为左上角，坐标已含滚动平移，
    /// 对齐基准 nodeCache 屏幕坐标语义 hit-test.ts:9-10）。同一 id 可多次注册（item 分块声明，
    /// 如多行 item 的标题行+参数行各一块 — 反向遍历语义下同 id 多 rect 与单 rect 等价）。
    /// </summary>
    /// <param name="id">命中标识（IScrollItem.HitTestId — 非空白；空 id 视为编程错误直接抛出）。</param>
    /// <param name="row">矩形左上角行（0-based 屏幕行）。</param>
    /// <param name="col">矩形左上角列（0-based 屏幕列）。</param>
    /// <param name="width">矩形宽（cell 数，&gt;= 1 — 半开区间右边界为 col+width，不含）。</param>
    /// <param name="height">矩形高（cell 数，&gt;= 1 — 半开区间下边界为 row+height，不含）。</param>
    public void Register(string id, int row, int col, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        _rects.Add(new HitRect(id, row, col, width, height));
    }

    /// <summary>
    /// 命中测试 — 返回包含 (row, col) 的最顶层注册 id；无任何矩形包含返回 null（未渲染区域，
    /// 对齐基准 nodeCache 未命中语义 hit-test.ts:24）。反向遍历：后注册（后绘制）优先，
    /// 对齐基准 :33-39 反向遍历的顶层优先语义。边界判定为半开区间（基准 :26-29）：
    /// (row, col) ∈ [Row, Row+Height) × [Col, Col+Width)。
    /// </summary>
    /// <param name="row">查询行（0-based 屏幕行 — MouseEvent.Row 同坐标系）。</param>
    /// <param name="col">查询列（0-based 屏幕列 — MouseEvent.Col 同坐标系）。</param>
    /// <returns>命中的 HitTestId；未命中 null。</returns>
    public string? HitTest(int row, int col)
    {
        for (var i = _rects.Count - 1; i >= 0; i--)
        {
            var rect = _rects[i];
            if (col >= rect.Col && col < rect.Col + rect.Width
                && row >= rect.Row && row < rect.Row + rect.Height)
            {
                return rect.Id;
            }
        }

        return null;
    }

    /// <summary>帧首清空 — 每帧重建语义（对齐基准 nodeCache 每帧重填 hit-test.ts:8-10；
    /// 上一帧的 rect 不跨帧存活，防止陈旧命中）。</summary>
    public void Clear() => _rects.Clear();
}
