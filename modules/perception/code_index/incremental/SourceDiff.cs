namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// 源码差异计算 — 基于公共前后缀计算最小编辑区间，用于增量解析
/// </summary>
public static class SourceDiff {
    /// <summary>
    /// 计算从旧源码到新源码的最小编辑 — 公共前缀/后缀外的中间部分为变更区间
    /// </summary>
    /// <param name="oldSource">旧源码</param>
    /// <param name="newSource">新源码</param>
    /// <returns>编辑描述，含字节偏移和行列位置</returns>
    public static Edit ComputeEdit(string oldSource, string newSource) {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(newSource);

        if (oldSource == newSource) {
            return new Edit {
                StartIndex = 0,
                OldEndIndex = 0,
                NewEndIndex = 0,
                StartPosition = new Point(0, 0),
                OldEndPosition = new Point(0, 0),
                NewEndPosition = new Point(0, 0)
            };
        }

        var commonPrefixLen = CommonPrefixLength(oldSource, newSource);
        var commonSuffixLen = CommonSuffixLength(oldSource, newSource, commonPrefixLen);

        var startCharIndex = commonPrefixLen;
        var oldEndCharIndex = oldSource.Length - commonSuffixLen;
        var newEndCharIndex = newSource.Length - commonSuffixLen;

        var startIndex = CharToByteOffset(oldSource, startCharIndex);
        var oldEndIndex = CharToByteOffset(oldSource, oldEndCharIndex);
        var newEndIndex = CharToByteOffset(newSource, newEndCharIndex);

        var startPosition = OffsetToPosition(oldSource, startCharIndex);
        var oldEndPosition = OffsetToPosition(oldSource, oldEndCharIndex);
        var newEndPosition = OffsetToPosition(newSource, newEndCharIndex);

        return new Edit {
            StartIndex = startIndex,
            OldEndIndex = oldEndIndex,
            NewEndIndex = newEndIndex,
            StartPosition = startPosition,
            OldEndPosition = oldEndPosition,
            NewEndPosition = newEndPosition
        };
    }

    /// <summary>
    /// 将字符偏移转换为 UTF-8 字节偏移 — Tree-sitter 使用字节偏移
    /// </summary>
    /// <param name="source">源码字符串</param>
    /// <param name="charOffset">字符偏移</param>
    /// <returns>对应的 UTF-8 字节偏移</returns>
    public static int CharToByteOffset(string source, int charOffset) {
        ArgumentNullException.ThrowIfNull(source);
        if (charOffset <= 0) return 0;
        var clampedOffset = Math.Min(charOffset, source.Length);
        return System.Text.Encoding.UTF8.GetByteCount(source.AsSpan(0, clampedOffset));
    }

    private static int CommonPrefixLength(string a, string b) {
        var minLen = Math.Min(a.Length, b.Length);
        for (var i = 0; i < minLen; i++) {
            if (a[i] != b[i]) return i;
        }
        return minLen;
    }

    private static int CommonSuffixLength(string a, string b, int prefixLen) {
        var maxSuffixLen = Math.Min(a.Length, b.Length) - prefixLen;
        for (var i = 0; i < maxSuffixLen; i++) {
            if (a[a.Length - 1 - i] != b[b.Length - 1 - i]) return i;
        }
        return maxSuffixLen;
    }

    private static Point OffsetToPosition(string source, int charOffset) {
        if (charOffset <= 0) return new Point(0, 0);

        var row = 0;
        var lineStartCharIndex = 0;

        for (var i = 0; i < charOffset && i < source.Length; i++) {
            if (source[i] == '\n') {
                row++;
                lineStartCharIndex = i + 1;
            }
        }

        var columnCharCount = Math.Min(charOffset, source.Length) - lineStartCharIndex;
        var columnByteCount = System.Text.Encoding.UTF8.GetByteCount(source.AsSpan(lineStartCharIndex, columnCharCount));
        return new Point(row, columnByteCount);
    }
}