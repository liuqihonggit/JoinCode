namespace NativeTui.Core.Repl.Vim;

/// <summary>
/// vim text-object 边界查找 — 基准 src/vim/textObjects.ts 全量：
/// 词对象（iw/aw/iW/aW）、引号对象（i"/a"/i'/a'/i`/a`）、括号对象（i(/a(/i)/a)/ib/ab、[]、{}、iB/aB、&lt;&gt;）。
/// 输入 = (text, offset, 对象类型键, inner/around)，输出 = [start, end) 偏移区间（未命中 null）——纯计算零副作用。
/// 对象类型键全集 = 基准 types.ts:164-180 TEXT_OBJ_TYPES（w W " ' ` ( ) b [ ] { } B &lt; &gt;）。
/// </summary>
public static class VimTextObject
{
    /// <summary>
    /// text-object 对象类型键判定 — 基准 types.ts:164-180 TEXT_OBJ_TYPES 集合成员判定。
    /// </summary>
    /// <param name="key">对象类型键字符。</param>
    /// <returns>是否为合法对象类型键。</returns>
    public static bool IsObjectKey(char key) =>
        key is 'w' or 'W' or '"' or '\'' or '`' or '(' or ')' or 'b'
            or '[' or ']' or '{' or '}' or 'B' or '<' or '>';

    /// <summary>
    /// 查找光标处 text-object 范围 — 基准 textObjects.ts:38-58 findTextObject：
    /// w → 词对象（词字符分类）；W → 大词对象（非空白分类）；成对界定符 → 引号/括号对象。
    /// </summary>
    /// <param name="text">全文镜像。</param>
    /// <param name="offset">光标偏移。</param>
    /// <param name="objectKey">对象类型键（见 <see cref="IsObjectKey"/>）。</param>
    /// <param name="isInner">inner（i 前缀）取内部；around（a 前缀）含界定符/周边空白。</param>
    /// <returns>[start, end) 区间；对象不存在返回 null。</returns>
    public static (int Start, int End)? Find(string text, int offset, char objectKey, bool isInner)
    {
        if (objectKey == 'w')
        {
            return FindWordObject(text, offset, isInner, bigWord: false);
        }

        if (objectKey == 'W')
        {
            return FindWordObject(text, offset, isInner, bigWord: true);
        }

        return TryGetPair(objectKey, out var open, out var close)
            ? (open == close
                ? FindQuoteObject(text, offset, open, isInner)
                : FindBracketObject(text, offset, open, close, isInner))
            : null;
    }

    /// <summary>
    /// 成对界定符表 — 基准 textObjects.ts:19-33 PAIRS：
    /// () 与 b 同组、{} 与 B 同组、[]、&lt;&gt;；引号 " ' ` 为同字符开闭。
    /// </summary>
    private static bool TryGetPair(char objectKey, out char open, out char close)
    {
        switch (objectKey)
        {
            case '(':
            case ')':
            case 'b':
                open = '(';
                close = ')';
                return true;
            case '[':
            case ']':
                open = '[';
                close = ']';
                return true;
            case '{':
            case '}':
            case 'B':
                open = '{';
                close = '}';
                return true;
            case '<':
            case '>':
                open = '<';
                close = '>';
                return true;
            case '"':
            case '\'':
            case '`':
                open = objectKey;
                close = objectKey;
                return true;
            default:
                open = '\0';
                close = '\0';
                return false;
        }
    }

