namespace NativeTui.Core.Repl.Attachments;

/// <summary>
/// 单行图片路径识别结果 — 基准 asImageFilePath/tryReadImageFromPath 的纯路径判定部分
///（imagePaste.ts:324-344）：清理后的路径 + 是否绝对路径（宿主决定直接读文件还是
/// 与剪贴板文本路径 basename 匹配 — 基准 imagePaste.ts:365-374 VSCode 终端只粘贴文件名场景）
/// + 文件名（basename 剪贴板匹配用）。文件读取/base64 转换属宿主域，Core 只做文本形态解析。
/// </summary>
/// <param name="Path">清理后路径（引号/转义剥离后）。</param>
/// <param name="IsAbsolute">是否绝对路径（win32 盘符/UNC；unix / 前缀 — node path.isAbsolute 语义）。</param>
/// <param name="Filename">路径 basename（无分隔符时为整串）。</param>
public sealed record ImagePasteMatch(string Path, bool IsAbsolute, string Filename);

/// <summary>
/// 粘贴内容图片识别聚合结果 — 基准 usePasteHandler.ts:126-164 粘贴超时批处理形态：
/// 多图拖拽粘贴以换行分隔（部分终端）或空格+绝对路径前瞻分隔（Finder 拖拽）到达，
/// 逐行识别后拆分为图片路径集合与非图片行集合；有效图全部读取失败时宿主以原文回退普通文本粘贴
///（基准 usePasteHandler.ts:169-173）。
/// </summary>
/// <param name="ImagePaths">识别为图片路径的行。</param>
/// <param name="NonImageLines">非图片行（回退文本粘贴数据源 — 基准 onPaste(nonImageLines.join('\n'))）。</param>
public sealed record ImagePasteResult(
    IReadOnlyList<ImagePasteMatch> ImagePaths,
    IReadOnlyList<string> NonImageLines)
{
    /// <summary>是否含图片路径（粘贴通道走图片分支的判定 — 基准 imagePaths.length &gt; 0）。</summary>
    public bool HasImage => ImagePaths.Count > 0;

    /// <summary>空结果（null/空粘贴 — 无图无文本）。</summary>
    public static ImagePasteResult Empty { get; } = new([], []);
}

/// <summary>
/// 粘贴内容 → 图片路径识别器 — 基准图片粘贴文本形态协议（imagePaste.ts + usePasteHandler.ts）：
/// 终端把图传给 TUI 的文本形态 = <b>图片文件路径文本</b>（拖拽文件/复制文件时终端粘贴路径字符串），
/// 无 OSC 52 / kitty graphics / base64 标记协议；剪贴板位图数据不经终端通道（宿主域读剪贴板）。
/// 识别链（基准 isImageFilePath — imagePaste.ts:324-328）：trim → 剥外层成对引号 →
/// 剥 shell 转义反斜杠（仅非 Windows — Windows 反斜杠是路径组成部分）→ 扩展名正则匹配
///（IMAGE_EXTENSION_REGEX — imagePaste.ts:270，png/jpe?g/gif/webp 大小写不敏感）。
/// 多行分割（基准 usePasteHandler.ts:126-130）：空格+绝对路径前瞻分割（` /`、` C:\`）→
/// 换行分割 → 去空行；尾部焦点事件残留（[I/[O）剥离（基准 usePasteHandler.ts:113-116）。
/// 纯文本解析零 IO；识别不了返回 null/空走普通文本粘贴。
/// </summary>
public static partial class ImagePasteDetector
{
    /// <summary>
    /// 整块粘贴内容识别（public 入口 — 运行时平台分支）。
    /// </summary>
    /// <param name="pastedText">bracketed paste 到达的粘贴原文（可为空 — 剪贴板位图粘贴终端发空块）。</param>
    /// <returns>图片路径 + 非图片行聚合结果；空粘贴返回 <see cref="ImagePasteResult.Empty"/>。</returns>
    public static ImagePasteResult Detect(string? pastedText) => Detect(pastedText, OperatingSystem.IsWindows());

