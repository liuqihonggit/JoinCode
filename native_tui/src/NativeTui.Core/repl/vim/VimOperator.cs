namespace NativeTui.Core.Repl.Vim;

/// <summary>
/// 算子执行会话 — 基准 src/vim/operators.ts:26-37 OperatorContext 的纯逻辑化载体：
/// TS 侧经 setText/setOffset/enterInsert/setRegister/setLastFind/recordChange 回调写宿主状态
///（useVimInput.ts:82-107 组装）；C# 侧收敛为可变工作副本，算子执行完毕由 VimEngine.ApplySession
/// 一次性提交镜像并产出接线效果（纯函数风格的副作用收集器）。
/// </summary>
internal sealed class VimEditSession
{
    /// <summary>创建会话（携带引擎镜像快照）。</summary>
    public VimEditSession(string text, int offset, int wrapWidth, bool record)
    {
        Text = text;
        Offset = offset;
        WrapWidth = wrapWidth;
        Record = record;
    }

    /// <summary>工作副本文本（setText 目标）。</summary>
    public string Text { get; private set; }

    /// <summary>工作副本光标（setOffset 目标 — 多次调用末值生效）。</summary>
    public int Offset { get; private set; }

    /// <summary>视觉行折行内容宽（gj/gk motion 用 — 引擎 WrapWidth 快照）。</summary>
    public int WrapWidth { get; }

    /// <summary>是否记录 dot-repeat（replay 期间 false — 对齐基准 isReplay 短路 recordChange，useVimInput.ts:101-105）。</summary>
    public bool Record { get; }

    /// <summary>enterInsert 是否被调用（change 类算子收尾进 INSERT）。</summary>
    public bool EnterInsert { get; private set; }

    /// <summary>寄存器待写入内容（setRegister 目标；null = 未写）。</summary>
    public string? RegisterContent { get; private set; }

    /// <summary>寄存器写入是否 linewise。</summary>
    public bool RegisterLinewise { get; private set; }

    /// <summary>最近查找待写入（setLastFind 目标；null = 未写）。</summary>
    public VimLastFind? LastFind { get; private set; }

    /// <summary>dot-repeat 待记录变更（recordChange 目标；null = 未记录）。</summary>
    public VimRecordedChange? Change { get; private set; }

    /// <summary>写入文本（对齐 ctx.setText）。</summary>
    public void SetText(string text) => Text = text;

    /// <summary>设定光标（对齐 ctx.setOffset — 基准直接写 textInput 偏移，末值生效）。</summary>
    public void SetOffset(int offset) => Offset = offset;

    /// <summary>进入 INSERT 并定位光标（对齐 ctx.enterInsert — change/c 系算子收尾）。</summary>
    public void EnterInsertAt(int offset)
    {
        EnterInsert = true;
        Offset = offset;
    }

    /// <summary>写寄存器（对齐 ctx.setRegister — linewise 内容保证尾随 \n 由调用方拼接）。</summary>
    public void SetRegister(string content, bool linewise)
    {
        RegisterContent = content;
        RegisterLinewise = linewise;
    }

    /// <summary>记最近查找（对齐 ctx.setLastFind）。</summary>
    public void SetLastFind(VimFindKind kind, string ch) => LastFind = new VimLastFind(kind, ch);

    /// <summary>记 dot-repeat 变更（replay 会话静默 — 对齐基准 isReplay 空实现）。</summary>
    public void RecordChange(VimRecordedChange change)
    {
        if (Record)
        {
            Change = change;
        }
    }
}

