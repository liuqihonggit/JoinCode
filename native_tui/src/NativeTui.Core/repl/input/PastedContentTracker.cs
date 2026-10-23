namespace NativeTui.Core.Repl;

/// <summary>
/// 粘贴内容登记簿 — 对齐基准 PromptInput.tsx onTextPaste / inputPaste.ts / history.ts 的粘贴折叠三件套：
/// ① 折叠：&gt;800 字符或行数超限的粘贴块登记为 [Pasted text #N +M lines] 引用（原文保留在登记簿）；
/// ② 截断：全输入超 10000 字符时留前/后 500 字符，中段折叠为 [...Truncated text #N +M lines...]；
/// ③ 展开：提交时把引用替换回原文（expandPastedTextRefs）— 显示折叠形态，提交全文语义。
/// 纯数据类：不做键盘/渲染决策，InputBuffer 持有并在 Paste 入口联动。
/// </summary>
public sealed partial class PastedContentTracker
{
    /// <summary>粘贴折叠阈值（基准 PASTE_THRESHOLD = 800）。</summary>
    public const int PasteThreshold = 800;

    /// <summary>全输入截断阈值（基准 TRUNCATION_THRESHOLD = 10000）。</summary>
    public const int TruncationThreshold = 10000;

    /// <summary>截断保留预览长度（基准 PREVIEW_LENGTH = 1000 — 前 500 + 后 500）。</summary>
    public const int PreviewLength = 1000;

    /// <summary>折叠引用行数上限默认值（基准 maxLines = min(rows - 10, 2) 的保守基线）。</summary>
    public const int DefaultMaxVisibleLines = 2;

    private readonly Dictionary<int, string> _contents = [];
    private int _nextId = 1;

    /// <summary>已登记的粘贴块数。</summary>
    public int Count => _contents.Count;

    /// <summary>按编号取登记原文（未登记返回 null）。</summary>
    public string? TryGetContent(int id) => _contents.TryGetValue(id, out var content) ? content : null;

    /// <summary>
    /// 粘贴入口：清洗（去 ANSI 转义 / \r 归一 \n / Tab 展开 4 空格 — 基准 onTextPaste 清洗链）
    /// 后判定折叠 — 超阈值（&gt;800 字符或行数 &gt; maxVisibleLines）登记并返回折叠引用，否则原文直插。
    /// </summary>
    public string RegisterPaste(string rawText, int maxVisibleLines = DefaultMaxVisibleLines)
    {
        var text = Sanitize(rawText);
        var numLines = CountNewlines(text);
        if (text.Length <= PasteThreshold && numLines <= maxVisibleLines)
        {
            return text;
        }

        var id = _nextId++;
        _contents[id] = text;
        return FormatPastedTextRef(id, numLines);
    }

    /// <summary>
    /// 全输入超长截断（基准 maybeTruncateMessageForInput）— 超 10000 字符时保留前/后 500 字符，
    /// 中段登记并折叠为 [...Truncated text #N +M lines...]；未超限原样返回。
    /// </summary>
    public string TruncateIfTooLong(string input)
    {
        if (input.Length <= TruncationThreshold)
        {
            return input;
        }

        var startLength = PreviewLength / 2;
        var endLength = PreviewLength / 2;
        var startText = input[..startLength];
        var endText = input[^endLength..];
        var placeholderContent = input[startLength..^endLength];

        var id = _nextId++;
        _contents[id] = placeholderContent;
        return startText + FormatTruncatedTextRef(id, CountNewlines(placeholderContent)) + endText;
    }

    /// <summary>
    /// 展开输入中的折叠引用为登记原文（基准 expandPastedTextRefs）— 倒序替换保偏移有效；
    /// 仅展开文本类引用（Pasted text / ...Truncated text — Image 引用编号独立，原样保留）；
    /// 未登记编号原样保留。提交语义：调用方以本方法结果作为提交全文。
    /// </summary>
    public string ExpandReferences(string input)
    {
        var refs = ParseReferences(input);
        return refs
            .Where(reference => reference.IsText && _contents.ContainsKey(reference.Id))
            .OrderByDescending(reference => reference.Index)
            .Aggregate(input, ReplaceReference);
    }

    /// <summary>清空登记簿（提交后随缓冲清空联动）。编号会话级只增不复用（7.8 — 基准
    /// nextPasteIdRef 只增：防历史残留 [Pasted text #N] 与新登记撞号误替换）。</summary>
    public void Clear()
    {
        _contents.Clear();
    }

    /// <summary>当前登记快照（DSG045 — 提交入史携带：Clear 前由调用方取）。
    /// 返回副本 — 提交后 Clear 清空原字典不穿透已入史快照（引用共享会连历史条目一起清空）。</summary>
    public IReadOnlyDictionary<int, string> Snapshot => _contents.ToDictionary();

    /// <summary>整体替换注入历史粘贴登记（DSG045 — 导航恢复回放：对齐基准恢复态直接替换语义，
    /// 不叠加旧登记防撞号）；编号衔接取 max（7.8 会话级只增不回退 — 恢复注入后新粘贴续增不撞号）。</summary>
    public void LoadPasted(IReadOnlyDictionary<int, string>? contents)
    {
        _contents.Clear();
        var maxId = 0;
        if (contents is not null)
        {
            foreach (var (id, text) in contents)
            {
                _contents[id] = text;
                maxId = Math.Max(maxId, id);
            }
        }

        _nextId = Math.Max(_nextId, maxId + 1);
    }

