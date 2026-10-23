namespace NativeTui.Core.Repl.Vim;

/// <summary>
/// vim 编辑模式纯逻辑引擎 — 基准 src/vim/{types,motions,operators,textObjects,transitions}.ts 四模块 +
/// src/hooks/useVimInput.ts 消费层的合并收敛体（状态机 + 镜像 + 效果产出）。
/// 职责边界：
/// ① 引擎自持文本镜像与光标偏移（全文 char 偏移 0..len，与 InputBuffer.CursorCol 同构），
/// 不触碰 InputBuffer — 宿主经 <see cref="Sync"/> 回写镜像、经 <see cref="HandleKey"/> 返回的
/// <see cref="VimEffect"/> 接线（PassThrough 转发原键 / ApplyText 写回 / EnterInsert 切形态 / UndoRequested 委托）；
/// ② INSERT 模式不编辑文本 — 透传原键给宿主（基准 useVimInput.ts:184-227），引擎仅跟踪 insertedText
/// 供 dot-repeat；NORMAL 模式按键驱动命令状态机（基准 transitions.ts）；
/// ③ 模式仅 Insert/Normal 两态（基准 textInputTypes.ts:222），无 Visual/Replace 模式。
/// 键模型复用 KeyPress（对齐基准 ink 的 (input, key) 二元组语义）。
/// </summary>
public sealed class VimEngine
{
    private VimMode _mode = VimMode.Insert; // 基准 createInitialVimState 返回 INSERT（types.ts:188-190）
    private VimCommand _command = VimCommand.Idle();
    private readonly VimRegister _register = new();
    private VimRecordedChange? _lastChange;
    private VimLastFind? _lastFind;
    private string _text = string.Empty;
    private int _offset;
    private string _insertedText = string.Empty;

    /// <summary>创建空文本引擎（INSERT 起步 — 对齐基准初始态）。</summary>
    public VimEngine()
    {
    }

    /// <summary>创建指定镜像的引擎（测试/恢复用）。</summary>
    /// <param name="text">初始文本镜像。</param>
    /// <param name="offset">初始光标偏移（钳制到文本界）。</param>
    /// <param name="mode">初始模式（默认 Insert — 基准初始态）。</param>
    public VimEngine(string text, int offset, VimMode mode = VimMode.Insert)
    {
        _text = text ?? string.Empty;
        _offset = Math.Clamp(offset, 0, _text.Length);
        _mode = mode;
    }

    /// <summary>工厂：指定文本/光标/模式。</summary>
    public static VimEngine Of(string text, int offset, VimMode mode = VimMode.Insert) =>
        new(text, offset, mode);

    /// <summary>当前模式（INSERT/NORMAL）。</summary>
    public VimMode Mode => _mode;

    /// <summary>光标偏移（文本内 0..len — 与 InputBuffer.CursorCol 同构）。</summary>
    public int Cursor => _offset;

    /// <summary>文本镜像（引擎侧真身 — NORMAL 编辑直接改写，INSERT 透传经 Sync 回写）。</summary>
    public string Text => _text;

    /// <summary>模式指示串 — 基准仅 INSERT 显示 "-- INSERT --"（PromptInputFooterLeftSide.tsx:191，dim 色）；NORMAL 为空串。</summary>
    public string StatusLine => _mode == VimMode.Insert ? "-- INSERT --" : string.Empty;

    /// <summary>NORMAL 命令状态机当前状态（pending operator/count 等观察口）。</summary>
    public VimCommand Command => _command;

    /// <summary>无名寄存器（yank/paste 数据通道）。</summary>
    public VimRegister Register => _register;

    /// <summary>INSERT 期间累计键入文本（dot-repeat insert 记录源 — Esc 时落账）。</summary>
    public string InsertedText => _insertedText;

    /// <summary>最近一次查找记忆（;/, 重复源）。</summary>
    public VimLastFind? LastFind => _lastFind;

    /// <summary>最近一次变更记录（dot-repeat 源）。</summary>
    public VimRecordedChange? LastChange => _lastChange;

    /// <summary>视觉行折行内容宽（gj/gk 用 — 对齐基准 columns 折行语义；宿主接终端宽-PromptWidth）。</summary>
    public int WrapWidth { get; set; } = 80;

