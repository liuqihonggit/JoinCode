namespace NativeTui.Core.Bridge;

/// <summary>
/// 模拟引擎 — Demo 模拟回合的事件化改造（ReplShell 提取后替代 DemoSession 内嵌状态机）：
/// 时间驱动三阶段（Thinking 1.2s → ToolUse 2.4s → Responding 5.4s），
/// 阶段迁移产出 BridgeEvent（ToolCall/ToolResult/TokenDelta/TurnCompleted），
/// 与真实 bridge 路径共用同一事件协议（ReplBridgeDriver 唯一翻译层）。
/// 时钟注入（默认墙钟）— 单元测试可注入虚拟时钟做确定性验证。
/// spinner Message 不经事件协议（真实路径空消息+随机动词采样，模拟直改属可弃糖）。
/// </summary>
public sealed class SimulatedEngine : IReplEngine
{
    /// <summary>Thinking → ToolUse 迁移时刻（ms）。</summary>
    private const long ToolUseAtMs = 1200;

    /// <summary>ToolUse → Responding 迁移时刻（ms）。</summary>
    private const long RespondingAtMs = 2400;

    /// <summary>回合完成时刻（ms）。</summary>
    private const long CompletedAtMs = 5400;

    /// <summary>Responding 阶段每拍 token 增量。</summary>
    private const int TokenDeltaPerTick = 9;

    /// <summary>回合计时起点（clock 值）。</summary>
    private long _turnStart;

    /// <summary>已推进到的阶段（0=Thinking / 1=ToolUse / 2=Responding / 3=完成）。</summary>
    private int _stage;

    /// <summary>回合进行中。</summary>
    private bool _inTurn;

    /// <summary>待排空事件队列（Submit/Interrupt 立即入队，DrainEvents 阶段迁移追加）。</summary>
    private readonly Queue<BridgeEvent> _pending = new();

    /// <summary>时钟（默认墙钟 — 测试注入虚拟时钟）。</summary>
    private readonly Func<long> _clock;

    /// <summary>
    /// 模拟补丁 — Demo 演示 StructuredDiff 渲染链路（M7-2）：4 行 hunk 超 2 行预览，同时演示折叠提示。
    /// </summary>
    private static readonly DiffHunk[] SimulatedPatch =
    [
        new()
        {
            Header = "@@ -1,4 +1,5 @@",
            Lines =
            [
                new DiffLine { Kind = DiffLineKind.Context, Content = "existing line" },
                new DiffLine { Kind = DiffLineKind.Removed, Content = "old behavior" },
                new DiffLine { Kind = DiffLineKind.Added, Content = "new behavior + diff render" },
                new DiffLine { Kind = DiffLineKind.Added, Content = "extra line to show collapse" },
            ],
        },
    ];

    /// <summary>
    /// 演示 Markdown（M7-4）— Demo 模拟回合完成消息的块模型，覆盖全 8 类块形态
    /// （heading/paragraph 行内样式/无序+有序 list/quote/code/rule/table）；
    /// 与 SimulatedPatch 同理直接手构块模型（Core 零依赖，不经 Markdig 投影器），
    /// 公开静态供 Demo SeedMessages 等演示构造复用。
    /// </summary>
    public static readonly MarkdownDocument DemoMarkdown = new(
    [
        new MarkdownHeading(2, [Seg("Markdown 渲染管线")]),
        new MarkdownParagraph(
        [
            Seg("Core 块模型 → 布局 → 渲染三层分离；"),
            Seg("行内代码", MarkdownStyle.Code),
            Seg(" 取 permission 色，"),
            Seg("粗体", MarkdownStyle.Bold),
            Seg(" / "),
            Seg("斜体", MarkdownStyle.Italic),
            Seg(" 组合 SGR。"),
        ]),
        new MarkdownList(false, 0,
        [
            new MarkdownListItem([new MarkdownParagraph([Seg("无序项 — 两空格缩进")])]),
            new MarkdownListItem([new MarkdownParagraph([Seg("第二项")])]),
        ]),
        new MarkdownList(true, 1,
        [
            new MarkdownListItem([new MarkdownParagraph([Seg("有序项 — 数字编号")])]),
            new MarkdownListItem([new MarkdownParagraph([Seg("第二项 — 自动递增")])]),
        ]),
        new MarkdownQuote([new MarkdownParagraph([Seg("引用 — ▎ 前缀 + 内容斜体（布局层叠加）。")])]),
        new MarkdownCode("csharp", "var doc = Markdown.Parse(text, Pipeline);\nvar blocks = MarkdigProjector.Project(doc);"),
        new MarkdownRule(),
        new MarkdownTable(
        [
            Cell(Seg("块形态", MarkdownStyle.Bold)),
            Cell(Seg("演示要点", MarkdownStyle.Bold)),
        ],
        [
            [Cell(Seg("代码块")), Cell(Seg("Language 原样保留，逐行不换行"))],
            [Cell(Seg("表格")), Cell(Seg("列宽 = max(显示宽, 3)"))],
        ]),
    ]);

