namespace NativeTui.Core.Bridge;

/// <summary>
/// 模拟引擎 — Demo 演示命令路由（md/diff/perm/all — 未知命令 Error 诱导）：
/// Submit 按命令选定回合脚本（TurnScript），时间驱动阶段状态机按脚本产出 BridgeEvent
/// （ToolCall/ToolResult（带原始输出多行折叠演示）/TokenDelta/PermissionRequest/
/// TokenUsage（回合完成用量汇总假数据）/TurnCompleted），
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

    /// <summary>流式分片队列（M8-1 逐字流式演示 — stage 2 进入时按完成文本预切分，Responding 每拍弹一段）。</summary>
    private readonly Queue<string> _streamSlices = new();

    /// <summary>子代理批量启动时刻（ms）— Submit 后一拍内全部 SubagentStarted（_pending 队列承载 300 规模）。</summary>
    private const long SubagentStartedAtMs = 200;

    /// <summary>子代理首个工具活动基准时刻（ms）— 后续按序号错开（i×25ms 交错演示窗口活动跟随）。</summary>
    private const long SubagentFirstToolAtMs = 500;

    /// <summary>子代理相邻工具活动间隔（ms）。</summary>
    private const long SubagentToolIntervalMs = 350;

    /// <summary>子代理每代理工具活动次数。</summary>
    private const int SubagentToolUsesPerAgent = 2;

    /// <summary>子代理完成时刻基准（ms）— 末次工具活动后 300ms 交错完成。</summary>
    private const long SubagentFinishBaseMs = SubagentFirstToolAtMs + SubagentToolUsesPerAgent * SubagentToolIntervalMs + 300;

    /// <summary>agents 默认子代理数量（不带参数）。</summary>
    private const int DefaultSubagentCount = 12;

    /// <summary>子代理类型池（轮转分配 — 混合类型组演示 hideType=false 形态与八色板多色）。</summary>
    private static readonly string[] SubagentTypes = ["explore", "reviewer", "builder"];

    /// <summary>子代理工具名池（轮转 — 活动行文本变化演示）。</summary>
    private static readonly string[] SubagentToolNames = ["Read", "Grep", "Edit"];

    /// <summary>权限演示工具名（对话框标题）。</summary>
    private const string PermissionToolName = "Bash";

    /// <summary>权限演示描述（对话框正文 — 说明将执行什么）。</summary>
    private const string PermissionDescription = "运行演示命令 demo/simulated.sh（模拟需确认的工具调用）";

    /// <summary>工具结果原始输出演示文本（6 行 — 多行输出折叠渲染演示：3 行预览 + 3 行隐藏）。</summary>
    private const string DemoToolRawOutput =
        "编译 demo/simulated.cs…完成\n运行测试 3/3 通过\n覆盖率 87.5%（+2.1%）\n生成性能基线快照\n格式化输出产物\n清理临时目录";

    /// <summary>工具结果折叠隐藏行数演示值（6 行 − 3 行预览 = 3 — 对齐渲染层折叠提示形态）。</summary>
    private const int DemoToolHiddenLines = 3;

    /// <summary>回合计时起点（clock 值）。</summary>
    private long _turnStart;

    /// <summary>已推进到的阶段（0=Thinking / 1=ToolUse / 2=Responding / 3=完成）。</summary>
    private int _stage;

    /// <summary>回合进行中。</summary>
    private bool _inTurn;

    /// <summary>当前回合演示脚本（Submit 命令路由产物 — 决定阶段迁移事件序列）。</summary>
    private TurnScript _script = TurnScript.Full;

    /// <summary>agents 脚本并行子代理列表（Submit 时建列表，200ms 批量发启动事件）。</summary>
    private List<SimulatedSubagent> _subagents = [];

    /// <summary>agents 脚本是否已发批量启动事件。</summary>
    private bool _subagentStartedSent;

    /// <summary>agents 脚本是否已发回合收束（全部完成后一次）。</summary>
    private bool _subagentTurnCompleted;

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

        /// <summary>agents — 并行子代理分组演示（M7-7）：批量启动 + 交错工具活动 + 交错完成（每第 7 个失败）。</summary>
        Subagents,
    }

    /// <summary>模拟子代理（agents 脚本并行子回合 — 独立时间线推进，区别于主回合三段状态机）。</summary>
    private sealed class SimulatedSubagent
    {
        /// <summary>序号（事件 id 与交错时刻基数）。</summary>
        public required int Index { get; init; }

        /// <summary>代理类型（类型池轮转）。</summary>
        public required string AgentType { get; init; }

        /// <summary>任务描述。</summary>
        public required string Description { get; init; }

        /// <summary>剩余工具活动次数。</summary>
        public int ToolUsesLeft { get; set; }

        /// <summary>下次工具活动时刻（ms，回合内偏移）。</summary>
        public long NextToolAtMs { get; set; }

        /// <summary>完成时刻（ms，回合内偏移）。</summary>
        public long FinishAtMs { get; init; }

        /// <summary>是否已完成。</summary>
        public bool IsDone { get; set; }
    }

    /// <summary>
    /// 模拟补丁 — Demo 演示 StructuredDiff 渲染链路（M7-2 + TASK066 F1 行号 gutter）：
    /// Removed/Added 行在前（红绿可见），Context 行殿后；行号真值演示（引擎 PatchLine 投影同语义 —
    /// Removed 带 old、Added 带 new、Context 双侧）+ 自洽 hunk 头（old 2 行 / new 3 行）。
    /// 4 行 hunk 全量渲染（≤ DiffResultItem 预览上限 10 行）。
    /// 公开静态供渲染可见性回归测试与 Demo 构造复用（对齐 DemoMarkdown 先例）。
    /// </summary>
    public static readonly DiffHunk[] SimulatedPatch =
    [
        new()
        {
            Header = "@@ -1,2 +1,3 @@",
            Lines =
            [
                new DiffLine { Kind = DiffLineKind.Removed, Content = "old behavior", OldLineNumber = 1 },
                new DiffLine { Kind = DiffLineKind.Added, Content = "new behavior + diff render", NewLineNumber = 1 },
                new DiffLine { Kind = DiffLineKind.Added, Content = "extra line to show collapse", NewLineNumber = 2 },
                new DiffLine { Kind = DiffLineKind.Context, Content = "existing line", OldLineNumber = 2, NewLineNumber = 3 },
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

    /// <summary>提交回合（带图片附件 — E4+G3 图片通道演示验证）：附件计数（SubmittedImageCount 供演示/测试
    /// 断言）+ 原文本路由单参 Submit（演示引擎无多模态语义 — 引擎消息模型 string-only，附件不进演示脚本）。</summary>
    /// <param name="text">提交文本（占位已由 shell 剥离）。</param>
    /// <param name="images">图片附件（null/空 = 等价单参提交，不计数）。</param>
    public void Submit(string text, IReadOnlyList<BridgeImageAttachment>? images)
    {
        SubmittedImageCount += images?.Count ?? 0;
        Submit(text);
    }

    /// <summary>开始新会话（G3 — D8 Clear 演示语义）：重置内部回合状态（在途回合中止 + 阶段/子代理列表/
    /// 流式分片清空 — 模拟引擎无跨回合消息序列，回合状态复位即新会话语义）；事件队列保序不清
    ///（在途事件如 Error 诱导仍可达下拍排空 — 新会话不吞已产出事件）。</summary>
    public void StartNewConversation()
    {
        _inTurn = false;
        _stage = 0;
        _subagents = [];
        _subagentStartedSent = false;
        _subagentTurnCompleted = false;
        _streamSlices.Clear();
    }

    /// <summary>收到图片附件总计数（G3 演示验证 — 图片重载提交累计，单参提交不增）。</summary>
    public int SubmittedImageCount { get; private set; }

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
        if (script is TurnScript.Subagents)
        {
            _subagents = CreateSubagents(ParseSubagentCount(text));
            _subagentStartedSent = false;
            _subagentTurnCompleted = false;
        }

        _pending.Enqueue(new BridgeEvent.TurnStarted());
    }

    /// <summary>演示命令路由（trim 后匹配，兼容斜杠前缀 /md 形态 — TUI 补全接受后提交带 /）— null = 未知命令；
    /// agents 支持 "agents" / "agents N"（N=1..999，非法后缀拒绝）。</summary>
    private static TurnScript? ParseCommand(string text)
    {
        var command = text.Trim();
        if (command.StartsWith('/'))
        {
            command = command[1..];
        }

        if (command == "agents" || (command.StartsWith("agents ", StringComparison.Ordinal) && int.TryParse(command["agents ".Length..], out var count) && count is >= 1 and <= 999))
        {
            return TurnScript.Subagents;
        }

        return command switch
        {
            "md" => TurnScript.Markdown,
            "diff" => TurnScript.Diff,
            "perm" => TurnScript.Permission,
            "all" => TurnScript.Full,
            "theme" => TurnScript.Markdown,
            "model" => TurnScript.Full,
            _ => null,
        };
    }

    /// <summary>解析子代理数量 — "agents" 默认 12；"agents N" 取 N（调用方已校验合法）。</summary>
    private static int ParseSubagentCount(string text)
    {
        var command = text.Trim().TrimStart('/');
        return int.TryParse(command["agents".Length..].Trim(), out var count) ? count : DefaultSubagentCount;
    }

    /// <summary>构造并行子代理列表（类型/工具名轮转 + 交错时间线 + 每 7 个一个失败演示组头红态）。</summary>
    private static List<SimulatedSubagent> CreateSubagents(int count) =>
        Enumerable.Range(0, count).Select(i => new SimulatedSubagent
        {
            Index = i,
            AgentType = SubagentTypes[i % SubagentTypes.Length],
            Description = $"并行任务 {i + 1}",
            ToolUsesLeft = SubagentToolUsesPerAgent,
            NextToolAtMs = SubagentFirstToolAtMs + i * 25,
            FinishAtMs = SubagentFinishBaseMs + i * 25,
        }).ToList();

    /// <summary>未知命令 Error 诱导 — 列出全部演示命令与各自用途（含怎么做，提示可输入 / 弹补全列表）。</summary>
    private static string UnknownCommandMessage(string input) =>
        $"未知演示命令「{input}」。可用命令：md（Markdown 全形态渲染）、diff（结构化补丁）、perm（权限对话框）、all（完整回合 = 补丁 + Markdown）、agents（并行子代理分组，可带数量如 agents 300）。可直接输入命令或输入 / 弹出补全列表选择。";

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

    /// <summary>按已流逝时间推进阶段状态机（阶段迁移按脚本分派事件；Responding 阶段非权限脚本每拍 token 增量；
    /// agents 脚本走独立并行子回合推进）。</summary>
    private void AdvanceStages()
    {
        if (!_inTurn)
        {
            return;
        }

        var elapsed = _clock() - _turnStart;
        if (_script is TurnScript.Subagents)
        {
            AdvanceSubagents(elapsed);
            return;
        }

        AdvanceThinking(elapsed);
        AdvanceToolUse(elapsed);
        AdvanceResponding(elapsed);
    }

    /// <summary>agents 脚本并行子回合推进：批量启动（一拍）→ 交错工具活动（各自时间线）→ 交错完成（每第 7 个失败）→ 全部完成收束回合。</summary>
    private void AdvanceSubagents(long elapsed)
    {
        if (!_subagentStartedSent && elapsed >= SubagentStartedAtMs)
        {
            _subagentStartedSent = true;
            foreach (var subagent in _subagents)
            {
                _pending.Enqueue(new BridgeEvent.SubagentStarted($"agent-{subagent.Index}", subagent.AgentType, subagent.Description));
            }
        }

        foreach (var subagent in _subagents.Where(s => !s.IsDone))
        {
            while (subagent.ToolUsesLeft > 0 && subagent.NextToolAtMs <= elapsed)
            {
                var useIndex = SubagentToolUsesPerAgent - subagent.ToolUsesLeft;
                _pending.Enqueue(new BridgeEvent.SubagentToolUse(
                    $"agent-{subagent.Index}",
                    SubagentToolNames[(subagent.Index + useIndex) % SubagentToolNames.Length],
                    $"demo/file{subagent.Index % 5}.cs",
                    TokenGain: 100 + (subagent.Index * 37) % 800));
                subagent.ToolUsesLeft--;
                subagent.NextToolAtMs += SubagentToolIntervalMs;
            }

            if (subagent.ToolUsesLeft == 0 && subagent.FinishAtMs <= elapsed)
            {
                subagent.IsDone = true;
                _pending.Enqueue(new BridgeEvent.SubagentFinished($"agent-{subagent.Index}", IsError: subagent.Index % 7 == 6, TokenTotal: 1200 + (subagent.Index * 311) % 9000));
            }
        }

        if (!_subagentTurnCompleted && _subagents.All(static subagent => subagent.IsDone))
        {
            _subagentTurnCompleted = true;
            _inTurn = false;
            var failures = _subagents.Count(static subagent => subagent.Index % 7 == 6);
            _pending.Enqueue(new BridgeEvent.AssistantMessage($"演示回复 — {_subagents.Count} 个子代理并行完成（{failures} 个失败）分组渲染链路运行正常。"));
            EnqueueDemoTokenUsage();
            _pending.Enqueue(new BridgeEvent.TurnCompleted());
        }
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
        // M8-1 逐字流式演示 — 完成文本按空格预分片，Responding 期每拍携带一段增量（对齐 MockServer word 流形态）
        _streamSlices.Clear();
        foreach (var slice in SliceBySpaces(CompletionText(_script))) {
            _streamSlices.Enqueue(slice);
        }
        if (_script is TurnScript.Full or TurnScript.Diff)
        {
            // 工具结果带原始输出（多行折叠演示 — 渲染层 3 行预览 + HiddenLines 折叠提示）
            _pending.Enqueue(new BridgeEvent.ToolResult(
                "Updated demo/simulated.cs (simulated)",
                SimulatedPatch,
                DemoToolRawOutput,
                DemoToolHiddenLines));
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
            EnqueueDemoTokenUsage();
            _pending.Enqueue(new BridgeEvent.TurnCompleted());
            return;
        }

        if (_stage == 2 && _script is not TurnScript.Permission)
        {
            // M8-1：分片队列非空发带文本增量（StreamingTextItem 逐字上屏），耗尽后回落纯计数形态
            if (_streamSlices.TryDequeue(out var slice)) {
                _pending.Enqueue(new BridgeEvent.TokenDelta(slice.Length, slice));
            } else {
                _pending.Enqueue(new BridgeEvent.TokenDelta(TokenDeltaPerTick));
            }
        }
    }

    /// <summary>按空格切分文本为流式分片（保留空格 — 逐段 Append 拼回原文；空文本零分片）。</summary>
    private static List<string> SliceBySpaces(string text) {
        var slices = new List<string>();
        if (string.IsNullOrEmpty(text)) {
            return slices;
        }

        var start = 0;
        while (start < text.Length) {
            var next = text.IndexOf(' ', start);
            if (next < 0) {
                slices.Add(text[start..]);
                break;
            }

            slices.Add(text[start..(next + 1)]);
            start = next + 1;
        }
        return slices;
    }

    /// <summary>完成时刻（ms）— 权限脚本 8s（交互窗口），其余 5.4s。</summary>
    private static long CompletionAtMs(TurnScript script) => script is TurnScript.Permission ? PermissionCompletedAtMs : CompletedAtMs;

    /// <summary>回合完成演示用量汇总 — 假数据（确定性常量）：input/output/context/contextLimit 四通道全形态
    /// （对齐真实引擎 OnDone usage 投影事件形态，S4 TokenUsageTracker 消费链路演示；
    /// context = input + output 语义自洽）。</summary>
    private void EnqueueDemoTokenUsage() =>
        _pending.Enqueue(new BridgeEvent.TokenUsage(
            InputTokens: 1284, OutputTokens: 356, ContextTokens: 1640, ContextLimitTokens: 131072));

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
