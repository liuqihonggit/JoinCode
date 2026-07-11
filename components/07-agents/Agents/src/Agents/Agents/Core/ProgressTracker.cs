namespace Core.Agents;

public sealed class ProgressTracker : JoinCode.Abstractions.Interfaces.IProgressTracker
{
    private readonly IClockService? _clock;
    private readonly List<JoinCode.Abstractions.Interfaces.ToolActivity> _recentActivities = new(5);
    private int _toolUseCount;
    private int _tokenCount;
    private string? _summary;
    private volatile bool _notified;

    public ProgressTracker(IClockService? clock = null)
    {
        _clock = clock;
    }

    public int ToolUseCount => _toolUseCount;
    public int TokenCount => _tokenCount;
    public string? Summary => _summary;
    public bool Notified => _notified;

    public void RecordToolUse(string toolName, string? activityDescription = null, Dictionary<string, string>? input = null)
    {
        Interlocked.Increment(ref _toolUseCount);

        var activity = new JoinCode.Abstractions.Interfaces.ToolActivity
        {
            ToolName = toolName,
            ActivityDescription = activityDescription,
            IsSearch = toolName.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0,
            IsRead = toolName.IndexOf("read", StringComparison.OrdinalIgnoreCase) >= 0,
            Input = input,
            Timestamp = _clock?.GetUtcNow() ?? DateTime.UtcNow
        };

        lock (_recentActivities)
        {
            if (_recentActivities.Count >= 5)
                _recentActivities.RemoveAt(0);
            _recentActivities.Add(activity);
        }
    }

    public void RecordTokenUsage(int tokenCount)
    {
        Interlocked.Add(ref _tokenCount, tokenCount);
    }

    public void UpdateSummary(string summary)
    {
        _summary = summary;
    }

    public bool MarkNotified()
    {
        return Interlocked.CompareExchange(ref _notified, true, false) == false;
    }

    public JoinCode.Abstractions.Interfaces.AgentProgress ToProgress()
    {
        JoinCode.Abstractions.Interfaces.ToolActivity? lastActivity;
        IReadOnlyList<JoinCode.Abstractions.Interfaces.ToolActivity>? recentActivities;
        lock (_recentActivities)
        {
            lastActivity = _recentActivities.Count > 0
                ? _recentActivities[^1]
                : null;
            recentActivities = _recentActivities.ToList();
        }

        return new JoinCode.Abstractions.Interfaces.AgentProgress
        {
            ToolUseCount = _toolUseCount,
            TokenCount = _tokenCount,
            LastActivity = lastActivity,
            RecentActivities = recentActivities,
            Summary = _summary
        };
    }
}