    /// <summary>
    /// 宿主回写镜像 — INSERT 透传键经 InputBuffer 实际编辑后，宿主把结果同步回引擎
    ///（基准 useVimInput 的 text/offset 即 props.value/textInput.offset，每次按键实时读宿主真身；
    /// C# 引擎持镜像，Sync 是宿主真身 → 镜像的通道）。
    /// </summary>
    /// <param name="text">宿主当前文本。</param>
    /// <param name="offset">宿主当前光标偏移。</param>
    public void Sync(string text, int offset)
    {
        _text = text ?? string.Empty;
        _offset = Math.Clamp(offset, 0, _text.Length);
    }

    /// <summary>
    /// 处理按键 — 基准 useVimInput.ts:175-295 handleVimInput 的纯逻辑化：
    /// Ctrl 任意模式透传（184-187）；Esc 切 NORMAL/取消 pending（192-201）；Enter 任意模式透传（204-207）；
    /// INSERT 跟踪 insertedText 后透传（209-229）；NORMAL idle 方向键透传历史导航（235-243）；
    /// 其余 NORMAL 键方向键/退格映射 motion 后进命令状态机（264-286）。
    /// </summary>
    /// <param name="key">解析后的按键事件。</param>
    /// <returns>接线效果序列（宿主按序执行；PassThrough 处理后需 Sync 回镜像）。</returns>
    public IReadOnlyList<VimEffect> HandleKey(KeyPress key)
    {
        var effects = new List<VimEffect>();

        if (key.Ctrl)
        {
            effects.Add(VimEffect.Pass(key)); // 基准 184-187：ctrl 快捷两模式都走宿主 base handler
            return effects;
        }

        if (key.Kind == KeyKind.Escape)
        {
            HandleEscape(effects);
            return effects;
        }

        if (key.Kind == KeyKind.Return)
        {
            effects.Add(VimEffect.Pass(key)); // 基准 204-207：Enter 允许 NORMAL 模式直接提交
            return effects;
        }

        if (_mode == VimMode.Insert)
        {
            TrackInsertedText(key); // 基准 209-229：dot-repeat 跟踪
            effects.Add(VimEffect.Pass(key));
            return effects;
        }

        // NORMAL idle 下方向键委托宿主（历史导航回退 — 基准 235-243）
        if (_command.Kind == VimCommandKind.Idle
            && key.Kind is KeyKind.UpArrow or KeyKind.DownArrow or KeyKind.LeftArrow or KeyKind.RightArrow)
        {
            effects.Add(VimEffect.Pass(key));
            return effects;
        }

        var input = MapNormalInput(key);
        var (next, executed) = Transition(_command, input, effects);

        // execute 可能已切 INSERT（enterInsert）— 此时命令状态由 EnterInsert 重置，不再覆盖（基准 279-286）
        if (_mode == VimMode.Normal)
        {
            if (next is { } command)
            {
                _command = command;
            }
            else if (executed)
            {
                _command = VimCommand.Idle();
            }
        }

        return effects;
    }

    /// <summary>
    /// Esc 处理 — 基准 useVimInput.ts:192-201：
    /// INSERT → switchToNormalMode（记 dot-repeat insert + 光标左移一格 + 切模式）；
    /// NORMAL → 取消任何 pending 命令回 idle。
    /// </summary>
    private void HandleEscape(List<VimEffect> effects)
    {
        if (_mode == VimMode.Insert)
        {
            SwitchToNormal(effects);
            return;
        }

        _command = VimCommand.Idle();
    }

    /// <summary>
    /// INSERT → NORMAL — 基准 useVimInput.ts:61-80 switchToNormalMode：
    /// 键入文本非空时记 dot-repeat insert；光标左移一格（char 级 -1，行首/偏移 0 例外 —
    /// 基准 offset-1 语义）；模式切换并重置命令状态。
    /// </summary>
    private void SwitchToNormal(List<VimEffect> effects)
    {
        if (_insertedText.Length > 0)
        {
            _lastChange = new VimRecordedChange { Kind = VimRecordedChangeKind.Insert, Text = _insertedText };
        }

        if (_offset > 0 && _text[_offset - 1] != '\n')
        {
            _offset--;
            effects.Add(VimEffect.Move(_offset));
        }

        _mode = VimMode.Normal;
        _command = VimCommand.Idle();
        _insertedText = string.Empty;
    }

