namespace NativeTui.Core.Repl;

/// <summary>
/// Kill ring — 对齐基准 utils/Cursor.ts 的模块级全局剪切环（跨输入域共享）：
/// 容量 10，连续 kill 累积（prepend 前插 / append 后接），Ctrl+Y yank 头条，
/// Alt+Y yank-pop 环回替换上次 yank 区域。非 kill/yank 操作分别打断对应链。
/// </summary>
public static class KillRing
{
    /// <summary>环容量（基准 KILL_RING_MAX_SIZE）。</summary>
    private const int MaxSize = 10;

    private static readonly List<string> s_ring = [];
    private static int s_ringIndex;
    private static bool s_lastWasKill;
    private static int s_lastYankStart;
    private static int s_lastYankLength;
    private static bool s_lastWasYank;

    /// <summary>最新剪切（环头）；空环返回空串。</summary>
    public static string Last => s_ring.Count > 0 ? s_ring[0] : string.Empty;

    /// <summary>环内条目数。</summary>
    public static int Size => s_ring.Count;

    /// <summary>压入剪切文本：连续 kill 按 direction 累积，否则 unshift 新条目（超容量淘汰最旧）。</summary>
    public static void Push(string text, bool prepend)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (s_lastWasKill && s_ring.Count > 0)
        {
            s_ring[0] = prepend ? text + s_ring[0] : s_ring[0] + text;
        }
        else
        {
            s_ring.Insert(0, text);
            if (s_ring.Count > MaxSize)
            {
                s_ring.RemoveAt(s_ring.Count - 1);
            }
        }

        s_lastWasKill = true;
        s_lastWasYank = false;
    }

    /// <summary>记录一次 yank（供后续 yank-pop 替换定位）；yank 打断 kill 累积链。</summary>
    public static void RecordYank(int start, int length)
    {
        s_lastYankStart = start;
        s_lastYankLength = length;
        s_lastWasYank = true;
        s_ringIndex = 0;
        s_lastWasKill = false;
    }

    /// <summary>尝试 yank-pop：上次 yank 存在且环多于一条时环回取下一条，返回替换区域。</summary>
    public static bool TryYankPop(out string text, out int start, out int length)
    {
        text = string.Empty;
        start = 0;
        length = 0;
        if (!s_lastWasYank || s_ring.Count <= 1)
        {
            return false;
        }

        s_ringIndex = (s_ringIndex + 1) % s_ring.Count;
        text = s_ring[s_ringIndex];
        start = s_lastYankStart;
        length = s_lastYankLength;
        return true;
    }

    /// <summary>yank-pop 替换后更新长度（下次 pop 定位）。</summary>
    public static void UpdateYankLength(int length) => s_lastYankLength = length;

    /// <summary>打断 kill 累积链（非 kill 按键时调用）。</summary>
    public static void ResetKillAccumulation() => s_lastWasKill = false;

    /// <summary>打断 yank 链（非 yank 按键时调用）。</summary>
    public static void ResetYankState() => s_lastWasYank = false;

    /// <summary>清空全部状态（测试隔离用）。</summary>
    public static void Clear()
    {
        s_ring.Clear();
        s_ringIndex = 0;
        s_lastWasKill = false;
        s_lastWasYank = false;
        s_lastYankStart = 0;
        s_lastYankLength = 0;
    }
}
