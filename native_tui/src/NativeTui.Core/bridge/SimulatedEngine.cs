namespace NativeTui.Core.Bridge;

/// <summary>
/// 模拟引擎 — Demo 演示命令路由（md/diff/perm/all — 未知命令 Error 诱导）：
/// Submit 按命令选定回合脚本（TurnScript），时间驱动阶段状态机按脚本产出 BridgeEvent
/// （ToolCall/ToolResult/TokenDelta/PermissionRequest/TurnCompleted），
/// 与真实 bridge 路径共用同一事件协议（ReplBridgeDriver 唯一翻译层）。
/// 时钟注入（默认墙钟）— 单元测试可注入虚拟时钟做确定性验证。
/// spinner Message 不经事件协议（真实路径空消息+随机动词采样，模拟直改属可弃糖）。
/// </summary>
public sealed class SimulatedEngine : IReplEngine
{
    /// <summary>Thinking → 下一阶段迁移时刻（ms）— 工具脚本产 ToolCall、权限脚本产 PermissionRequest。</summary>
    private const long ToolUseAtMs = 1200;

    /// <summary>ToolUse → Responding 迁移时刻（ms）— 工具脚本产 ToolResult + 补丁。</summary>
    private const long RespondingAtMs = 2400;

    /// <summary>回合完成时刻（ms）— md/diff/all 脚本。</summary>
    private const long CompletedAtMs = 5400;

    /// <summary>权限脚本回合完成时刻（ms）— 交互窗口加长（用户从容选择档位）。</summary>
    private const long PermissionCompletedAtMs = 8000;

    /// <summary>Responding 阶段每拍 token 增量。</summary>
    private const int TokenDeltaPerTick = 9;

    /// <summary>权限演示工具名（对话框标题）。</summary>
    private const string PermissionToolName = "Bash";

    /// <summary>权限演示描述（对话框正文 — 说明将执行什么）。</summary>
    private const string PermissionDescription = "运行演示命令 demo/simulated.sh（模拟需确认的工具调用）";

    /// <summary>回合计时起点（clock 值）。</summary>
    private long _turnStart;

    /// <summary>已推进到的阶段（0=Thinking / 1=ToolUse / 2=Responding / 3=完成）。</summary>
    private int _stage;

    /// <summary>回合进行中。</summary>
    private bool _inTurn;

    /// <summary>当前回合演示脚本（Submit 命令路由产物 — 决定阶段迁移事件序列）。</summary>
    private TurnScript _script = TurnScript.Full;

    /// <summary>待排空事件队列（Submit/Interrupt 立即入队，DrainEvents 阶段迁移追加）。</summary>
    private readonly Queue<BridgeEvent> _pending = new();

    /// <summary>时钟（默认墙钟 — 测试注入虚拟时钟）。</summary>
    private readonly Func<long> _clock;

    /// <summary>回合演示脚本 — Submit 命令路由产物，决定阶段迁移产出的事件序列。</summary>
    private enum TurnScript
    {
        /// <summary>md — 纯 Markdown 演示：无工具事件，完成消息携带全形态块模型。</summary>
        Markdown,

        /// <summary>diff — 纯 StructuredDiff 演示：ToolResult 带补丁，完成消息纯文本。</summary>
        Diff,

        /// <summary>perm — 权限对话框演示：1.2s PermissionRequest，挂起期无 token 流式，8s 延迟完成。</summary>
        Permission,

        /// <summary>all — 完整回合：补丁 + Markdown（既有默认演示）。</summary>
        Full,
    }

