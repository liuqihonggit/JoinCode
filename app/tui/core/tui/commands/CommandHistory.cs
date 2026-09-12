namespace JoinCode.Tui.Commands;

/// <summary>
/// 命令历史导航器 — 维护最近输入的命令列表，支持上下箭头导航。
/// 最多保留 20 条命令，连续重复命令不记录。
/// </summary>
public sealed class CommandHistory
{
    private const int MaxCapacity = 20;
    private readonly List<string> _commands = new(MaxCapacity + 1);
    private int _cursor = -1;

    /// <summary>全部历史命令（从旧到新）。</summary>
    public IReadOnlyList<string> AllCommands => _commands;

    /// <summary>
    /// 添加一条命令到历史。连续重复命令不记录。添加后重置导航游标。
    /// </summary>
    public void Add(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        if (_commands.Count > 0 && _commands[^1] == command) return;
        if (_commands.Count >= MaxCapacity)
            _commands.RemoveAt(0);
        _commands.Add(command);
        _cursor = -1;
    }

    /// <summary>
    /// 向上导航（更早的命令）。返回 null 表示已到顶部或无历史。
    /// </summary>
    public string? NavigateUp()
    {
        if (_commands.Count == 0) return null;
        if (_cursor < 0)
            _cursor = _commands.Count - 1;
        else if (_cursor > 0)
            _cursor--;
        return _commands[_cursor];
    }

    /// <summary>
    /// 向下导航（更新的命令）。返回 null 表示已到底部。
    /// </summary>
    public string? NavigateDown()
    {
        if (_commands.Count == 0 || _cursor < 0) return null;
        if (_cursor < _commands.Count - 1)
        {
            _cursor++;
            return _commands[_cursor];
        }
        _cursor = -1;
        return null;
    }
}
