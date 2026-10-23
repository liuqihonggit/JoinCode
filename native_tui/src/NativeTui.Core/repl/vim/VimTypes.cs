namespace NativeTui.Core.Repl.Vim;

/// <summary>
/// vim 算子 — 基准 src/vim/types.ts:33 `Operator = 'delete' | 'change' | 'yank'` 三值
///（粘贴 p/P、删除字符 x、替换 r 等是独立命令，不属于 operator 组合体系，见 VimEngine）。
/// </summary>
public enum VimOperator
{
    /// <summary>删除（d — d{motion} / dd / x 类行内删除）。</summary>
    Delete,

    /// <summary>修改（c — 删除后进 INSERT；cw 特例改到词尾，operators.ts:441-450）。</summary>
    Change,

    /// <summary>复制（y — 文本不变，光标移到范围起点，operators.ts:507-508）。</summary>
    Yank,
}

/// <summary>
/// 字符查找方向/语义 — 基准 src/vim/types.ts:35 `FindType = 'f' | 'F' | 't' | 'T'`
///（f 正向到字符 / F 反向到字符 / t 正向贴字符前 / T 反向贴字符后，Cursor.ts:1062-1105）。
/// </summary>
public enum VimFindKind
{
    /// <summary>f：正向查找到目标字符（光标落在目标上）。</summary>
    ToForward,

    /// <summary>F：反向查找到目标字符。</summary>
    ToBackward,

    /// <summary>t：正向查找贴在目标字符前（光标落在目标前一 grapheme）。</summary>
    TillForward,

    /// <summary>T：反向查找贴在目标字符后。</summary>
    TillBackward,
}

/// <summary>
/// text object 范围语义 — 基准 src/vim/types.ts:37 `TextObjScope = 'inner' | 'around'`
///（i 取对象内部 / a 含界定符或周边空白，textObjects.ts:38-58）。
/// </summary>
public enum VimTextObjectScope
{
    /// <summary>inner（i 前缀）：引号/括号对象不含界定符本身，词对象仅词本身。</summary>
    Inner,

    /// <summary>around（a 前缀）：含界定符；词对象含尾随（或前导）空白，textObjects.ts:106-113。</summary>
    Around,
}

/// <summary>
/// 缩进方向 — 基准 src/vim/types.ts:75 `indent 状态 dir: '&gt;' | '&lt;'`（&gt;&gt; 增两空格 / &lt;&lt; 减）。
/// </summary>
public enum VimIndentDirection
{
    /// <summary>&gt;（&gt;&gt; 命令）：行首加两空格（operators.ts:363-364）。</summary>
    Increase,

    /// <summary>&lt;（&lt;&lt; 命令）：行首去缩进（两空格/一个 Tab/至多两空白，operators.ts:365-382）。</summary>
    Decrease,
}

/// <summary>
/// NORMAL 模式命令状态机状态种类 — 基准 src/vim/types.ts:59-75 CommandState 判别联合的 11 个 type
///（状态图见 types.ts:7-26：idle→count/operator/find/g/replace/indent，operator→operatorCount/operatorFind/operatorTextObj/operatorG）。
/// </summary>
public enum VimCommandKind
{
    /// <summary>空闲 — 等待新命令起点（types.ts:60）。</summary>
    Idle,

    /// <summary>收集数量前缀数字（types.ts:61）。</summary>
    Count,

    /// <summary>算子待定 — 等待 motion/text-object/find（dw/ciw/yf 组合的 d，types.ts:62）。</summary>
    Operator,

    /// <summary>算子后的第二段数量前缀（2d3w 的 3，types.ts:63）。</summary>
    OperatorCount,

    /// <summary>算子+查找待定 — 等待目标字符（dfx 的 x 位置，types.ts:64）。</summary>
    OperatorFind,

    /// <summary>算子+text-object 待定 — 已按 i/a，等待对象类型（diw 的 w，types.ts:65-70）。</summary>
    OperatorTextObj,

    /// <summary>裸查找待定 — 等待目标字符（fx 的 x，types.ts:71）。</summary>
    Find,

    /// <summary>g 前缀待定 — 等待 g/j/k（gg/gj/gk，types.ts:72）。</summary>
    G,