    /// <summary>
    /// 模拟补丁 — Demo 演示 StructuredDiff 渲染链路（M7-2）：Removed/Added 行在前（红绿可见），
    /// Context 行殿后；4 行 hunk 全量渲染（≤ DiffResultItem 预览上限 10 行）。
    /// 公开静态供渲染可见性回归测试与 Demo 构造复用（对齐 DemoMarkdown 先例）。
    /// </summary>
    public static readonly DiffHunk[] SimulatedPatch =
    [
        new()
        {
            Header = "@@ -1,4 +1,5 @@",
            Lines =
            [
                new DiffLine { Kind = DiffLineKind.Removed, Content = "old behavior" },
                new DiffLine { Kind = DiffLineKind.Added, Content = "new behavior + diff render" },
                new DiffLine { Kind = DiffLineKind.Added, Content = "extra line to show collapse" },
                new DiffLine { Kind = DiffLineKind.Context, Content = "existing line" },
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

    /// <summary>提交回合：忙时拒绝回 Error 诱导（对齐 EngineTurnClient 语义）；未知命令拒绝回 Error 诱导
    /// （列出全部演示命令与用途 — 含为什么拒绝/怎么做）；有效命令选定脚本、入队 TurnStarted 并开始计时。</summary>
    public void Submit(string text)
    {
        if (_inTurn)
        {
            _pending.Enqueue(new BridgeEvent.Error("回合进行中，请等待完成或按 Esc 打断后再提交。"));
            return;
        }

        if (ParseCommand(text) is not { } script)
        {
            _pending.Enqueue(new BridgeEvent.Error(UnknownCommandMessage(text)));
            return;
        }

        _script = script;
        _inTurn = true;
        _stage = 0;
        _turnStart = _clock();
        _pending.Enqueue(new BridgeEvent.TurnStarted());
    }

    /// <summary>演示命令路由（trim 后精确匹配）— null = 未知命令。</summary>
    private static TurnScript? ParseCommand(string text) => text.Trim() switch
    {
        "md" => TurnScript.Markdown,
        "diff" => TurnScript.Diff,
        "perm" => TurnScript.Permission,
        "all" => TurnScript.Full,
        _ => null,
    };

    /// <summary>未知命令 Error 诱导 — 列出全部演示命令与各自用途（含怎么做）。</summary>
    private static string UnknownCommandMessage(string input) =>
        $"未知演示命令「{input}」。可用命令：md（Markdown 全形态渲染）、diff（结构化补丁）、perm（权限对话框）、all（完整回合 = 补丁 + Markdown）。请输入上述命令之一后回车。";

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

    /// <summary>按已流逝时间推进阶段状态机（阶段迁移按脚本分派事件；Responding 阶段非权限脚本每拍 token 增量）。</summary>
    private void AdvanceStages()
    {
        if (!_inTurn)
        {
            return;
        }

        var elapsed = _clock() - _turnStart;
        AdvanceThinking(elapsed);
        AdvanceToolUse(elapsed);
        AdvanceResponding(elapsed);
    }

    /// <summary>Thinking → 下一阶段（1.2s 到点）：工具脚本产 ToolCall，权限脚本产 PermissionRequest，md 无事件直入。</summary>
    private void AdvanceThinking(long elapsed)
    {
        if (_stage != 0 || elapsed < ToolUseAtMs)
        {
            return;
        }

        _stage = 1;
        if (_script is TurnScript.Full or TurnScript.Diff)
        {
            _pending.Enqueue(new BridgeEvent.ToolCall("Edit", "demo/simulated.cs"));
        }
        else if (_script is TurnScript.Permission)
        {
            _pending.Enqueue(new BridgeEvent.PermissionRequest(PermissionToolName, PermissionDescription));
        }
    }

    /// <summary>ToolUse → Responding（2.4s 到点）：工具脚本产 ToolResult + 结构化补丁，其余脚本无事件。</summary>
    private void AdvanceToolUse(long elapsed)
    {
        if (_stage != 1 || elapsed < RespondingAtMs)
        {
            return;
        }

        _stage = 2;
        if (_script is TurnScript.Full or TurnScript.Diff)
        {
            _pending.Enqueue(new BridgeEvent.ToolResult("Updated demo/simulated.cs (simulated)", SimulatedPatch));
        }
    }

    /// <summary>Responding → 完成（脚本完成时刻到点）：完成消息（md/all 携带 Markdown 块模型）+ TurnCompleted；
    /// 权限脚本挂起期无 token 流式（权限等待语义），其余脚本每拍 token 增量。</summary>
    private void AdvanceResponding(long elapsed)
    {
        if (_stage == 2 && elapsed >= CompletionAtMs(_script))
        {
            _stage = 3;
            _inTurn = false;
            _pending.Enqueue(new BridgeEvent.AssistantMessage(CompletionText(_script), CompletionMarkdown(_script)));
            _pending.Enqueue(new BridgeEvent.TurnCompleted());
            return;
        }

        if (_stage == 2 && _script is not TurnScript.Permission)
        {
            _pending.Enqueue(new BridgeEvent.TokenDelta(TokenDeltaPerTick));
        }
    }

    /// <summary>完成时刻（ms）— 权限脚本 8s（交互窗口），其余 5.4s。</summary>
    private static long CompletionAtMs(TurnScript script) => script is TurnScript.Permission ? PermissionCompletedAtMs : CompletedAtMs;

    /// <summary>完成消息文本（按脚本语义区分）。</summary>
    private static string CompletionText(TurnScript script) => script switch
    {
        TurnScript.Diff => "演示回复 — StructuredDiff 补丁渲染链路运行正常。",
        TurnScript.Permission => "演示回复 — 权限对话框交互完成（挂起期无 token 流式，回合正常收束）。",
        TurnScript.Markdown => "演示回复 — Markdown 全形态渲染管线运行正常。",
        _ => "演示回复 — 完整回合（补丁 + Markdown）运行正常。",
    };

    /// <summary>完成消息 Markdown 块模型 — md/all 携带全形态演示文档，diff/perm 纯文本。</summary>
    private static MarkdownDocument? CompletionMarkdown(TurnScript script) =>
        script is TurnScript.Markdown or TurnScript.Full ? DemoMarkdown : null;
}