    /// <summary>
    /// INSERT 键入跟踪 — 基准 useVimInput.ts:211-228：
    /// Backspace/Delete 剥末尾 grapheme；其余累计 input（功能键 input 空 → 不变）。
    /// </summary>
    private void TrackInsertedText(KeyPress key)
    {
        if (key.Kind is KeyKind.Backspace or KeyKind.Delete)
        {
            if (_insertedText.Length > 0)
            {
                _insertedText = _insertedText[..^VimMotions.LastGraphemeLength(_insertedText)];
            }
        }
        else
        {
            _insertedText += key.Input;
        }
    }

    /// <summary>
    /// NORMAL 键映射 — 基准 useVimInput.ts:257-271：
    /// 方向键映射 h/l/k/j；Backspace 在 motion 等待态（idle/count/operator/operatorCount）映射 h；
    /// Delete 在 motion 等待态且非 count 态映射 x（count 态 Delete 不执行破坏性 Nx）；
    /// literal-char 态（find/replace/operatorFind 等）收到空 input 由各状态自行取消/吞掉。
    /// </summary>
    private string MapNormalInput(KeyPress key)
    {
        var expectsMotion = _command.Kind is VimCommandKind.Idle or VimCommandKind.Count
            or VimCommandKind.Operator or VimCommandKind.OperatorCount;
        return key.Kind switch
        {
            KeyKind.LeftArrow => "h",
            KeyKind.RightArrow => "l",
            KeyKind.UpArrow => "k",
            KeyKind.DownArrow => "j",
            KeyKind.Backspace when expectsMotion => "h",
            KeyKind.Delete when expectsMotion && _command.Kind != VimCommandKind.Count => "x",
            _ => key.Input,
        };
    }

    /// <summary>
    /// 命令状态机分发 — 基准 transitions.ts:59-88 transition（按状态种类查转移函数）。
    /// 返回 (next?, executed)：next 非空 → 转移到该状态；executed → 已执行命令（无 next 时回 idle）；
    /// 双空 → 无效键吞掉（保持当前状态 — 基准返回 {} 的分支）。
    /// </summary>
    private (VimCommand? Next, bool Executed) Transition(VimCommand state, string input, List<VimEffect> effects) =>
        state.Kind switch
        {
            VimCommandKind.Idle => FromIdle(input, effects),
            VimCommandKind.Count => FromCount(state, input, effects),
            VimCommandKind.Operator => FromOperator(state, input, effects),
            VimCommandKind.OperatorCount => FromOperatorCount(state, input, effects),
            VimCommandKind.OperatorFind => FromOperatorFind(state, input, effects),
            VimCommandKind.OperatorTextObj => FromOperatorTextObj(state, input, effects),
            VimCommandKind.Find => FromFind(state, input, effects),
            VimCommandKind.G => FromG(state, input, effects),
            VimCommandKind.OperatorG => FromOperatorG(state, input, effects),
            VimCommandKind.Replace => FromReplace(state, input, effects),
            VimCommandKind.Indent => FromIndent(state, input, effects),
            _ => (null, false),
        };

    /// <summary>
    /// idle 态转移 — 基准 transitions.ts:248-263 fromIdle：
    /// [1-9] 进 count 收集；'0' 是行首 motion（非 count 前缀）；其余按 count=1 走 handleNormalInput。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromIdle(string input, List<VimEffect> effects)
    {
        if (input.Length == 1 && input[0] is >= '1' and <= '9')
        {
            return (VimCommand.OfCount(input), false);
        }

        if (input == "0")
        {
            MoveCursorTo(VimMotions.LineStart(_text, _offset), effects);
            return (null, true);
        }

        return HandleNormalInput(input, count: 1, effects);
    }

    /// <summary>
    /// count 态转移 — 基准 transitions.ts:265-281 fromCount：
    /// 数字继续累计（MAX_VIM_COUNT 钳制）；否则按解析后的 count 执行命令；无效键回 idle。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromCount(VimCommand state, string input, List<VimEffect> effects)
    {
        if (input.Length == 1 && input[0] is >= '0' and <= '9')
        {
            var digits = state.Digits + input;
            var count = Math.Min(int.Parse(digits), VimLimits.MaxCount);
            return (VimCommand.OfCount(count.ToString()), false);
        }

        var result = HandleNormalInput(input, int.Parse(state.Digits), effects);
        return result.Next is null && !result.Executed
            ? (VimCommand.Idle(), false)
            : result;
    }

