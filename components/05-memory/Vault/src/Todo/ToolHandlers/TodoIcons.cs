
namespace Services.Todo.ToolHandlers;

/// <summary>
/// Todo/Task 共享图标常量 — 消除 TodoToolHandlers 和 TaskToolHandlers 中的重复定义
/// </summary>
internal static class TodoIcons
{
    public static readonly FrozenDictionary<string, string> PriorityIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [TodoPriorityConstants.High] = PrioritySymbolConstants.Critical,
        [TodoPriorityConstants.Medium] = PrioritySymbolConstants.Medium,
        [TodoPriorityConstants.Low] = PrioritySymbolConstants.Low
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, string> TodoStatusIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [TodoStatusConstants.Completed] = StatusSymbolConstants.Tick,
        [TodoStatusConstants.InProgress] = StatusSymbolConstants.Refresh,
        [TodoStatusConstants.Pending] = StatusSymbolConstants.Circle
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, string> TaskStatusIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [TaskStatusConstants.Completed] = StatusSymbolConstants.Tick,
        [TaskStatusConstants.InProgress] = StatusSymbolConstants.Refresh,
        [TaskStatusConstants.Pending] = StatusSymbolConstants.Circle,
        [TaskStatusConstants.Stopped] = StatusSymbolConstants.Stop
    }.ToFrozenDictionary();

    public static readonly FrozenSet<string> ValidPriorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TodoPriorityConstants.High,
        TodoPriorityConstants.Medium,
        TodoPriorityConstants.Low
    }.ToFrozenSet();

    public static readonly FrozenSet<string> ValidTodoStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TodoStatusConstants.Pending,
        TodoStatusConstants.InProgress,
        TodoStatusConstants.Completed
    }.ToFrozenSet();
}
