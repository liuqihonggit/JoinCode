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
        _currentFilePaths = paths ?? [];
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
