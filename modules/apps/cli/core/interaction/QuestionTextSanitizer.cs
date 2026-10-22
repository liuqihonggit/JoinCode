namespace JoinCode.Cli.Interaction;

/// <summary>
/// 问题文本规整器 — AI 生成文本（question/options/description）入终端前的统一清洗与折行：
/// ①<see cref="Sanitize"/>：行尾归一（CRLF/裸 CR/病态 \r 段 → 单 \n，AI 输出混入 CR 是超长换行根因）、
/// ESC 剥除（防终端二次解释 ANSI）、NUL 等控制字符剥除、TAB 转空格（对齐稳定）；
/// ②<see cref="Wrap"/>：按显示宽度词边界折行（CJK 宽字符 2 列计）、悬挂缩进对齐、超宽词硬切防单行超屏。
/// 纯函数零状态 — 终端交互与 TUI 选项渲染共用同一口径。
/// </summary>
public static class QuestionTextSanitizer {
    /// <summary>
    /// 清洗文本 — 行尾归一（\r 段折叠单 \n）+ 剥控制字符（ESC/NUL 等 &lt;0x20 除 \n）+ TAB 转空格。
    /// 顺序扫描单遍完成（\r 段贪吃 + 段后无 \n 自补一个 — CR 语义即换行）。
    /// </summary>
    /// <param name="text">原始文本（AI 生成，可能混入 CR/控制字符）。</param>
    /// <returns>干净文本（行尾统一 \n，无 ESC/控制字符）。</returns>
    public static string Sanitize(string text) {
        if (text.Length == 0)
            return text;

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length) {
            var ch = text[index];
            if (ch == '\r') {
                // \r 段贪吃（\r\r\n 等病态混合折叠为单换行；\n\r 混合防双换行 — 尾已是 \n 不补）
                while (index < text.Length && text[index] == '\r') {
                    index++;
                }

                if (index < text.Length && text[index] == '\n') {
                    index++;
                }

                if (builder.Length == 0 || builder[^1] != '\n') {
                    builder.Append('\n');
                }

                continue;
            }

            if (ch == '\n') {
                builder.Append('\n');
                index++;
                continue;
            }

            if (ch == '\t') {
                builder.Append(' ');
                index++;
                continue;
            }

            if (ch < ' ') {
                // C0 控制字符剥除（ESC 防终端二次解释 ANSI、NUL 等）
                index++;
                continue;
            }

            builder.Append(ch);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// 按显示宽度折行 — 段落（\n）保留独立折行；空格分词词边界换行；CJK 宽字符按 2 列计；
    /// 续行悬挂缩进 indent 空格（可用宽度 = width - indent）；超宽词按可用宽度硬切。
    /// 宽度钳制：width ≤ indent 或 ≤0 返回单行原文（防退化）。
    /// </summary>
    /// <param name="text">已清洗文本（\n 行尾）。</param>
    /// <param name="width">目标显示宽度（显示列）。</param>
    /// <param name="indent">续行悬挂缩进列数。</param>
    /// <returns>折行结果（每元素 ≤ width 显示列；空文本单空行）。</returns>
    public static IReadOnlyList<string> Wrap(string text, int width, int indent) {
        if (width <= indent || width <= 0) {
            return [text];
        }

        var lines = new List<string>();
        foreach (var segment in text.Split('\n')) {
            WrapSegment(segment, width, indent, lines);
        }

        return lines.Count > 0 ? lines : [string.Empty];
    }

    /// <summary>单段折行 — 词序列逐词装行（首行与续行同内容宽 available，续行前置 indent 悬挂缩进）；
    /// 超宽词硬切后并入装行流程；段恰装满时尾空行不输出（空段保留单空行 — 段落边界语义）。</summary>
    private static void WrapSegment(string segment, int width, int indent, List<string> lines) {
        var available = width - indent;
        var current = new StringBuilder();
        var currentWidth = 0;
        var pendingIndent = false;
        foreach (var word in SplitWords(segment)) {
            if (DisplayWidth(word) > available) {
                FlushCurrent();
                AppendHardChunks(word, available, indent, lines, pendingIndent ? indent : 0);
                pendingIndent = true;
                continue;
            }

            var separatorWidth = currentWidth == 0 ? 0 : 1;
            if (currentWidth + separatorWidth + DisplayWidth(word) > available) {
                FlushCurrent();
            }

            if (currentWidth == 0 && pendingIndent) {
                current.Append(' ', indent);
            }

            if (currentWidth > 0) {
                current.Append(' ');
                currentWidth++;
            }

            current.Append(word);
            currentWidth += DisplayWidth(word);
        }

        if (current.Length > 0 || lines.Count == 0) {
            lines.Add(current.ToString());
        }

        void FlushCurrent() {
            if (current.Length > 0) {
                lines.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
                pendingIndent = true;
            }
        }
    }

    /// <summary>超宽词硬切 — 按可用宽度逐片切（首片前缀对齐前文形态；宽字符不跨片边界：
    /// 片尾恰逢宽字符占满则该片提前收口）。</summary>
    private static void AppendHardChunks(string word, int available, int indent, List<string> lines, int firstPrefix) {
        var index = 0;
        var prefix = new string(' ', firstPrefix);
        var continuationPrefix = new string(' ', indent);
        while (index < word.Length) {
            var chunkWidth = 0;
            var end = index;
            while (end < word.Length && chunkWidth + CharWidth(word[end]) <= available) {
                chunkWidth += CharWidth(word[end]);
                end++;
            }

            if (end == index) {
                end = index + 1;  // 单字符超宽防御（不可能路径 — 字符宽 ≤2 且 available ≥1）
            }

            lines.Add(prefix + word[index..end]);
            index = end;
            prefix = continuationPrefix;
        }
    }

    /// <summary>空格分词（多空格折叠单分隔 — 连续空格语义等价单空格）。</summary>
    private static IEnumerable<string> SplitWords(string segment) =>
        segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>文本显示宽度（逐字符宽度和 — CJK 宽字符 2 列）。</summary>
    private static int DisplayWidth(string text) {
        var width = 0;
        foreach (var ch in text) {
            width += CharWidth(ch);
        }

        return width;
    }

    /// <summary>单字符显示宽 — ASCII/半角 1 列、CJK 全角 2 列（通用表意文字/扩展/全角符号/韩文范围）。</summary>
    private static int CharWidth(char ch) =>
        ch >= 0x1100 && (ch <= 0x115F                        // Hangul Jamo
            || ch >= 0x2E80 && ch <= 0xA4CF                  // CJK 部首/康熙/注音/中日韩符号/平假名/片假名/兼容
            || ch >= 0xAC00 && ch <= 0xD7A3                  // Hangul 音节
            || ch >= 0xF900 && ch <= 0xFAFF                  // CJK 兼容表意
            || ch >= 0xFE30 && ch <= 0xFE4F                  // CJK 兼容形式
            || ch >= 0xFF00 && ch <= 0xFF60                  // 全角形式
            || ch >= 0xFFE0 && ch <= 0xFFE6                  // 全角符号
            || ch >= 0x4E00 && ch <= 0x9FFF                  // CJK 统一表意文字
            || ch >= 0x3000 && ch <= 0x303F)                 // CJK 标点
            ? 2 : 1;
}