    /// <summary>
    /// 整块粘贴内容识别（internal 平台参数形态 — 测试双平台分支覆盖）：
    /// 分割为行 → 逐行单行识别 → 图片路径/非图片行两桶聚合。
    /// </summary>
    /// <param name="pastedText">粘贴原文。</param>
    /// <param name="isWindows">平台语义（转义剥离仅非 Windows — 基准 stripBackslashEscapes imagePaste.ts:293-317）。</param>
    /// <returns>聚合识别结果。</returns>
    internal static ImagePasteResult Detect(string? pastedText, bool isWindows)
    {
        var detected = SplitPasteLines(pastedText)
            .Select(line => (Line: line, Match: DetectSingle(line, isWindows)))
            .ToList();
        return new(
            [.. detected.Where(entry => entry.Match is not null).Select(entry => entry.Match!)],
            [.. detected.Where(entry => entry.Match is null).Select(entry => entry.Line)]);
    }

    /// <summary>
    /// 单行图片路径识别（public 入口 — 运行时平台分支）：trim → 引号/转义剥离 → 扩展名匹配。
    /// </summary>
    /// <param name="line">单行粘贴文本（多图粘贴分割后的一行）。</param>
    /// <returns>识别命中（清理路径/绝对判定/文件名）；非图片形态返回 null 走普通文本粘贴。</returns>
    public static ImagePasteMatch? DetectSingle(string? line) => DetectSingle(line, OperatingSystem.IsWindows());