    /// <summary>
    /// operator 态转移 — 基准 transitions.ts:283-308 fromOperator：
    /// 同算子首字符（dd/cc/yy）执行行级算子；数字进 operatorCount；motion/find/text-object 前缀继续；无效键回 idle。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromOperator(VimCommand state, string input, List<VimEffect> effects)
    {
        if (input.Length == 1 && input[0] == OperatorKey(state.Op))
        {
            ExecuteLineOp(state.Op, state.Count, effects);
            return (null, true);
        }

        if (input.Length == 1 && input[0] is >= '0' and <= '9')
        {
            return (VimCommand.OfOperatorCount(state.Op, state.Count, input), false);
        }

        var result = HandleOperatorInput(state.Op, state.Count, input, effects);
        return result.Next is null && !result.Executed
            ? (VimCommand.Idle(), false)
            : result;
    }

    /// <summary>
    /// operatorCount 态转移 — 基准 transitions.ts:310-332 fromOperatorCount：
    /// 数字继续累计；motion 时有效 count = 前缀 count × motion count（2d3w = 6 词）。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromOperatorCount(VimCommand state, string input, List<VimEffect> effects)
    {
        if (input.Length == 1 && input[0] is >= '0' and <= '9')
        {
            var digits = state.Digits + input;
            var count = Math.Min(int.Parse(digits), VimLimits.MaxCount);
            return (VimCommand.OfOperatorCount(state.Op, state.Count, count.ToString()), false);
        }

        var effectiveCount = state.Count * int.Parse(state.Digits);
        var result = HandleOperatorInput(state.Op, effectiveCount, input, effects);
        return result.Next is null && !result.Executed
            ? (VimCommand.Idle(), false)
            : result;
    }

    /// <summary>
    /// operatorFind 态转移 — 基准 transitions.ts:334-348 fromOperatorFind：
    /// 任意输入字符即为目标字符执行查找算子（空目标不命中 → 无操作，仍算已执行回 idle）。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromOperatorFind(VimCommand state, string input, List<VimEffect> effects)
    {
        ExecuteOperatorFind(state.Op, state.Find, input, state.Count, effects);
        return (null, true);
    }

    /// <summary>
    /// operatorTextObj 态转移 — 基准 transitions.ts:350-367 fromOperatorTextObj：
    /// 对象类型键执行算子+对象；无效键回 idle。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromOperatorTextObj(VimCommand state, string input, List<VimEffect> effects)
    {
        if (input.Length == 1 && VimTextObject.IsObjectKey(input[0]))
        {
            ExecuteOperatorTextObj(state.Op, state.Scope, input[0], state.Count, effects);
            return (null, true);
        }

        return (VimCommand.Idle(), false);
    }

    /// <summary>
    /// find 态转移 — 基准 transitions.ts:369-383 fromFind：
    /// 任意输入字符为目标执行查找；命中移光标并记 lastFind。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromFind(VimCommand state, string input, List<VimEffect> effects)
    {
        var result = VimMotions.FindCharacter(_text, _offset, input, state.Find, state.Count);
        if (result is { } target)
        {
            MoveCursorTo(target, effects);
            _lastFind = new VimLastFind(state.Find, input);
        }

        return (null, true);
    }

    /// <summary>
    /// g 态转移 — 基准 transitions.ts:385-418 fromG：
    /// j/k 视觉行移动（gj/gk）；gg 到首行（count&gt;1 到第 N 行）；无效键回 idle。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromG(VimCommand state, string input, List<VimEffect> effects)
    {
        if (input == "j" || input == "k")
        {
            var motion = input == "j" ? VimMotion.VisualLineDown : VimMotion.VisualLineUp;
            MoveCursorTo(VimMotions.Resolve(_text, _offset, motion, state.Count, WrapWidth), effects);
            return (null, true);
        }

        if (input == "g")
        {
            MoveCursorTo(
                state.Count > 1 ? VimMotions.GoToLine(_text, state.Count) : 0,
                effects);
            return (null, true);
        }

        return (VimCommand.Idle(), false);
    }

    /// <summary>
    /// operatorG 态转移 — 基准 transitions.ts:420-436 fromOperatorG：
    /// j/k 算子+视觉行 motion；g 算子+gg；其余取消算子回 idle。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromOperatorG(VimCommand state, string input, List<VimEffect> effects)
    {
        if (input == "j" || input == "k")
        {
            var motion = input == "j" ? VimMotion.VisualLineDown : VimMotion.VisualLineUp;
            ExecuteOperatorMotion(state.Op, motion, state.Count, effects);
            return (null, true);
        }

        if (input == "g")
        {
            ExecuteOperatorGg(state.Op, state.Count, effects);
            return (null, true);
        }

        return (VimCommand.Idle(), false);
    }

