namespace NativeTui.Core.Repl;

/// <summary>
/// transcript 搜索状态 — 对齐基准 REPL.tsx 4271-4288 + TranscriptSearchBar（368-439）：
/// '/' 打开增量搜索栏（查询保留预填 — less "shows last pattern" 语义，可编辑续搜）；
/// 可打印字符增量查询、每键重扫；Return 提交关闭栏（查询与匹配保留 — n/N 继续工作，
/// less 语义）；Esc/ctrl+c/ctrl+g 撤销到 pre-/ 状态（查询清空）；n/N 在匹配序列上
/// 循环导航（cur/count 徽章数据）。匹配计算对齐基准 VML setSearchQuery
/// （toLowerCase + indexOf）= OrdinalIgnoreCase 子串、同行不重叠多命中。
/// 行源经构造注入委托（指向冻结快照行），保持纯逻辑可测、与渲染解耦。
/// </summary>
public sealed class TranscriptSearchState(Func<IReadOnlyList<string>> linesProvider)
{
    /// <summary>搜索栏是否打开（栏内 readline 编辑；关闭后 n/N 仍可用 — 查询保留）。</summary>
    public bool BarOpen { get; private set; }

    /// <summary>当前查询（栏开关均保留 — n/N 依赖；Cancel/Reset 清空）。</summary>
    public string Query { get; private set; } = string.Empty;

    /// <summary>匹配区间列表（行序 + 列序，供渲染高亮消费；查询空 = 空列表）。</summary>
    public IReadOnlyList<TranscriptMatch> Matches { get; private set; } = [];

    /// <summary>当前匹配索引（Matches 内 0-based）。</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>匹配总数。</summary>
    public int Count => Matches.Count;

    /// <summary>当前匹配（无匹配为 null）。</summary>
    public TranscriptMatch? Current => CurrentIndex < Matches.Count ? Matches[CurrentIndex] : null;

    /// <summary>徽章当前序号（1-based；无匹配 0 — 渲染 `N/M` 右对齐数据源）。</summary>
    public int CurrentOrdinal => Count > 0 ? CurrentIndex + 1 : 0;

    /// <summary>打开搜索栏 — 查询保留预填（上次 pattern 可编辑续搜 — 基准 initialQuery 种子语义）。</summary>
    public void OpenBar()
    {
        BarOpen = true;
    }

    /// <summary>增量输入 — 追加查询并重扫（每键重扫 — 基准增量语义）；重置到第一个匹配。</summary>
    public void Type(string text)
    {
        Query += text;
        Rescan();
    }

    /// <summary>退格 — 删除末字符并重扫（查询变空 = 无匹配；空查询退格无害）。</summary>
    public void Backspace()
    {
        if (Query.Length > 0)
        {
            Query = Query[..^1];
        }

        Rescan();
    }

    /// <summary>提交（Return）— 关闭栏；查询与匹配保留（n/N 继续跳转 — less 语义）。</summary>
    public void CommitBar()
    {
        BarOpen = false;
    }

    /// <summary>撤销（Esc / ctrl+c / ctrl+g）— 关闭栏并清空查询与匹配（回到 pre-/ 状态；滚动回锚由控制器执行）。</summary>
    public void Cancel()
    {
        BarOpen = false;
        Clear();
    }

    /// <summary>重置（退出 transcript）— 全清。匹配绑定冻结行号，快照丢弃后必须清空防止行号错位。</summary>
    public void Reset()
    {
        BarOpen = false;
        Clear();
    }

    /// <summary>下一匹配（循环 — 越过末个回绕首个）；无匹配不动返回 false。</summary>
    public bool Next()
    {
        if (Count == 0)
        {
            return false;
        }

        CurrentIndex = (CurrentIndex + 1) % Count;
        return true;
    }

    /// <summary>上一匹配（循环 — 越过首个回绕末个）；无匹配不动返回 false。</summary>
    public bool Prev()
    {
        if (Count == 0)
        {
            return false;
        }

        CurrentIndex = (CurrentIndex - 1 + Count) % Count;
        return true;
    }

    /// <summary>定位最近匹配 — 行号 &gt;= anchorLine 的第一个，否则回绕首个（基准 nearest-ptr：
    /// 搜索输入预览从当前视口就近跳转）；无匹配返回 false 不动。</summary>
    public bool FocusNearest(int anchorLine)
    {
        if (Count == 0)
        {
            return false;
        }

        for (var i = 0; i < Matches.Count; i++)
        {
            if (Matches[i].LineIndex >= anchorLine)
            {
                CurrentIndex = i;
                return true;
            }
        }

        CurrentIndex = 0;
        return true;
    }

    /// <summary>重扫 — 查询变化后调用（每次读取行源委托，行源指向冻结快照行 — 会话内不变）。</summary>
    private void Rescan()
    {
        Matches = [];
        CurrentIndex = 0;
        if (Query.Length == 0)
        {
            return;
        }

        var lines = linesProvider();
        var matches = new List<TranscriptMatch>();
        for (var line = 0; line < lines.Count; line++)
        {
            var text = lines[line];
            var column = text.IndexOf(Query, StringComparison.OrdinalIgnoreCase);
            while (column >= 0)
            {
                matches.Add(new TranscriptMatch(line, column, Query.Length));
                column = text.IndexOf(Query, column + Query.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        Matches = matches;
    }

    /// <summary>清空查询与匹配（撤销/重置共用）。</summary>
    private void Clear()
    {
        Query = string.Empty;
        Matches = [];
        CurrentIndex = 0;
    }
}