/// <summary>
/// vim 算子执行 — 基准 src/vim/operators.ts 全量纯函数（delete/change/yank 三算子 ×
/// motion/find/text-object/行组合 + x/r/~/J/p/P/o/O/缩进独立命令）。
/// 语义锚点：
/// ① inclusive motion（e/E/$）范围右端扩一 grapheme（getOperatorRange，operators.ts:465-467）；
/// ② linewise motion（j/k/G/gg）范围整行扩展（operators.ts:451-464）；
/// ③ cw/cW 特例 — 到词尾而非下一词首（operators.ts:441-450）；
/// ④ 删除后光标钳制到最后 grapheme 起点（maxOff 语义，operators.ts:145-149/188-192）；
/// ⑤ linewise 寄存器内容以 \n 结尾（paste 分流依据，operators.ts:121-124/302）；
/// ⑥ 基准 [Image #N] 芯片 snap（operators.ts:470-472）不实现 — w2 输入缓冲无图片占位符概念。
/// </summary>
internal static class VimOperators
{
    /// <summary>
    /// 算子+motion 执行 — 基准 operators.ts:42-54 executeOperatorMotion：
    /// 目标与光标重合时无操作；否则按范围语义应用算子并记录 dot-repeat。
    /// </summary>
    public static void ExecuteOperatorMotion(VimEditSession session, VimOperator op, VimMotion motion, int count)
    {
        var target = VimMotions.Resolve(session.Text, session.Offset, motion, count, session.WrapWidth);
        if (target == session.Offset)
        {
            return;
        }

        var (from, to, linewise) = GetOperatorRange(session.Text, session.Offset, target, motion, op, count);
        ApplyOperator(session, op, from, to, linewise);
        session.RecordChange(new VimRecordedChange
        {
            Kind = VimRecordedChangeKind.Operator,
            Op = op,
            Motion = motion,
            Count = count,
        });
    }

    /// <summary>
    /// 算子+查找执行 — 基准 operators.ts:59-75 executeOperatorFind：
    /// findCharacter 未命中无操作；命中按 inclusive 范围应用（t/T 已在 findCharacter 内调好偏移，
    /// operators.ts:479-481 注释 — findType 不参与范围计算）；记 lastFind。
    /// </summary>
    public static void ExecuteOperatorFind(
        VimEditSession session,
        VimOperator op,
        VimFindKind findKind,
        string ch,
        int count)
    {
        var targetOffset = VimMotions.FindCharacter(session.Text, session.Offset, ch, findKind, count);
        if (targetOffset is not { } target)
        {
            return;
        }

        var from = Math.Min(session.Offset, target);
        var to = VimMotions.NextGraphemeStart(session.Text, Math.Max(session.Offset, target));
        ApplyOperator(session, op, from, to, linewise: false);
        session.SetLastFind(findKind, ch);
        session.RecordChange(new VimRecordedChange
        {
            Kind = VimRecordedChangeKind.OperatorFind,
            Op = op,
            Find = findKind,
            Char = ch,
            Count = count,
        });
    }

    /// <summary>
    /// 算子+text-object 执行 — 基准 operators.ts:80-97 executeOperatorTextObj：
    /// 对象未命中无操作；命中按对象范围应用（charwise — linewise 恒 false）。
    /// </summary>
    public static void ExecuteOperatorTextObj(
        VimEditSession session,
        VimOperator op,
        VimTextObjectScope scope,
        char objectKey,
        int count)
    {
        var range = VimTextObject.Find(session.Text, session.Offset, objectKey, scope == VimTextObjectScope.Inner);
        if (range is not { } objRange)
        {
            return;
        }

        ApplyOperator(session, op, objRange.Start, objRange.End, linewise: false);
        session.RecordChange(new VimRecordedChange
        {
            Kind = VimRecordedChangeKind.OperatorTextObj,
            Op = op,
            ObjType = objectKey,
            Scope = scope,
            Count = count,
        });
    }