    /// <summary>算子+g 前缀待定 — 等待 g/j/k（dgg/dgj，types.ts:73）。</summary>
    OperatorG,

    /// <summary>替换字符待定 — 等待替换目标字符（r{char}，types.ts:74）。</summary>
    Replace,

    /// <summary>缩进待定 — 等待方向重复（&gt;&gt;/&lt;&lt;，types.ts:75）。</summary>
    Indent,
}

/// <summary>
/// NORMAL 模式命令状态 — 基准 src/vim/types.ts:59-75 CommandState 判别联合的扁平化载体：
/// Kind 判别状态种类，其余字段按 Kind 取用（TS 判别联合字段到 C# record 的同构映射）。
/// 不可变值类型，状态转移通过工厂构造新实例（transition 返回 next，useVimInput.ts:280-286 应用）。
/// </summary>
public readonly record struct VimCommand
{
    /// <summary>状态种类（判别字段）。</summary>
    public VimCommandKind Kind { get; init; }

    /// <summary>算子（Operator/OperatorCount/OperatorFind/OperatorTextObj/OperatorG 态有效）。</summary>
    public VimOperator Op { get; init; }

    /// <summary>数量前缀（count 缺省语义为 1 — 基准 handleNormalInput 从 idle 进入时传 1，
    /// transitions.ts:259；Count 态用 <see cref="Digits"/> 字符串收集）。</summary>
    public int Count { get; init; }

    /// <summary>数量前缀数字串（Count/OperatorCount 态有效 — 基准 digits: string）。</summary>
    public string Digits { get; init; }

    /// <summary>查找种类（Find/OperatorFind 态有效）。</summary>
    public VimFindKind Find { get; init; }

    /// <summary>text-object 范围（OperatorTextObj 态有效）。</summary>
    public VimTextObjectScope Scope { get; init; }

    /// <summary>缩进方向（Indent 态有效）。</summary>
    public VimIndentDirection IndentDirection { get; init; }

    /// <summary>空闲状态工厂（初始态 — 基准 command: { type: 'idle' }）。</summary>
    public static VimCommand Idle() => new() { Kind = VimCommandKind.Idle };

    /// <summary>数量前缀收集态工厂（fromIdle 首数字进入，transitions.ts:250-252）。</summary>
    public static VimCommand OfCount(string digits) => new() { Kind = VimCommandKind.Count, Digits = digits };

    /// <summary>算子待定态工厂（handleNormalInput 算子键进入，transitions.ts:103-105）。</summary>
    public static VimCommand OfOperator(VimOperator op, int count) =>
        new() { Kind = VimCommandKind.Operator, Op = op, Count = count };

    /// <summary>算子后数量收集态工厂（fromOperator 数字进入，transitions.ts:293-302）。</summary>
    public static VimCommand OfOperatorCount(VimOperator op, int count, string digits) =>
        new() { Kind = VimCommandKind.OperatorCount, Op = op, Count = count, Digits = digits };

    /// <summary>算子+查找待定态工厂（handleOperatorInput 查找键进入，transitions.ts:223-228）。</summary>
    public static VimCommand OfOperatorFind(VimOperator op, int count, VimFindKind find) =>
        new() { Kind = VimCommandKind.OperatorFind, Op = op, Count = count, Find = find };

    /// <summary>算子+text-object 待定态工厂（handleOperatorInput i/a 进入，transitions.ts:212-220）。</summary>
    public static VimCommand OfOperatorTextObj(VimOperator op, int count, VimTextObjectScope scope) =>
        new() { Kind = VimCommandKind.OperatorTextObj, Op = op, Count = count, Scope = scope };

    /// <summary>裸查找待定态工厂（handleNormalInput f/F/t/T 进入，transitions.ts:116-118）。</summary>
    public static VimCommand OfFind(VimFindKind find, int count) =>
        new() { Kind = VimCommandKind.Find, Find = find, Count = count };

    /// <summary>g 前缀待定态工厂（handleNormalInput g 进入，transitions.ts:120）。</summary>
    public static VimCommand OfG(int count) => new() { Kind = VimCommandKind.G, Count = count };

    /// <summary>算子+g 前缀待定态工厂（handleOperatorInput g 进入，transitions.ts:237-239）。</summary>
    public static VimCommand OfOperatorG(VimOperator op, int count) =>
        new() { Kind = VimCommandKind.OperatorG, Op = op, Count = count };

    /// <summary>替换字符待定态工厂（handleNormalInput r 进入，transitions.ts:121）。</summary>
    public static VimCommand OfReplace(int count) => new() { Kind = VimCommandKind.Replace, Count = count };

    /// <summary>缩进待定态工厂（handleNormalInput &gt;/&lt; 进入，transitions.ts:122-124）。</summary>
    public static VimCommand OfIndent(VimIndentDirection dir, int count) =>
        new() { Kind = VimCommandKind.Indent, IndentDirection = dir, Count = count };
}