    /// <summary>
    /// replace 态转移 — 基准 transitions.ts:438-448 fromReplace：
    /// 空输入（Backspace/Delete 空载荷）取消替换回 idle；否则以输入字符为替换目标执行。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromReplace(VimCommand state, string input, List<VimEffect> effects)
    {
        if (input.Length == 0)
        {
            return (VimCommand.Idle(), false);
        }

        ExecuteReplace(input, state.Count, effects);
        return (null, true);
    }

    /// <summary>
    /// indent 态转移 — 基准 transitions.ts:450-459 fromIndent：
    /// 同方向重复（&gt;&gt;/&lt;&lt;）执行缩进；其余回 idle。
    /// </summary>
    private (VimCommand? Next, bool Executed) FromIndent(VimCommand state, string input, List<VimEffect> effects)
    {
        if (input.Length == 1 && input[0] == IndentKey(state.IndentDirection))
        {
            ExecuteIndent(state.IndentDirection, state.Count, effects);
            return (null, true);
        }

        return (VimCommand.Idle(), false);
    }

    /// <summary>
    /// idle/count 共用的命令入口 — 基准 transitions.ts:98-200 handleNormalInput 全集：
    /// 算子/motion/find/g/r/indent 前缀进入子状态；~ x J p P D C Y G . ; , u i I a A o O 直接执行。
    /// </summary>
    private (VimCommand? Next, bool Executed) HandleNormalInput(string input, int count, List<VimEffect> effects)
    {
        if (TryOperatorKey(input, out var op))
        {
            return (VimCommand.OfOperator(op, count), false);
        }

        if (input.Length == 1 && VimMotions.TryResolveSimpleKey(input[0], out var motion))
        {
            MoveCursorTo(VimMotions.Resolve(_text, _offset, motion, count, WrapWidth), effects);
            return (null, true);
        }

        if (TryFindKey(input, out var findKind))
        {
            return (VimCommand.OfFind(findKind, count), false);
        }

        if (input == "g")
        {
            return (VimCommand.OfG(count), false);
        }

        if (input == "r")
        {
            return (VimCommand.OfReplace(count), false);
        }

        if (input == ">" || input == "<")
        {
            return (VimCommand.OfIndent(ParseIndentDirection(input), count), false);
        }

        if (input == "~")
        {
            ExecuteToggleCase(count, effects);
            return (null, true);
        }

        if (input == "x")
        {
            ExecuteX(count, effects);
            return (null, true);
        }

        if (input == "J")
        {
            ExecuteJoin(count, effects);
            return (null, true);
        }

        if (input is "p" or "P")
        {
            ExecutePaste(after: input == "p", count, effects);
            return (null, true);
        }

        if (input == "D")
        {
            ExecuteOperatorMotion(VimOperator.Delete, VimMotion.LineEnd, 1, effects); // 基准 138：count 固定 1
            return (null, true);
        }

        if (input == "C")
        {
            ExecuteOperatorMotion(VimOperator.Change, VimMotion.LineEnd, 1, effects); // 基准 141：count 固定 1
            return (null, true);
        }

        if (input == "Y")
        {
            ExecuteLineOp(VimOperator.Yank, count, effects); // 基准 144：Y = yy 语义（非 vim 原生 y$）
            return (null, true);
        }

        if (input == "G")
        {
            // 基准 146-158：count=1 语义为无 count → 末行首；否则第 N 行首
            MoveCursorTo(
                count == 1 ? VimMotions.LastLineStart(_text) : VimMotions.GoToLine(_text, count),
                effects);
            return (null, true);
        }

        if (input == ".")
        {
            ReplayLastChange(effects);
            return (null, true);
        }

        if (input is ";" or ",")
        {
            RepeatFind(reverse: input == ",", count, effects);
            return (null, true);
        }

        if (input == "u")
        {
            effects.Add(VimEffect.Undo()); // 基准 165-167：undo 委托宿主（props.onUndo）
            return (null, true);
        }

        if (input == "i")
        {
            EnterInsertAt(_offset, effects);
            return (null, true);
        }

        if (input == "I")
        {
            EnterInsertAt(VimMotions.FirstNonBlank(_text, _offset), effects);
            return (null, true);
        }

        if (input == "a")
        {
            // 基准 177-186：光标在末尾不动，否则右移一 grapheme 后进 INSERT
            var target = _offset >= _text.Length ? _offset : VimMotions.NextGraphemeStart(_text, _offset);
            EnterInsertAt(target, effects);
            return (null, true);
        }

        if (input == "A")
        {
            EnterInsertAt(VimMotions.LineEnd(_text, _offset), effects);
            return (null, true);
        }

        if (input == "o")
        {
            ExecuteOpenLine(below: true, effects);
            return (null, true);
        }

        if (input == "O")
        {
            ExecuteOpenLine(below: false, effects);
            return (null, true);
        }

        return (null, false);
    }