    /// <summary>
    /// 行级算子（dd/cc/yy）— 基准 operators.ts:102-166 executeLineOp：
    /// 逻辑行定位（基准 110-113 注释明确用换行计数而非 getPosition 视觉行）；
    /// 寄存器写整行内容（尾随 \n 保证）；yank 光标回行首；delete 末行收缩含前置换行；
    /// change 单行清空、多行折叠为单空行后进 INSERT。
    /// </summary>
    public static void ExecuteLineOp(VimEditSession session, VimOperator op, int count)
    {
        var text = session.Text;
        var lines = VimMotions.SplitLines(text);
        var currentLine = VimMotions.LineIndexOf(text, session.Offset);
        var linesToAffect = Math.Min(count, lines.Count - currentLine);
        var lineStart = VimMotions.LineStart(text, session.Offset);
        var lineEnd = lineStart;
        for (var i = 0; i < linesToAffect; i++)
        {
            var nextNewline = text.IndexOf('\n', lineEnd);
            lineEnd = nextNewline == -1 ? text.Length : nextNewline + 1;
        }

        var content = text[lineStart..lineEnd];
        if (!content.EndsWith('\n'))
        {
            content += '\n';
        }

        session.SetRegister(content, linewise: true);

        if (op == VimOperator.Yank)
        {
            session.SetOffset(lineStart);
        }
        else if (op == VimOperator.Delete)
        {
            var deleteStart = lineStart;
            var deleteEnd = lineEnd;

            // 删到文件末尾且有前置换行 → 连前置换行一起删（末行删除不留尾随空行，基准 133-141）
            if (deleteEnd == text.Length && deleteStart > 0 && text[deleteStart - 1] == '\n')
            {
                deleteStart--;
            }

            var newText = text[..deleteStart] + text[deleteEnd..];
            session.SetText(newText);
            var maxOff = Math.Max(0, newText.Length - VimMotions.LastGraphemeLength(newText));
            session.SetOffset(Math.Min(deleteStart, maxOff));
        }
        else
        {
            // change：单行清空；多行折叠为单空行后进 INSERT（基准 150-163）
            if (lines.Count == 1)
            {
                session.SetText(string.Empty);
                session.EnterInsertAt(0);
            }
            else
            {
                var beforeLines = lines.Take(currentLine);
                var afterLines = lines.Skip(currentLine + linesToAffect);
                var newText = string.Join("\n", beforeLines.Append(string.Empty).Concat(afterLines));
                session.SetText(newText);
                session.EnterInsertAt(lineStart);
            }
        }

        // 基准记 motion: op[0]（'d'/'c'/'y'）→ 重放 resolveMotion 不识别 → noop；C# 用 None 占位对齐
        session.RecordChange(new VimRecordedChange
        {
            Kind = VimRecordedChangeKind.Operator,
            Op = op,
            Motion = VimMotion.None,
            Count = count,
        });
    }

    /// <summary>
    /// 删除光标处字符（x 命令）— 基准 operators.ts:171-194 executeX：
    /// 向右推进 count 个 grapheme（末尾停）；charwise 写寄存器；光标 maxOff 钳制。
    /// </summary>
    public static void ExecuteX(VimEditSession session, int count)
    {
        var from = session.Offset;
        if (from >= session.Text.Length)
        {
            return;
        }

        var end = from;
        for (var i = 0; i < count && end < session.Text.Length; i++)
        {
            end = VimMotions.NextGraphemeStart(session.Text, end);
        }

        var deleted = session.Text[from..end];
        var newText = session.Text[..from] + session.Text[end..];
        session.SetRegister(deleted, linewise: false);
        session.SetText(newText);
        var maxOff = Math.Max(0, newText.Length - VimMotions.LastGraphemeLength(newText));
        session.SetOffset(Math.Min(from, maxOff));
        session.RecordChange(new VimRecordedChange { Kind = VimRecordedChangeKind.DeleteChar, Count = count });
    }

    /// <summary>
    /// 替换字符（r{char} 命令）— 基准 operators.ts:199-217 executeReplace：
    /// 逐 grapheme 替换 count 次（替换字符可为多 char grapheme）；光标停在最后一个替换字符上。
    /// </summary>
    public static void ExecuteReplace(VimEditSession session, string ch, int count)
    {
        var offset = session.Offset;
        var newText = session.Text;
        for (var i = 0; i < count && offset < newText.Length; i++)
        {
            var graphemeLength = GraphemeSegmenter.TryNext(newText.AsSpan(offset), out var end)
                ? end - offset
                : 1;
            newText = newText[..offset] + ch + newText[(offset + graphemeLength)..];
            offset += ch.Length;
        }

        session.SetText(newText);
        session.SetOffset(Math.Max(0, offset - ch.Length));
        session.RecordChange(new VimRecordedChange { Kind = VimRecordedChangeKind.ReplaceChar, Char = ch, Count = count });
    }

