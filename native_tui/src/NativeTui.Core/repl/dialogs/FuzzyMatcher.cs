namespace NativeTui.Core.Repl;

/// <summary>
/// 模糊匹配打分器 — QuickOpen 文件快速打开域的纯函数打分引擎（全部自研，禁引第三方库）：
/// 子序列语义（query 每字符按序出现于 path，否则 0 分）+ 连续段加分（相邻匹配对奖励）
/// + 词首加分（路径分隔符/点/下划线/连字符/空格后首位或驼峰大写首）+ 文件名区加分
///（basename 段内每匹配字符）+ 短路径加分（路径越短越靠前）。大小写不敏感（ASCII 折叠比较；
/// 中文无大小写原样比较）。排序消费方（QuickOpenDialog.RefreshResults）：
/// 分降序 → 路径短优先 → 字母序。
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>不匹配哨兵分（子序列不满足或空路径非空查询）。</summary>
    public const int ScoreNoMatch = 0;

    /// <summary>空查询基础分（全匹配最低分 — 排序退化为路径短优先 + 字母序）。</summary>
    public const int EmptyQueryScore = 1;

    /// <summary>每个匹配字符的基础分。</summary>
    private const int BasePerMatchedChar = 16;

    /// <summary>连续接续奖励（每对相邻匹配位置）。</summary>
    private const int ConsecutiveBonus = 12;

    /// <summary>词首匹配奖励（分隔符/点/下划线/连字符/空格后首位或驼峰大写首）。</summary>
    private const int WordStartBonus = 28;

    /// <summary>文件名区匹配奖励（basename 段内每匹配字符）。</summary>
    private const int BasenameBonus = 8;

    /// <summary>短路径奖励上限（加 max(0, cap - path.Length) — 路径越短加分越高，长路径归零）。</summary>
    private const int ShortPathBonusCap = 96;

    /// <summary>打分 — query 与 path 的模糊匹配分（0 = 不匹配；空查询恒 EmptyQueryScore）。</summary>
    /// <param name="query">查询（用户输入的模糊串）。</param>
    /// <param name="path">候选路径（工作区相对路径，/ 分隔跨平台统一形态）。</param>
    /// <returns>匹配分（越高越相关；0 = 不匹配）。</returns>
    public static int Match(string query, string path)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(path);
        if (query.Length == 0)
        {
            return EmptyQueryScore;
        }

        var positions = new int[query.Length];
        return TryCollectPositions(query, path, positions)
            ? ScorePositions(path, positions)
            : ScoreNoMatch;
    }

    /// <summary>贪心子序列收集 — query 每字符在 path 中取最早出现位置（大小写折叠比较）。</summary>
    /// <param name="query">查询串。</param>
    /// <param name="path">候选路径。</param>
    /// <param name="positions">输出：每 query 字符在 path 中的匹配位置（长度 = query 长度）。</param>
    /// <returns>子序列满足 true；任一字符找不到 false。</returns>
    private static bool TryCollectPositions(string query, string path, int[] positions)
    {
        var pathIndex = 0;
        for (var i = 0; i < query.Length; i++)
        {
            pathIndex = IndexOfFolded(path, query[i], pathIndex);
            if (pathIndex < 0)
            {
                return false;
            }

            positions[i] = pathIndex++;
        }

        return true;
    }

    /// <summary>大小写折叠查找（char.ToLowerInvariant — 中文无大小写原样相等）。</summary>
    /// <returns>目标字符位置；不存在返回 -1。</returns>
    private static int IndexOfFolded(string path, char target, int start)
    {
        var folded = char.ToLowerInvariant(target);
        for (var i = start; i < path.Length; i++)
        {
            if (char.ToLowerInvariant(path[i]) == folded)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>按匹配位置打分 — 基础分 + 连续段 + 词首 + 文件名区 + 短路径。</summary>
    /// <param name="path">候选路径（驼峰词首判定需原文大小写）。</param>
    /// <param name="positions">每 query 字符的匹配位置。</param>
    /// <returns>总分（恒 &gt; 0 — 调用方已保证子序列成立）。</returns>
    private static int ScorePositions(string path, int[] positions)
    {
        var basenameStart = BasenameStart(path);
        return positions.Length * BasePerMatchedChar
            + ConsecutiveTotal(positions) * ConsecutiveBonus
            + CountWordStarts(path, positions) * WordStartBonus
            + CountBasenameMatches(positions, basenameStart) * BasenameBonus
            + Math.Max(0, ShortPathBonusCap - path.Length);
    }

    /// <summary>连续接续对数（相邻匹配位置差 1）。</summary>
    private static int ConsecutiveTotal(int[] positions)
    {
        var total = 0;
        for (var i = 1; i < positions.Length; i++)
        {
            if (positions[i] == positions[i - 1] + 1)
            {
                total++;
            }
        }

        return total;
    }

    /// <summary>词首匹配数（位置 0 / 分隔符·点·下划线·连字符·空格后 / 驼峰小→大）。</summary>
    private static int CountWordStarts(string path, int[] positions) =>
        positions.Count(index => IsWordStart(path, index));

    /// <summary>文件名区匹配数（位置 ≥ basename 起点 — 文件名命中优先于路径中段）。</summary>
    private static int CountBasenameMatches(int[] positions, int basenameStart) =>
        positions.Count(index => index >= basenameStart);

    /// <summary>basename 起点（最后一个路径分隔符后一位；无分隔符 = 0 整串即文件名）。</summary>
    private static int BasenameStart(string path) =>
        Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1;

    /// <summary>词首判定 — 位置 0，或前位是 / \ . _ - 空格，或前小写现大写（驼峰边界）。</summary>
    private static bool IsWordStart(string path, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var previous = path[index - 1];
        return previous is '/' or '\\' or '.' or '_' or '-' or ' '
            || (char.IsLower(previous) && char.IsUpper(path[index]));
    }
}