/// <summary>
/// 最近一次查找记忆 — 基准 src/vim/types.ts:83 `lastFind: { type: FindType; char: string } | null`
///（;/, 重复查找的数据源，transitions.ts:465-490）。
/// Char 由调用方保证非 null（键入字符或空串）；default 实例 Char 为 null — 消费方经可空性判空后使用。
/// </summary>
/// <param name="Kind">查找种类（f/F/t/T）。</param>
/// <param name="Char">目标字符（单键字符；空串表示查找未命中目标）。</param>
public readonly record struct VimLastFind(VimFindKind Kind, string Char);

/// <summary>
/// dot-repeat 记录的变更种类 — 基准 src/vim/types.ts:92-119 RecordedChange 判别联合的 type 值
///（'insert'/'operator'/'operatorTextObj'/'operatorFind'/'replace'/'x'/'toggleCase'/'indent'/'openLine'/'join'）。
/// </summary>
public enum VimRecordedChangeKind
{
    /// <summary>INSERT 期间键入的文本（Esc 时整段记录，useVimInput.ts:63-68；replay 整段重插）。</summary>
    Insert,

    /// <summary>算子+motion（dw 类；dd/cc/yy 的 lineOp 也记此形态 — motion 为 None 占位，见 VimMotion.None 注释）。</summary>
    Operator,

    /// <summary>算子+text-object（ciw 类）。</summary>
    OperatorTextObj,

    /// <summary>算子+查找（dfx 类）。</summary>
    OperatorFind,

    /// <summary>替换字符（r{char}）。</summary>
    ReplaceChar,

    /// <summary>删除光标处字符（x）。</summary>
    DeleteChar,

    /// <summary>翻转大小写（~）。</summary>
    ToggleCase,

    /// <summary>缩进（&gt;&gt;/&lt;&lt;）。</summary>
    Indent,

    /// <summary>开新行（o/O）。</summary>
    OpenLine,

    /// <summary>合并行（J）。</summary>
    Join,
}

/// <summary>
/// dot-repeat 记录 — 基准 src/vim/types.ts:92-119 RecordedChange 判别联合的扁平化载体：
/// Kind 判别变体，其余字段按变体取用；由算子执行时 recordChange 写入（operators.ts:53 等）、
/// '.' 键整段重放（useVimInput.ts:109-173 replayLastChange switch 分发）。
/// 不可变；replay 期间不回写（isReplay 短路，useVimInput.ts:101-105）。
/// </summary>
public sealed record VimRecordedChange
{
    /// <summary>变体判别字段。</summary>
    public VimRecordedChangeKind Kind { get; init; }

    /// <summary>Insert 变体：键入的文本。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Operator/OperatorTextObj/OperatorFind 变体：算子种类。</summary>
    public VimOperator Op { get; init; }

    /// <summary>Operator 变体：motion（lineOp 记 None 占位 — 对齐基准 motion: op[0] 重放失效语义）。</summary>
    public VimMotion Motion { get; init; }

    /// <summary>OperatorTextObj 变体：对象类型键（w/W/"/'/`/(/)/b/[/]/{/}/B/&lt;/&gt;）。</summary>
    public char ObjType { get; init; }

    /// <summary>OperatorTextObj 变体：范围语义（inner/around）。</summary>
    public VimTextObjectScope Scope { get; init; }

