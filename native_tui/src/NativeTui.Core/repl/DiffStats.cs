namespace NativeTui.Core.Repl;

/// <summary>
/// diff 统计头（TASK069-M3 — 对齐基准 components/FileEditToolUpdatedMessage.tsx L24-55）：
/// `Added N lines, removed M lines`；仅一侧有变更时省略另一侧（仅删除为 `Removed M lines`，
/// 基准 `numAdditions === 0 ? "R" : "r"`）；单复数按 &gt;1 判；数字 bold 由渲染层加。
/// 基准该行是编辑结果的首行（`⎿` 后），diff 紧随其后。
/// </summary>
public readonly record struct DiffStats(int Added, int Removed)
{
    /// <summary>无变更（两侧皆 0 — 不渲染统计行）。</summary>
    public bool IsEmpty => Added == 0 && Removed == 0;

    /// <summary>从 patch hunks 归纳增删行数（按行 Kind 计数，不解析前缀字符）。</summary>
    public static DiffStats From(IReadOnlyList<DiffHunk> hunks)
    {
        var added = 0;
        var removed = 0;
        foreach (var line in hunks.SelectMany(static hunk => hunk.Lines))
        {
            switch (line.Kind)
            {
                case DiffLineKind.Added:
                    added++;
                    break;
                case DiffLineKind.Removed:
                    removed++;
                    break;
                default:
                    break;
            }
        }

        return new DiffStats(added, removed);
    }

    /// <summary>纯文本形态（transcript 投影 / 搜索匹配 — 与分段渲染同文案）。</summary>
    public string ToText() => string.Concat(Segments().Select(static segment => segment.Text));

    /// <summary>分段（数字段 bold — 基准 `<Text bold={true}>{n}</Text>`）。</summary>
    public IEnumerable<(string Text, bool Bold)> Segments()
    {
        if (Added == 0 && Removed == 0)
        {
            yield break;
        }

        if (Added > 0)
        {
            yield return ("Added ", false);
            yield return (Added.ToString(System.Globalization.CultureInfo.InvariantCulture), true);
            yield return (Added > 1 ? " lines" : " line", false);
        }

        if (Removed > 0)
        {
            yield return (Added > 0 ? ", removed " : "Removed ", false);
            yield return (Removed.ToString(System.Globalization.CultureInfo.InvariantCulture), true);
            yield return (Removed > 1 ? " lines" : " line", false);
        }
    }
}