    /// <summary>
    /// operator 态共用的输入入口 — 基准 transitions.ts:206-242 handleOperatorInput：
    /// i/a 进 text-object 前缀态；f/F/t/T 进 operatorFind；简单 motion/G/g 直接执行或进 operatorG。
    /// </summary>
    private (VimCommand? Next, bool Executed) HandleOperatorInput(
        VimOperator op,
        int count,
        string input,
        List<VimEffect> effects)
    {
        if (input is "i" or "a")
        {
            return (VimCommand.OfOperatorTextObj(op, count, ParseScope(input)), false);
        }

        if (TryFindKey(input, out var findKind))
        {
            return (VimCommand.OfOperatorFind(op, count, findKind), false);
        }

        if (input.Length == 1 && VimMotions.TryResolveSimpleKey(input[0], out var motion))
        {
            ExecuteOperatorMotion(op, motion, count, effects);
            return (null, true);
        }

        if (input == "G")
        {
            ExecuteOperatorG(op, count, effects);
            return (null, true);
        }

        if (input == "g")
        {
            return (VimCommand.OfOperatorG(op, count), false);
        }

        return (null, false);
    }

    /// <summary>
    /// dot-repeat 重放 — 基准 useVimInput.ts:109-173 replayLastChange：
    /// insert 整段重插；其余分发到对应算子（replay 会话不回写 lastChange — 基准 isReplay 短路）。
    /// </summary>
    private void ReplayLastChange(List<VimEffect> effects)
    {
        if (_lastChange is not { } change)
        {
            return;
        }

        if (change.Kind == VimRecordedChangeKind.Insert)
        {
            if (change.Text.Length > 0)
            {
                var insertSession = NewSession(record: false);
                insertSession.SetText(_text[.._offset] + change.Text + _text[_offset..]);
                insertSession.SetOffset(_offset + change.Text.Length);
                ApplySession(insertSession, effects);
            }

            return;
        }

        var session = NewSession(record: false);
        switch (change.Kind)
        {
            case VimRecordedChangeKind.DeleteChar:
                VimOperators.ExecuteX(session, change.Count);
                break;
            case VimRecordedChangeKind.ReplaceChar:
                VimOperators.ExecuteReplace(session, change.Char, change.Count);
                break;
            case VimRecordedChangeKind.ToggleCase:
                VimOperators.ExecuteToggleCase(session, change.Count);
                break;
            case VimRecordedChangeKind.Indent:
                VimOperators.ExecuteIndent(session, change.IndentDirection, change.Count);
                break;
            case VimRecordedChangeKind.Join:
                VimOperators.ExecuteJoin(session, change.Count);
                break;
            case VimRecordedChangeKind.OpenLine:
                VimOperators.ExecuteOpenLine(session, change.Below);
                break;
            case VimRecordedChangeKind.Operator:
                VimOperators.ExecuteOperatorMotion(session, change.Op, change.Motion, change.Count);
                break;
            case VimRecordedChangeKind.OperatorFind:
                VimOperators.ExecuteOperatorFind(session, change.Op, change.Find, change.Char, change.Count);
                break;
            case VimRecordedChangeKind.OperatorTextObj:
                VimOperators.ExecuteOperatorTextObj(session, change.Op, change.Scope, change.ObjType, change.Count);
                break;
        }

        ApplySession(session, effects);
    }