    /// <summary>行内段速构（演示文档构造）。</summary>
    private static MarkdownSegment Seg(string text, MarkdownStyle style = MarkdownStyle.None) => new(text, style);

    /// <summary>表格单元格速构（深层嵌套集合表达式推断失败规避 — 对齐 MarkdownLayoutTests 先例）。</summary>
    private static IReadOnlyList<MarkdownSegment> Cell(params MarkdownSegment[] segments) => segments;

    /// <summary>默认时间源 — 墙钟毫秒。</summary>
    private static long DefaultClock() => Environment.TickCount64;

    /// <summary>创建模拟引擎（clock=时间源，毫秒）。</summary>
    public SimulatedEngine(Func<long>? clock = null) => _clock = clock ?? DefaultClock;

    /// <summary>进程内模拟 — 恒存活。</summary>
    public bool IsAlive => true;

    /// <summary>提交回合：忙时拒绝回 Error 诱导（对齐 EngineTurnClient 语义），否则入队 TurnStarted 并开始计时。</summary>
    public void Submit(string text)
    {
        if (_inTurn)
        {
            _pending.Enqueue(new BridgeEvent.Error("回合进行中，请等待完成或按 Esc 打断后再提交。"));
            return;
        }

        _inTurn = true;
        _stage = 0;
        _turnStart = _clock();
        _pending.Enqueue(new BridgeEvent.TurnStarted());
    }

    /// <summary>打断当前回合（闲时 no-op）。</summary>
    public void Interrupt()
    {
        if (!_inTurn)
        {
            return;
        }

        _inTurn = false;
        _pending.Enqueue(new BridgeEvent.TurnInterrupted());
    }

    /// <summary>权限模式本地立即回执（统一事件语义 — 与真实引擎回执路径一致）。</summary>
    public void SetPermissionMode(string mode) =>
        _pending.Enqueue(new BridgeEvent.PermissionModeChanged(mode));

    /// <summary>排空事件：先推进时间驱动状态机（产出到期迁移事件），再清空队列。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents()
    {
        AdvanceStages();
        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }

    /// <summary>按已流逝时间推进阶段状态机（到期迁移入队事件；Responding 阶段每拍 token 增量）。</summary>
    private void AdvanceStages()
    {
        if (!_inTurn)
        {
            return;
        }

        var elapsed = _clock() - _turnStart;
        if (_stage == 0 && elapsed >= ToolUseAtMs)
        {
            _stage = 1;
            _pending.Enqueue(new BridgeEvent.ToolCall("Edit", "demo/simulated.cs"));
        }

        if (_stage == 1 && elapsed >= RespondingAtMs)
        {
            _stage = 2;
            _pending.Enqueue(new BridgeEvent.ToolResult("Updated demo/simulated.cs (simulated)", SimulatedPatch));
        }

        if (_stage == 2 && elapsed >= CompletedAtMs)
        {
            _stage = 3;
            _inTurn = false;
            _pending.Enqueue(new BridgeEvent.AssistantMessage("演示回复 — markdown 全形态渲染管线运行正常。", DemoMarkdown));
            _pending.Enqueue(new BridgeEvent.TurnCompleted());
            return;
        }

        if (_stage == 2)
        {
            _pending.Enqueue(new BridgeEvent.TokenDelta(TokenDeltaPerTick));
        }
    }
}
