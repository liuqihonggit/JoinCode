using JoinCode.Abstractions.Attributes;

namespace Core.Prompts;

[Register]
public sealed partial class FileContextTracker
{
    private volatile FrozenSet<string> _currentFilePaths = FrozenSet<string>.Empty;
    private volatile string _currentUserMessage = string.Empty;

    public IReadOnlySet<string> CurrentFilePaths => _currentFilePaths;
    public string CurrentUserMessage => _currentUserMessage;

    public void UpdateFilePaths(string[] paths)
    {
        _currentFilePaths = paths is null ? FrozenSet<string>.Empty : paths.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public void UpdateUserMessage(string message)
    {
        _currentUserMessage = message ?? string.Empty;
    }

    public void Clear()
    {
        _currentFilePaths = FrozenSet<string>.Empty;
        _currentUserMessage = string.Empty;
    }
}
