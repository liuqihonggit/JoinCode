namespace JoinCode.Tui.Rendering;

/// <summary>
/// 子代理卡片展开/折叠管理器 — 跟踪展开状态，最多同时展开 3 个。
/// 展开第 4 个时自动折叠最早展开的。线程安全。
/// </summary>
public sealed class SubAgentCardManager
{
    private readonly object _lock = new();
    private readonly LinkedList<string> _expandedOrder = new();
    private readonly HashSet<string> _expandedSet = new(StringComparer.Ordinal);
    private const int MaxExpanded = 3;

    /// <summary>当前展开的子代理 ID 列表（按展开时间排序）。</summary>
    public IReadOnlyList<string> Expanded => [.. _expandedOrder];

    /// <summary>已展开数量。</summary>
    public int ExpandedCount => _expandedOrder.Count;

    /// <summary>指定子代理是否已展开。</summary>
    public bool IsExpanded(string agentId)
    {
        lock (_lock)
        {
            return _expandedSet.Contains(agentId);
        }
    }

    /// <summary>展开子代理。超过最大数量时自动折叠最早展开的。</summary>
    /// <returns>被自动折叠的子代理 ID（null 表示没有折叠）。</returns>
    public string? Expand(string agentId)
    {
        lock (_lock)
        {
            if (_expandedSet.Contains(agentId)) return null;

            string? evicted = null;
            if (_expandedOrder.Count >= MaxExpanded)
            {
                evicted = _expandedOrder.First!.Value;
                _expandedOrder.RemoveFirst();
                _expandedSet.Remove(evicted);
            }

            _expandedOrder.AddLast(agentId);
            _expandedSet.Add(agentId);
            return evicted;
        }
    }

    /// <summary>折叠子代理。</summary>
    /// <returns>是否成功折叠（false 表示原本未展开）。</returns>
    public bool Collapse(string agentId)
    {
        lock (_lock)
        {
            if (!_expandedSet.Contains(agentId)) return false;
            _expandedSet.Remove(agentId);
            _expandedOrder.Remove(agentId);
            return true;
        }
    }

    /// <summary>切换展开/折叠状态。</summary>
    /// <returns>被自动折叠的子代理 ID（null 表示没有折叠或操作是折叠）。</returns>
    public string? Toggle(string agentId)
    {
        lock (_lock)
        {
            if (_expandedSet.Contains(agentId))
            {
                Collapse(agentId);
                return null;
            }
            return Expand(agentId);
        }
    }

    /// <summary>折叠所有子代理。</summary>
    public void CollapseAll()
    {
        lock (_lock)
        {
            _expandedOrder.Clear();
            _expandedSet.Clear();
        }
    }
}
