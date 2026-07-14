using JoinCode.Abstractions.Attributes;

namespace Core.Prompts;

[Register]
public sealed partial class FileContextTracker
{
    private volatile string[] _currentFilePaths = [];
    private volatile string _currentUserMessage = string.Empty;

    public IReadOnlyList<string> CurrentFilePaths => _currentFilePaths;
    public string CurrentUserMessage => _currentUserMessage;

    public void UpdateFilePaths(string[] paths)
    {
        // P2-3: 防御性复制 — 避免调用方修改传入数组导致竞态
        _currentFilePaths = paths is null ? [] : (string[])paths.Clone();
    }

    public void UpdateUserMessage(string message)
    {
        _currentUserMessage = message ?? string.Empty;
    }

    public void Clear()
    {
        _currentFilePaths = [];
        _currentUserMessage = string.Empty;
    }
}