    /// <summary>折叠引用格式（基准 formatPastedTextRef）— 无换行 [Pasted text #N]，有换行 [Pasted text #N +M lines]。</summary>
    internal static string FormatPastedTextRef(int id, int numLines) =>
        numLines == 0 ? $"[Pasted text #{id}]" : $"[Pasted text #{id} +{numLines} lines]";

    /// <summary>截断占位格式（基准 formatTruncatedTextRef）— [...Truncated text #N +M lines...]。</summary>
    internal static string FormatTruncatedTextRef(int id, int numLines) =>
        $"[...Truncated text #{id} +{numLines} lines...]";

    /// <summary>换行计数（基准 getPastedTextRefNumLines — "a\nb\nc" 计 2 非 3：数分隔符非段数；\r\n 合并计 1）。</summary>
    internal static int CountNewlines(string text)
    {
        var count = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\r')
            {
                count++;
                i += i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }
            else
            {
                if (text[i] == '\n')
                {
                    count++;
                }

                i++;
            }
        }

        return count;
    }

    /// <summary>
    /// 粘贴清洗（基准 onTextPaste：stripAnsi + \r→\n + Tab→4 空格）—
    /// ANSI 转义序列（ESC [ ... final / ESC O final / ESC 单字符）整体剥离防泄漏。
    /// </summary>
    internal static string Sanitize(string rawText) =>
        ExpandTabs(NormalizeCarriageReturns(StripAnsi(rawText)));

    /// <summary>解析折叠引用（基准 parseReferences）— [Pasted text|Image|...Truncated text #N (+M lines)?]，编号须为正。</summary>
    private static List<Reference> ParseReferences(string input)
    {
        return ReferenceRegex().Matches(input)
            .Select(ToReference)
            .Where(reference => reference.Id > 0)
            .ToList();
    }

    /// <summary>正则命中转引用记录（编号为第 2 捕获组、类型为第 1 捕获组；解析失败按 0 过滤）。</summary>
    private static Reference ToReference(Match match) =>
        new(
            int.TryParse(match.Groups[2].Value, out var id) ? id : 0,
            match.Value,
            match.Index,
            IsTextReference(match.Groups[1].Value));

    /// <summary>文本类引用判定 — Image 引用不参与文本展开（基准 content.type !== 'text' 跳过）。</summary>
    private static bool IsTextReference(string kind) => kind != "Image";

    /// <summary>倒序替换单条引用为登记原文。</summary>
    private string ReplaceReference(string expanded, Reference reference) =>
        reference.Index >= expanded.Length
            || !expanded.AsSpan(reference.Index).StartsWith(reference.Match, StringComparison.Ordinal)
            ? expanded
            : string.Concat(expanded.AsSpan(0, reference.Index), _contents[reference.Id],
                expanded.AsSpan(reference.Index + reference.Match.Length));

    /// <summary>剥离 ANSI 转义序列 — ESC [ params final（final 为 0x40-0x7E）/ ESC O final / ESC + 单字符。</summary>
    private static string StripAnsi(string text)
    {
        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '\u001b')
            {
                builder.Append(text[i]);
                i++;
                continue;
            }

            i = SkipEscape(text, i);
        }

        return builder.ToString();
    }

    /// <summary>跳过一条 ESC 转义序列，返回下一未消费下标。</summary>
    private static int SkipEscape(string text, int start)
    {
        var i = start + 1;
        if (i >= text.Length)
        {
            return i;
        }

        return text[i] switch
        {
            '[' => SkipUntilFinalByte(text, i + 1),
            'O' => Math.Min(i + 2, text.Length),
            _ => i + 1,
        };
    }

    /// <summary>CSI 参数区扫描 — 消费到 final 字节（0x40-0x7E）为止。</summary>
    private static int SkipUntilFinalByte(string text, int start)
    {
        var i = start;
        while (i < text.Length && text[i] < 0x40)
        {
            i++;
        }

        return Math.Min(i + 1, text.Length);
    }

    /// <summary>\r 归一为 \n（含 \r\n 折叠为单个 \n）。</summary>
    private static string NormalizeCarriageReturns(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>Tab 展开 4 空格（基准 replaceAll('\t', '    ')）。</summary>
    private static string ExpandTabs(string text) => text.Replace("\t", "    ");

    /// <summary>引用识别正则（基准 referencePattern 原样迁移，GeneratedRegex AOT 源生成）。</summary>
    [GeneratedRegex(@"\[(Pasted text|Image|\.\.\.Truncated text) #(\d+)(?: \+\d+ lines)?(\.)*\]")]
    private static partial Regex ReferenceRegex();

    /// <summary>折叠引用记录 — 编号 / 完整匹配 / 起始偏移 / 是否文本类。</summary>
    /// <param name="Id">引用编号。</param>
    /// <param name="Match">完整匹配文本。</param>
    /// <param name="Index">输入内起始偏移。</param>
    /// <param name="IsText">文本类引用（Pasted text / ...Truncated text — 可展开；Image 不可）。</param>
    private readonly record struct Reference(int Id, string Match, int Index, bool IsText);
}