    /// <summary>
    /// 翻转大小写（~ 命令）— 基准 operators.ts:222-253 executeToggleCase：
    /// 逐 grapheme 翻转 count 个（已是大写 → 小写，否则大写）；光标移到最后翻转字符之后。
    /// </summary>
    public static void ExecuteToggleCase(VimEditSession session, int count)
    {
        var offset = session.Offset;
        if (offset >= session.Text.Length)
        {
            return;
        }

        var newText = session.Text;
        var toggled = 0;
        while (offset < newText.Length && toggled < count)
        {
            var grapheme = VimMotions.GraphemeAt(newText, offset);
            if (grapheme.Length == 0)
            {
                break;
            }

            var upper = grapheme.ToUpperInvariant();
            var toggledGrapheme = grapheme == upper ? grapheme.ToLowerInvariant() : upper;
            newText = newText[..offset] + toggledGrapheme + newText[(offset + grapheme.Length)..];
            offset += toggledGrapheme.Length;
            toggled++;
        }

        session.SetText(newText);
        session.SetOffset(offset);
        session.RecordChange(new VimRecordedChange { Kind = VimRecordedChangeKind.ToggleCase, Count = count });
    }

    /// <summary>
    /// 合并行（J 命令）— 基准 operators.ts:258-289 executeJoin：
    /// 合并后续 count 行；后续行去前导空白，非空时以单空格衔接（当前行不以空格结尾且非空）；
    /// 光标停在合并前当前行内容的末尾。行定位用逻辑行（基准 getPosition 视觉行属遗留差异，统一逻辑行）。
    /// </summary>
    public static void ExecuteJoin(VimEditSession session, int count)
    {
        var text = session.Text;
        var lines = VimMotions.SplitLines(text);
        var currentLine = VimMotions.LineIndexOf(text, session.Offset);
        if (currentLine >= lines.Count - 1)
        {
            return;
        }

        var linesToJoin = Math.Min(count, lines.Count - currentLine - 1);
        var joinedLine = lines[currentLine];
        var cursorPos = joinedLine.Length;
        for (var i = 1; i <= linesToJoin; i++)
        {
            var nextLine = lines[currentLine + i].TrimStart();
            if (nextLine.Length > 0)
            {
                if (!joinedLine.EndsWith(' ') && joinedLine.Length > 0)
                {
                    joinedLine += ' ';
                }

                joinedLine += nextLine;
            }
        }

        var newLines = lines.Take(currentLine)
            .Append(joinedLine)
            .Concat(lines.Skip(currentLine + linesToJoin + 1))
            .ToList();
        var newText = string.Join("\n", newLines);
        session.SetText(newText);
        session.SetOffset(VimMotions.LineStartOffset(newLines, currentLine) + cursorPos);
        session.RecordChange(new VimRecordedChange { Kind = VimRecordedChangeKind.Join, Count = count });
    }

    /// <summary>
    /// 粘贴（p/P 命令）— 基准 operators.ts:294-343 executePaste：
    /// linewise（内容尾随 \n）→ 行级插入（p 下行 / P 当前行前），光标到插入内容首行行首；
    /// charwise → p 在光标右邻 grapheme 后插入 / P 在光标处插入，光标落在内容末 grapheme 上；
    /// 内容重复 count 次。粘贴不记录 dot-repeat（基准 executePaste 无 recordChange）。
    /// </summary>
    public static void ExecutePaste(VimEditSession session, bool after, int count, string registerContent)
    {
        if (registerContent.Length == 0)
        {
            return;
        }

        var isLinewise = registerContent.EndsWith('\n');
        var content = isLinewise ? registerContent[..^1] : registerContent;
        var steps = Math.Max(1, count);

        if (isLinewise)
        {
            var lines = VimMotions.SplitLines(session.Text);
            var currentLine = VimMotions.LineIndexOf(session.Text, session.Offset);
            var insertLine = after ? currentLine + 1 : currentLine;
            var contentLines = VimMotions.SplitLines(content);
            var newLines = lines.Take(insertLine)
                .Concat(Enumerable.Repeat(contentLines, steps).SelectMany(static repeated => repeated))
                .Concat(lines.Skip(insertLine))
                .ToList();
            var newText = string.Join("\n", newLines);
            session.SetText(newText);
            session.SetOffset(VimMotions.LineStartOffset(newLines, insertLine));
        }
        else
        {
            var textToInsert = string.Concat(Enumerable.Repeat(content, steps));
            var insertPoint = after && session.Offset < session.Text.Length
                ? VimMotions.NextGraphemeStart(session.Text, session.Offset)
                : session.Offset;
            var newText = session.Text[..insertPoint] + textToInsert + session.Text[insertPoint..];
            var lastGraphemeLength = VimMotions.LastGraphemeLength(textToInsert);
            var newOffset = insertPoint + textToInsert.Length - lastGraphemeLength;
            session.SetText(newText);
            session.SetOffset(Math.Max(insertPoint, newOffset));
        }
    }