    /// <summary>OperatorFind/ReplaceChar 变体：目标/替换字符。</summary>
    public string Char { get; init; } = string.Empty;

    /// <summary>OperatorFind 变体：查找种类。</summary>
    public VimFindKind Find { get; init; }

    /// <summary>数量前缀（x/~/J/replace/indent/operator 系）。</summary>
    public int Count { get; init; }

    /// <summary>Indent 变体：方向。</summary>
    public VimIndentDirection IndentDirection { get; init; }

    /// <summary>OpenLine 变体：开行方向（o=下方 / O=上方）。</summary>
    public bool Below { get; init; }
}

/// <summary>
/// 效果种类 — HandleKey 返回给 ReplShell 的接线动作（基准副作用出口的显式化）：
/// 基准经 textInput.onInput 透传（useVimInput.ts:184-187/204-207/227）、setText/setOffset 回写
///（operators.ts:26-37 OperatorContext）、enterInsert 切模式（useVimInput.ts:49-59）、
/// onUndo 委托（transitions.ts:165-167）。
/// </summary>
public enum VimEffectKind
{
    /// <summary>无外部动作（纯内部状态变化，如 NORMAL 模式吞掉无效键）。</summary>
    None,

    /// <summary>INSERT 透传：原键转发给 InputBuffer 实际编辑（Ctrl 快捷/Enter 提交/INSERT 字符/idle 方向键历史导航），
    /// 宿主处理后需 <c>Sync</c> 回引擎同步镜像。</summary>
    PassThrough,

    /// <summary>NORMAL 编辑写回：文本与光标整体替换（宿主调 InputBuffer.ReplaceAt）。</summary>
    ApplyText,

    /// <summary>NORMAL 纯光标移动（宿主同步 InputBuffer 光标，不动文本）。</summary>
    MoveCursor,

    /// <summary>进入 INSERT：光标定位到指定偏移并切换模式形态（宿主停用 vim 拦截、状态栏显示 -- INSERT --）。</summary>
    EnterInsert,

    /// <summary>撤销请求：u 键委托宿主 undo（对齐基准 onUndo 委托 props，transitions.ts:165-167）。</summary>
    UndoRequested,
}

/// <summary>
/// 单条接线效果 — <see cref="VimEffectKind"/> 的载荷载体（Key 仅 PassThrough 有效；
/// Text 仅 ApplyText 有效；Offset 对 ApplyText/MoveCursor/EnterInsert 有效）。
/// </summary>
/// <param name="Kind">效果种类。</param>
/// <param name="Key">PassThrough：原按键（宿主转发给 InputBuffer）。</param>
/// <param name="Text">ApplyText：新文本全量。</param>
/// <param name="Offset">ApplyText/MoveCursor/EnterInsert：目标光标偏移（文本内 0..len）。</param>
public sealed record VimEffect(VimEffectKind Kind, KeyPress? Key = null, string? Text = null, int Offset = 0)
{
    /// <summary>PassThrough 效果工厂。</summary>
    public static VimEffect Pass(KeyPress key) => new(VimEffectKind.PassThrough, Key: key);

    /// <summary>ApplyText 效果工厂（NORMAL 编辑写回）。</summary>
    public static VimEffect Apply(string text, int offset) =>
        new(VimEffectKind.ApplyText, Text: text, Offset: offset);

    /// <summary>MoveCursor 效果工厂（NORMAL 纯移动）。</summary>
    public static VimEffect Move(int offset) => new(VimEffectKind.MoveCursor, Offset: offset);

    /// <summary>EnterInsert 效果工厂（切 INSERT 形态）。</summary>
    public static VimEffect Insert(int offset) => new(VimEffectKind.EnterInsert, Offset: offset);

    /// <summary>UndoRequested 效果工厂。</summary>
    public static VimEffect Undo() => new(VimEffectKind.UndoRequested);
}

/// <summary>
/// vim 数量前缀上限 — 基准 src/vim/types.ts:182 `MAX_VIM_COUNT = 10000`（count 收集钳制，transitions.ts:272/322）。
/// </summary>
public static class VimLimits
{
    /// <summary>数量前缀解析上限（超出按 10000 计）。</summary>
    public const int MaxCount = 10000;
}