    /// <summary>
    /// 词对象范围 — 基准 textObjects.ts:60-116 findWordObject：
    /// grapheme 预切分后按当前位置类别（词/空白/标点）向两侧扩展同类；
    /// 空白对象直接返回空白段（inner=around 同）；around 含尾随空白，无尾随则含前导
    ///（textObjects.ts:106-113）。光标在文本末尾边界时归最后一个 grapheme（基准 73-83 默认 graphemeIdx = length-1）。
    /// </summary>
    /// <param name="text">全文镜像。</param>
    /// <param name="offset">光标偏移（落在 grapheme 边界时归右侧 grapheme — 基准 offset &gt;= g.index &amp;&amp; offset &lt; nextStart）。</param>
    /// <param name="isInner">inner 仅同类段本身。</param>
    /// <param name="bigWord">词分类：false=vim 词字符（w）；true=非空白（W — 大词）。</param>
    private static (int Start, int End)? FindWordObject(string text, int offset, bool isInner, bool bigWord)
    {
        if (text.Length == 0)
        {
            return (0, 0); // 空文本空范围（基准 graphemeIdx=-1 → offsetAt(-1)=text.length=0）
        }

        // grapheme 预切分（全局边界点：boundaries[i] = 第 i 个 grapheme 起点）
        var boundaries = new List<int> { 0 };
        var start = 0;
        while (start < text.Length && GraphemeSegmenter.TryNext(text.AsSpan(), start, out var end))
        {
            boundaries.Add(end);
            start = end;
        }

        // 偏移归位到 grapheme 索引（末尾边界归最后一个 grapheme — 基准 graphemeIdx 默认 length-1）
        var graphemeCount = boundaries.Count - 1;
        var graphemeIndex = graphemeCount - 1;
        for (var i = 0; i < graphemeCount; i++)
        {
            if (offset >= boundaries[i] && offset < boundaries[i + 1])
            {
                graphemeIndex = i;
                break;
            }
        }

        int OffsetAt(int idx) => idx < graphemeCount ? boundaries[idx] : text.Length;
        ReadOnlySpan<char> GraphemeAt(int idx) => text.AsSpan(boundaries[idx], OffsetAt(idx + 1) - boundaries[idx]);
        bool IsWhitespaceAt(int idx) => VimMotions.IsVimWhitespace(GraphemeAt(idx));
        bool IsWordAt(int idx) => bigWord
            ? !IsWhitespaceAt(idx)
            : VimMotions.IsVimWordChar(GraphemeAt(idx));
        bool IsPunctuationAt(int idx) => VimMotions.IsVimPunctuation(GraphemeAt(idx));

        var startIndex = graphemeIndex;
        var endIndex = graphemeIndex;

        if (IsWordAt(graphemeIndex))
        {
            while (startIndex > 0 && IsWordAt(startIndex - 1))
            {
                startIndex--;
            }

            while (endIndex < graphemeCount && IsWordAt(endIndex))
            {
                endIndex++;
            }
        }
        else if (IsWhitespaceAt(graphemeIndex))
        {
            // 空白对象：直接返回空白段（inner/around 同 — 基准 97-100 提前 return）
            while (startIndex > 0 && IsWhitespaceAt(startIndex - 1))
            {
                startIndex--;
            }

            while (endIndex < graphemeCount && IsWhitespaceAt(endIndex))
            {
                endIndex++;
            }

            return (OffsetAt(startIndex), OffsetAt(endIndex));
        }
        else if (IsPunctuationAt(graphemeIndex))
        {
            while (startIndex > 0 && IsPunctuationAt(startIndex - 1))
            {
                startIndex--;
            }

            while (endIndex < graphemeCount && IsPunctuationAt(endIndex))
            {
                endIndex++;
            }
        }

        if (!isInner)
        {
            // around：优先含尾随空白，无尾随则含前导（基准 106-113）
            if (endIndex < graphemeCount && IsWhitespaceAt(endIndex))
            {
                while (endIndex < graphemeCount && IsWhitespaceAt(endIndex))
                {
                    endIndex++;
                }
            }
            else if (startIndex > 0 && IsWhitespaceAt(startIndex - 1))
            {
                while (startIndex > 0 && IsWhitespaceAt(startIndex - 1))
                {
                    startIndex--;
                }
            }
        }

        return (OffsetAt(startIndex), OffsetAt(endIndex));
    }

    /// <summary>
    /// 引号对象范围 — 基准 textObjects.ts:118-147 findQuoteObject：
    /// 限定当前逻辑行内，引号按出现顺序两两配对（0-1、2-3…）；光标在配对区间内（含端点）命中；
    /// inner 排除两端引号，around 含引号。光标不在任何配对内返回 null。
    /// </summary>
    private static (int Start, int End)? FindQuoteObject(string text, int offset, char quote, bool isInner)
    {
        if (text.Length == 0)
        {
            return null;
        }

        var lineStart = 0;
        for (var i = Math.Clamp(offset - 1, 0, text.Length - 1); i >= 0; i--)
        {
            if (text[i] == '\n')
            {
                lineStart = i + 1;
                break;
            }
        }

        var lineEnd = text.Length;
        for (var i = offset; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineEnd = i;
                break;
            }
        }

        var posInLine = Math.Clamp(offset, lineStart, lineEnd) - lineStart;

        // 行内引号位置收集 + 顺序配对（0-1、2-3… — 基准 130-136）
        var quoteCount = 0;
        for (var i = lineStart; i < lineEnd; i++)
        {
            if (text[i] == quote)
            {
                quoteCount++;
            }
        }

        if (quoteCount < 2)
        {
            return null;
        }

        var positions = new int[quoteCount];
        var index = 0;
        for (var i = lineStart; i < lineEnd; i++)
        {
            if (text[i] == quote)
            {
                positions[index++] = i - lineStart;
            }
        }

        for (var i = 0; i + 1 < positions.Length; i += 2)
        {
            var quoteStart = positions[i];
            var quoteEnd = positions[i + 1];
            if (quoteStart <= posInLine && posInLine <= quoteEnd)
            {
                return isInner
                    ? (lineStart + quoteStart + 1, lineStart + quoteEnd)
                    : (lineStart + quoteStart, lineStart + quoteEnd + 1);
            }
        }

        return null;
    }

    /// <summary>
    /// 括号对象范围 — 基准 textObjects.ts:149-186 findBracketObject：
    /// 向后扫未闭合 open（深度计数；光标所在 close 不计深度 — 站在 ')' 上也能选中整对），
    /// 向前扫匹配 close；inner 排除界定符，around 含界定符。任一端未闭合返回 null。
    /// char 级扫描（界定符为 ASCII，与基准一致）。
    /// </summary>
    private static (int Start, int End)? FindBracketObject(
        string text,
        int offset,
        char open,
        char close,
        bool isInner)
    {
        if (text.Length == 0)
        {
            return null;
        }

        var clamped = Math.Clamp(offset, 0, text.Length - 1);

        // 向后找未闭合 open（基准 158-168）
        var depth = 0;
        var start = -1;
        for (var i = clamped; i >= 0; i--)
        {
            if (text[i] == close && i != clamped)
            {
                depth++;
            }
            else if (text[i] == open)
            {
                if (depth == 0)
                {
                    start = i;
                    break;
                }

                depth--;
            }
        }

        if (start == -1)
        {
            return null;
        }

        // 向前找匹配 close（基准 171-182）
        depth = 0;
        var end = -1;
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close)
            {
                if (depth == 0)
                {
                    end = i;
                    break;
                }

                depth--;
            }
        }

        if (end == -1)
        {
            return null;
        }

        return isInner ? (start + 1, end) : (start, end + 1);
    }
}