    /// <summary>
    /// 缩进（&gt;&gt;/&lt;&lt; 命令）— 基准 operators.ts:348-392 executeIndent：
    /// 影响当前行起 count 行；&gt; 行首加两空格；&lt; 先试去两空格、再去单 Tab、否则去至多两前导空白；
    /// 光标到当前行首个非空白。
    /// </summary>
    public static void ExecuteIndent(VimEditSession session, VimIndentDirection direction, int count)
    {
        var lines = VimMotions.SplitLines(session.Text);
        var currentLine = VimMotions.LineIndexOf(session.Text, session.Offset);
        var linesToAffect = Math.Min(count, lines.Count - currentLine);
        const string indent = "  ";

        for (var i = 0; i < linesToAffect; i++)
        {
            var lineIndex = currentLine + i;
            var line = lines[lineIndex];
            if (direction == VimIndentDirection.Increase)
            {
                lines[lineIndex] = indent + line;
            }
            else if (line.StartsWith(indent, StringComparison.Ordinal))
            {
                lines[lineIndex] = line[indent.Length..];
            }
            else if (line.StartsWith('\t'))
            {
                lines[lineIndex] = line[1..];
            }
            else
            {
                // 去至多 indent 长度的前导空白（基准 370-381）
                var index = 0;
                var removed = 0;
                while (index < line.Length && removed < indent.Length && char.IsWhiteSpace(line[index]))
                {
                    removed++;
                    index++;
                }

                lines[lineIndex] = line[index..];
            }
        }

        var newText = string.Join("\n", lines);
        var currentLineText = lines[Math.Clamp(currentLine, 0, lines.Count - 1)];
        var firstNonBlank = 0;
        while (firstNonBlank < currentLineText.Length && char.IsWhiteSpace(currentLineText[firstNonBlank]))
        {
            firstNonBlank++;
        }

        session.SetText(newText);
        session.SetOffset(VimMotions.LineStartOffset(lines, currentLine) + firstNonBlank);
        session.RecordChange(new VimRecordedChange
        {
            Kind = VimRecordedChangeKind.Indent,
            IndentDirection = direction,
            Count = count,
        });
    }

    /// <summary>
    /// 开新行（o/O 命令）— 基准 operators.ts:397-416 executeOpenLine：
    /// 当前行下（o）/上（O）插入空行并进 INSERT（光标到新行行首）。
    /// </summary>
    public static void ExecuteOpenLine(VimEditSession session, bool below)
    {
        var lines = VimMotions.SplitLines(session.Text);
        var currentLine = VimMotions.LineIndexOf(session.Text, session.Offset);
        var insertLine = below ? currentLine + 1 : currentLine;
        var newLines = lines.Take(insertLine)
            .Append(string.Empty)
            .Concat(lines.Skip(insertLine))
            .ToList();
        var newText = string.Join("\n", newLines);
        session.SetText(newText);
        session.EnterInsertAt(VimMotions.LineStartOffset(newLines, insertLine));
        session.RecordChange(new VimRecordedChange { Kind = VimRecordedChangeKind.OpenLine, Below = below });
    }

    /// <summary>
    /// 算子+G 执行 — 基准 operators.ts:524-539 executeOperatorG：
    /// count=1 语义为无 count → 末行行首；否则第 N 行行首；目标重合无操作；linewise 整行范围。
    /// </summary>
    public static void ExecuteOperatorG(VimEditSession session, VimOperator op, int count)
    {
        var target = count == 1
            ? VimMotions.LastLineStart(session.Text)
            : VimMotions.GoToLine(session.Text, count);
        if (target == session.Offset)
        {
            return;
        }

        var (from, to, linewise) = GetOperatorRange(session.Text, session.Offset, target, VimMotion.BufferEnd, op, count);
        ApplyOperator(session, op, from, to, linewise);
        session.RecordChange(new VimRecordedChange
        {
            Kind = VimRecordedChangeKind.Operator,
            Op = op,
            Motion = VimMotion.BufferEnd,
            Count = count,
        });
    }