    /// <summary>
    /// 单行图片路径识别（internal 平台参数形态 — 测试双平台分支覆盖）：
    /// trim（基准 imagePaste.ts:325 text.trim()）→ 剥外层成对引号（removeOuterQuotes 277-285）→
    /// 非 Windows 剥 shell 转义（stripBackslashEscapes 293-317 — Windows 返回原样）→
    /// IMAGE_EXTENSION_REGEX 匹配（imagePaste.ts:270 — 只看结尾扩展名，
    /// .png 结尾的 URL 同样命中，与基准 JS regex 语义一致；误命中由宿主读文件失败自然回退）。
    /// </summary>
    /// <param name="line">单行粘贴文本。</param>
    /// <param name="isWindows">平台语义。</param>
    /// <returns>识别命中；非图片形态返回 null。</returns>
    internal static ImagePasteMatch? DetectSingle(string? line, bool isWindows)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var unquoted = RemoveOuterQuotes(line.Trim());
        var unescaped = isWindows ? unquoted : StripBackslashEscapes(unquoted);
        return ImageExtensionRegex().IsMatch(unescaped)
            ? new ImagePasteMatch(unescaped, IsAbsolutePath(unescaped, isWindows), ExtractFilename(unescaped))
            : null;
    }

    /// <summary>
    /// 粘贴行分割（基准 usePasteHandler.ts:110-129）— 尾部焦点事件残留剥离
    ///（[I/[O 各剥一次 — 基准 replace(/\[I$/,'') + replace(/\[O$/,'')，焦点事件拆分粘贴块时
    /// ESC 剥离后残留的文本尾巴）→ 空格+绝对路径前瞻分割（` /`、` C:\` — Finder 拖拽多图
    /// 空格分隔形态；路径内空格已被终端转义为 `\ ` 不会被误分割）→ 换行分割 → 去空行。
    /// </summary>
    /// <param name="pastedText">粘贴原文（null/空返回空列表）。</param>
    /// <returns>非空行集合（保留行内原文 — 基准 lines.filter(line =&gt; line.trim()) 不过滤内容只去空行）。</returns>
    private static List<string> SplitPasteLines(string? pastedText)
    {
        if (string.IsNullOrEmpty(pastedText))
        {
            return [];
        }

        return MultiPathSplitRegex().Split(StripTrailingFocusSequence(pastedText))
            .SelectMany(part => part.Split('\n'))
            .Where(line => line.Trim().Length > 0)
            .ToList();
    }

    /// <summary>尾部焦点残留剥离 — [I / [O 各至多剥一次（$ 锚定末尾 — 基准 replace 无 g flag 只替换首个且锚定结尾）。</summary>
    /// <param name="text">粘贴文本。</param>
    /// <returns>剥离后的文本。</returns>
    internal static string StripTrailingFocusSequence(string text) =>
        StripFocusTail(StripFocusTail(text, "[I"), "[O");

    /// <summary>单次尾部尾巴剥离（[I 或 [O — 不匹配原样返回）。</summary>
    private static string StripFocusTail(string text, string tail) =>
        text.EndsWith(tail, StringComparison.Ordinal) ? text[..^tail.Length] : text;

    /// <summary>
    /// 剥外层成对引号（基准 removeOuterQuotes — imagePaste.ts:277-285）：
    /// 首尾同为单引号或同为双引号才剥（不对称引号原样保留 — 基准 startsWith &amp;&amp; endsWith 语义）。
    /// </summary>
    /// <param name="text">已 trim 文本。</param>
    /// <returns>剥引号后文本。</returns>
    internal static string RemoveOuterQuotes(string text)
    {
        var span = text.AsSpan();
        return span.Length >= 2 && (span[0] == '"' || span[0] == '\'') && span[^1] == span[0]
            ? span[1..^1].ToString()
            : text;
    }

    /// <summary>
    /// 剥 shell 转义反斜杠（基准 stripBackslashEscapes — imagePaste.ts:293-317，仅 macOS/Linux/WSL）：
    /// 双反斜杠（\\）→ 单反斜杠（文件名中的字面反斜杠）；反斜杠+字符 → 字符（shell 转义，
    /// 如 name\ \(15\).png → name (15).png）；孤尾反斜杠无后续字符不匹配转义对 → 保留
    ///（基准 regex \\(.) 全局替换语义）。单遍扫描天然无占位符注入问题（基准用随机 salt
    /// 占位符防注入 — 两遍 replace 的必要性在此消解，语义等价）。
    /// </summary>
    /// <param name="path">可能含 shell 转义的路径。</param>
    /// <returns>转义剥离后路径。</returns>
    internal static string StripBackslashEscapes(string path)
    {
        var span = path.AsSpan();
        var builder = new StringBuilder(path.Length);
        var i = 0;
        while (i < span.Length)
        {
            if (span[i] != '\\')
            {
                builder.Append(span[i]);
                i++;
                continue;
            }

            if (i + 1 >= span.Length)
            {
                builder.Append('\\');
                i++;
                continue;
            }

            builder.Append(span[i + 1]);
            i += 2;
        }

        return builder.ToString();
    }

    /// <summary>
    /// 绝对路径判定（node path.isAbsolute 语义 — 基准 isAbsolute 调用 imagePaste.ts:365）：
    /// win32 — UNC（\\ 开头）或 盘符:/ 或 盘符:\（`C:` 裸盘符不算 — node 语义）；
    /// unix — / 开头。
    /// </summary>
    /// <param name="path">清理后路径。</param>
    /// <param name="isWindows">平台语义。</param>
    /// <returns>是否绝对路径。</returns>
    internal static bool IsAbsolutePath(string path, bool isWindows) =>
        isWindows
            ? path.StartsWith("\\\\", StringComparison.Ordinal)
                || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '/' || path[2] == '\\'))
            : path.Length > 0 && path[0] == '/';

    /// <summary>
    /// 路径 basename 提取（node basename — 基准 imagePaste.ts:372 剪贴板路径文件名匹配用）：
    /// 取最后一个 \ 或 / 之后段（跨平台分隔符都认 — 粘贴路径形态不定）；无分隔符返回整串。
    /// </summary>
    /// <param name="path">清理后路径。</param>
    /// <returns>basename。</returns>
    internal static string ExtractFilename(string path)
    {
        var separator = path.LastIndexOfAny(['\\', '/']);
        return separator >= 0 ? path[(separator + 1)..] : path;
    }

    /// <summary>
    /// 图片扩展名正则（基准 IMAGE_EXTENSION_REGEX — imagePaste.ts:270
    /// /\.(png|jpe?g|gif|webp)$/i；与 MIME_BY_EXT 同步约束见基准注释 — bmp 不在列）。
    /// </summary>
    [GeneratedRegex(@"\.(png|jpe?g|gif|webp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageExtensionRegex();

    /// <summary>
    /// 多路径分割正则（基准 usePasteHandler.ts:127 / (?=\/|[A-Za-z]:\\)/）—
    /// 空格 + 前瞻（unix / 或 win 盘符:\）处分割（Finder 拖拽多图空格分隔形态）。
    /// </summary>
    [GeneratedRegex(@" (?=/|[A-Za-z]:\\)")]
    private static partial Regex MultiPathSplitRegex();
}