    /// <summary>
    /// 重复最近查找（;/, 命令）— 基准 transitions.ts:465-490 executeRepeatFind：
    /// ',' 翻转方向（f↔F、t↔T）；命中移光标（不更新 lastFind — 翻转是临时的）。
    /// </summary>
    private void RepeatFind(bool reverse, int count, List<VimEffect> effects)
    {
        if (_lastFind is not { } lastFind)
        {
            return;
        }

        var kind = reverse ? FlipFind(lastFind.Kind) : lastFind.Kind;
        var result = VimMotions.FindCharacter(_text, _offset, lastFind.Char, kind, count);
        if (result is { } target)
        {
            MoveCursorTo(target, effects);
        }
    }

    /// <summary>查找方向翻转（f↔F、t↔T — 基准 flipMap，transitions.ts:477-484）。</summary>
    private static VimFindKind FlipFind(VimFindKind kind) => kind switch
    {
        VimFindKind.ToForward => VimFindKind.ToBackward,
        VimFindKind.ToBackward => VimFindKind.ToForward,
        VimFindKind.TillForward => VimFindKind.TillBackward,
        VimFindKind.TillBackward => VimFindKind.TillForward,
        _ => kind,
    };

    /// <summary>移动镜像光标（相同偏移无效果 — 基准 setOffset 同值无副作用）。</summary>
    private void MoveCursorTo(int offset, List<VimEffect> effects)
    {
        var clamped = Math.Clamp(offset, 0, _text.Length);
        if (clamped == _offset)
        {
            return;
        }

        _offset = clamped;
        effects.Add(VimEffect.Move(clamped));
    }

    /// <summary>切 INSERT 并定位光标（基准 switchToInsertMode — 重置命令状态与键入跟踪）。</summary>
    private void EnterInsertAt(int offset, List<VimEffect> effects)
    {
        _offset = Math.Clamp(offset, 0, _text.Length);
        _mode = VimMode.Insert;
        _insertedText = string.Empty;
        _command = VimCommand.Idle();
        effects.Add(VimEffect.Insert(_offset));
    }

    /// <summary>新建算子执行会话（record=false 为 dot-repeat 重放会话）。</summary>
    private VimEditSession NewSession(bool record) => new(_text, _offset, WrapWidth, record);

    /// <summary>
    /// 提交会话 — 寄存器/lastFind/lastChange 落账，镜像更新并产出接线效果：
    /// 文本变化 → ApplyText（宿主 ReplaceAt）；纯光标变化（yank 类）→ MoveCursor；
    /// enterInsert → 切 INSERT 形态（效果自带光标定位）。
    /// </summary>
    private void ApplySession(VimEditSession session, List<VimEffect> effects)
    {
        if (session.RegisterContent is { } registerContent)
        {
            _register.Write(registerContent, session.RegisterLinewise);
        }

        if (session.LastFind is { } lastFind)
        {
            _lastFind = lastFind;
        }

        if (session.Change is { } change)
        {
            _lastChange = change;
        }

        var textChanged = session.Text != _text;
        var previousOffset = _offset;
        _text = session.Text;
        _offset = Math.Clamp(session.Offset, 0, session.Text.Length);
        if (textChanged)
        {
            effects.Add(VimEffect.Apply(_text, _offset));
        }
        else if (session.Offset != previousOffset && !session.EnterInsert)
        {
            effects.Add(VimEffect.Move(_offset));
        }

        if (session.EnterInsert)
        {
            _mode = VimMode.Insert;
            _insertedText = string.Empty;
            _command = VimCommand.Idle();
            effects.Add(VimEffect.Insert(_offset));
        }
    }

    // ====================================================================
    // 算子执行包装 — NewSession → VimOperators → ApplySession 三段式
    // ====================================================================

    /// <summary>执行算子+motion（dw/de/d$ 类）。</summary>
    private void ExecuteOperatorMotion(VimOperator op, VimMotion motion, int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteOperatorMotion(session, op, motion, count);
        ApplySession(session, effects);
    }

    /// <summary>执行算子+查找（dfx 类）。</summary>
    private void ExecuteOperatorFind(VimOperator op, VimFindKind findKind, string ch, int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteOperatorFind(session, op, findKind, ch, count);
        ApplySession(session, effects);
    }

    /// <summary>执行算子+text-object（ciw/di( 类）。</summary>
    private void ExecuteOperatorTextObj(VimOperator op, VimTextObjectScope scope, char objectKey, int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteOperatorTextObj(session, op, scope, objectKey, count);
        ApplySession(session, effects);
    }

