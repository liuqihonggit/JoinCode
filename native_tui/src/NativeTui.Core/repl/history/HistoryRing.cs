namespace NativeTui.Core.Repl;

/// <summary>历史条目（DSG045 — 基准 HistoryEntry 携带语义）：折叠形态文本 + 粘贴登记快照
///（导航恢复时登记随条目回放 — 历史往返后提交展开回原文，null = 无粘贴条目）。</summary>
public sealed record HistoryEntry(string Text, IReadOnlyDictionary<int, string>? PastedContents);

/// <summary>
/// 输入历史环 — 对齐基准 useArrowKeyHistory 导航状态机 + history.ts 容量语义：
/// index 0 = 输入框态（非历史）；Up 翻旧（首次离开 0 暂存草稿、到底保持不动不回绕）；
/// Down 翻新（回到 0 恢复草稿，无草稿返回空串）；提交追加（末条去重）；
/// 容量 MAX_HISTORY_ITEMS=100 淘汰最旧。内部按提交顺序（旧→新）存储，导航从末尾倒数。
/// </summary>
public sealed class HistoryRing
{
    /// <summary>历史容量上限（基准 history.ts MAX_HISTORY_ITEMS）。</summary>
    public const int Capacity = 100;

    private readonly List<HistoryEntry> _entries = [];

    /// <summary>当前导航 index（0 = 输入框态；1..Count = 历史深度，越大越旧）。</summary>
    public int Index { get; private set; }

    /// <summary>翻历史前暂存的输入草稿（首次离开 index 0 时捕获；null = 未捕获）。</summary>
    public string? Draft { get; private set; }

    /// <summary>历史条数。</summary>
    public int Count => _entries.Count;

    /// <summary>历史条目快照（旧→新；测试断言面）。</summary>
    internal IReadOnlyList<HistoryEntry> Entries => _entries;

    /// <summary>历史条目文本投影（旧→新；string 断言兼容面）。</summary>
    internal IReadOnlyList<string> EntryTexts => _entries.Select(static entry => entry.Text).ToList();

    /// <summary>提交入史（无粘贴兼容面）：空文本忽略；末条相同去重；超容量淘汰最旧。</summary>
    public void Add(string text) => Add(text, null);

    /// <summary>提交入史（DSG045 — 折叠文本 + 粘贴登记快照随条目入史）：
    /// 空文本忽略；末条文本相同去重（登记不参与去重键 — 文本相同视为同条目）；超容量淘汰最旧。</summary>
    public void Add(string text, IReadOnlyDictionary<int, string>? pastedContents)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (_entries.Count > 0 && _entries[^1].Text == text)
        {
            return;
        }

        _entries.Add(new HistoryEntry(text, pastedContents));
        if (_entries.Count > Capacity)
        {
            _entries.RemoveAt(0);
        }
    }

    /// <summary>宿主注入历史种子（旧→新顺序）— 启动时从 transcript 加载。</summary>
    public void Seed(IEnumerable<string> entries)
    {
        foreach (var entry in entries)
        {
            Add(entry);
        }
    }

    /// <summary>撤销末条历史（M8-2 打断恢复 — 基准 removeLastFromHistory）：
    /// 仅当末条与给定文本一致才移除（防误撤他人条目 — 弹回输入与历史撤销原子配套）。</summary>
    public void RemoveLast(string text)
    {
        if (_entries.Count > 0 && _entries[^1].Text == text)
        {
            _entries.RemoveAt(_entries.Count - 1);
            if (Index > _entries.Count)
            {
                Index = _entries.Count;
            }
        }
    }

    /// <summary>翻上一条（旧方向）：index 递增，返回目标历史条目（文本 + 粘贴登记）；
    /// 首次离开输入框以 draft 暂存当前输入（缺省空串）；越过最旧一条返回 false（index 保持，不回绕）。</summary>
    public bool TryUp(out HistoryEntry entry, string? draft = null)
    {
        var target = Index + 1;
        if (target > _entries.Count)
        {
            entry = new HistoryEntry(string.Empty, null);
            return false;
        }

        if (Index == 0)
        {
            Draft = draft ?? string.Empty;
        }

        Index = target;
        entry = _entries[^target];
        return true;
    }

    /// <summary>翻下一条（新方向）：index 递减，返回目标历史条目（文本 + 粘贴登记；
    /// 回到输入框恢复草稿 — 草稿为纯文本无登记）；已在输入框返回 false。</summary>
    public bool TryDown(out HistoryEntry entry)
    {
        if (Index == 0)
        {
            entry = new HistoryEntry(string.Empty, null);
            return false;
        }

        Index--;
        if (Index == 0)
        {
            entry = new HistoryEntry(Draft ?? string.Empty, null);
            return true;
        }

        entry = _entries[^Index];
        return true;
    }

    /// <summary>重置导航态：回输入框 + 清草稿（提交/清空输入后调用）。</summary>
    public void Reset()
    {
        Index = 0;
        Draft = null;
    }
}