    /// <summary>
    /// 算子+gg 执行 — 基准 operators.ts:541-556 executeOperatorGg：
    /// count=1 → 首行行首；否则第 N 行行首；linewise 整行范围。
    /// </summary>
    public static void ExecuteOperatorGg(VimEditSession session, VimOperator op, int count)
    {
        var target = count == 1 ? 0 : VimMotions.GoToLine(session.Text, count);
        if (target == session.Offset)
        {
            return;
        }

        var (from, to, linewise) = GetOperatorRange(session.Text, session.Offset, target, VimMotion.FirstLine, op, count);
        ApplyOperator(session, op, from, to, linewise);
        session.RecordChange(new VimRecordedChange
        {
            Kind = VimRecordedChangeKind.Operator,
            Op = op,
            Motion = VimMotion.FirstLine,
            Count = count,
        });
    }

    /// <summary>
    /// 算子范围计算 — 基准 operators.ts:429-475 getOperatorRange：
    /// ① cw/cW 特例（改到词尾 — 向前 (count-1) 词后取词尾，右端扩一 grapheme）；
    /// ② linewise motion 整行扩展（右端到行尾含 \n；删到文末且左端前是 \n 则左端再收一格）；
    /// ③ inclusive motion（e/E/$）正向时右端扩一 grapheme。
    /// 基准的 [Image #N] 芯片 snap（operators.ts:470-472）不实现 — w2 无图片占位符。
    /// </summary>
    private static (int From, int To, bool Linewise) GetOperatorRange(
        string text,
        int cursorOffset,
        int targetOffset,
        VimMotion motion,
        VimOperator op,
        int count)
    {
        var from = Math.Min(cursorOffset, targetOffset);
        var to = Math.Max(cursorOffset, targetOffset);
        var linewise = false;

        if (op == VimOperator.Change && motion is VimMotion.WordForward or VimMotion.BigWordForward)
        {
            // cw 特例：向前 (count-1) 词再取词尾（基准 441-450）
            var wordCursor = cursorOffset;
            for (var i = 0; i < count - 1; i++)
            {
                wordCursor = motion == VimMotion.WordForward
                    ? VimMotions.NextVimWord(text, wordCursor)
                    : VimMotions.NextBigWord(text, wordCursor);
            }

            var wordEnd = motion == VimMotion.WordForward
                ? VimMotions.EndOfVimWord(text, wordCursor)
                : VimMotions.EndOfBigWord(text, wordCursor);
            to = VimMotions.NextGraphemeStart(text, wordEnd);
        }
        else if (VimMotions.IsLinewise(motion))
        {
            linewise = true;
            var nextNewline = text.IndexOf('\n', to);
            if (nextNewline == -1)
            {
                // 到文末 — 左端前是 \n 则连前置换行一起（基准 457-461）
                to = text.Length;
                if (from > 0 && text[from - 1] == '\n')
                {
                    from--;
                }
            }
            else
            {
                to = nextNewline + 1;
            }
        }
        else if (VimMotions.IsInclusive(motion) && cursorOffset <= targetOffset)
        {
            to = VimMotions.NextGraphemeStart(text, to);
        }

        return (from, to, linewise);
    }

    /// <summary>
    /// 应用算子到 [from, to) — 基准 operators.ts:493-522 applyOperator：
    /// 寄存器写范围内容（linewise 补尾 \n）；yank 光标回范围起点不动文本；
    /// delete 删除后光标 maxOff 钳制；change 删除后进 INSERT 于范围起点。
    /// </summary>
    private static void ApplyOperator(VimEditSession session, VimOperator op, int from, int to, bool linewise)
    {
        var content = session.Text[from..to];
        if (linewise && !content.EndsWith('\n'))
        {
            content += '\n';
        }

        session.SetRegister(content, linewise);

        if (op == VimOperator.Yank)
        {
            session.SetOffset(from);
        }
        else if (op == VimOperator.Delete)
        {
            var newText = session.Text[..from] + session.Text[to..];
            session.SetText(newText);
            var maxOff = Math.Max(0, newText.Length - VimMotions.LastGraphemeLength(newText));
            session.SetOffset(Math.Min(from, maxOff));
        }
        else
        {
            var newText = session.Text[..from] + session.Text[to..];
            session.SetText(newText);
            session.EnterInsertAt(from);
        }
    }
}