    /// <summary>执行行级算子（dd/cc/yy/Y）。</summary>
    private void ExecuteLineOp(VimOperator op, int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteLineOp(session, op, count);
        ApplySession(session, effects);
    }

    /// <summary>执行删除字符（x 类 — Delete 键在 motion 等待态的映射）。</summary>
    private void ExecuteX(int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteX(session, count);
        ApplySession(session, effects);
    }

    /// <summary>执行替换字符（r{char}）。</summary>
    private void ExecuteReplace(string ch, int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteReplace(session, ch, count);
        ApplySession(session, effects);
    }

    /// <summary>执行翻转大小写（~）。</summary>
    private void ExecuteToggleCase(int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteToggleCase(session, count);
        ApplySession(session, effects);
    }

    /// <summary>执行合并行（J）。</summary>
    private void ExecuteJoin(int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteJoin(session, count);
        ApplySession(session, effects);
    }

    /// <summary>执行粘贴（p/P — 读无名寄存器）。</summary>
    private void ExecutePaste(bool after, int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecutePaste(session, after, count, _register.Content);
        ApplySession(session, effects);
    }

    /// <summary>执行缩进（&gt;&gt;/&lt;&lt;）。</summary>
    private void ExecuteIndent(VimIndentDirection direction, int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteIndent(session, direction, count);
        ApplySession(session, effects);
    }

    /// <summary>执行开新行（o/O — 插入空行并进 INSERT）。</summary>
    private void ExecuteOpenLine(bool below, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteOpenLine(session, below);
        ApplySession(session, effects);
    }

    /// <summary>执行算子+G（dG/yG 类）。</summary>
    private void ExecuteOperatorG(VimOperator op, int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteOperatorG(session, op, count);
        ApplySession(session, effects);
    }

    /// <summary>执行算子+gg（dgg/ygg 类）。</summary>
    private void ExecuteOperatorGg(VimOperator op, int count, List<VimEffect> effects)
    {
        var session = NewSession(record: true);
        VimOperators.ExecuteOperatorGg(session, op, count);
        ApplySession(session, effects);
    }

    // ====================================================================
    // 键字符解析辅助 — 基准 types.ts:125-180（OPERATORS/FIND_KEYS/TEXT_OBJ_SCOPES 常量表）
    // ====================================================================

    /// <summary>算子键判定（d/c/y — 基准 types.ts:125-133 OPERATORS）。</summary>
    private static bool TryOperatorKey(string input, out VimOperator op)
    {
        op = input switch
        {
            "d" => VimOperator.Delete,
            "c" => VimOperator.Change,
            "y" => VimOperator.Yank,
            _ => default,
        };
        return input is "d" or "c" or "y";
    }

    /// <summary>查找键判定（f/F/t/T — 基准 types.ts:151 FIND_KEYS）。</summary>
    private static bool TryFindKey(string input, out VimFindKind kind)
    {
        kind = input switch
        {
            "f" => VimFindKind.ToForward,
            "F" => VimFindKind.ToBackward,
            "t" => VimFindKind.TillForward,
            "T" => VimFindKind.TillBackward,
            _ => default,
        };
        return input is "f" or "F" or "t" or "T";
    }

    /// <summary>text-object 范围键解析（i=inner/a=around — 基准 types.ts:153-156 TEXT_OBJ_SCOPES）。</summary>
    private static VimTextObjectScope ParseScope(string input) =>
        input == "i" ? VimTextObjectScope.Inner : VimTextObjectScope.Around;

    /// <summary>缩进方向键解析（&gt;/&lt;）。</summary>
    private static VimIndentDirection ParseIndentDirection(string input) =>
        input == ">" ? VimIndentDirection.Increase : VimIndentDirection.Decrease;

    /// <summary>算子首字符（dd/cc/yy 判定 — 基准 state.op[0]，transitions.ts:289）。</summary>
    private static char OperatorKey(VimOperator op) => op switch
    {
        VimOperator.Delete => 'd',
        VimOperator.Change => 'c',
        VimOperator.Yank => 'y',
        _ => '\0',
    };

    /// <summary>缩进方向键（&gt;&gt;/&lt;&lt; 判定 — 基准 input === state.dir，transitions.ts:455）。</summary>
    private static char IndentKey(VimIndentDirection direction) =>
        direction == VimIndentDirection.Increase ? '>' : '<';
}
