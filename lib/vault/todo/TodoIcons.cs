
namespace Services.Todo.ToolHandlers;

/// <summary>
/// Todo/Task 共享图标常量 — 消除 TodoToolHandlers 和 TaskToolHandlers 中的重复定义
/// </summary>
internal static class TodoIcons
{
    public static readonly FrozenDictionary<string, string> PriorityIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [TodoPriorityEnumConstants.High] = PrioritySymbolEnumConstants.Critical,
        [TodoPriorityEnumConstants.Medium] = PrioritySymbolEnumConstants.Medium,
        [TodoPriorityEnumConstants.Low] = PrioritySymbolEnumConstants.Low
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, string> TodoStatusIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [TodoStatusEnumConstants.Completed] = StatusSymbolEnumConstants.Tick,
        [TodoStatusEnumConstants.InProgress] = StatusSymbolEnumConstants.Refresh,
        [TodoStatusEnumConstants.Pending] = StatusSymbolEnumConstants.Circle
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, string> TaskStatusIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [TaskExecutionStatusEnumConstants.Completed] = StatusSymbolEnumConstants.Tick,
        [TaskExecutionStatusEnumConstants.Running] = StatusSymbolEnumConstants.Refresh,
        [TaskExecutionStatusEnumConstants.Pending] = StatusSymbolEnumConstants.Circle,
        [TaskExecutionStatusEnumConstants.Stopped] = StatusSymbolEnumConstants.Stop
    }.ToFrozenDictionary();

    public static readonly FrozenSet<string> ValidPriorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TodoPriorityEnumConstants.High,
        TodoPriorityEnumConstants.Medium,
        TodoPriorityEnumConstants.Low
    }.ToFrozenSet();

    public static readonly FrozenSet<string> ValidTodoStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TodoStatusEnumConstants.Pending,
        TodoStatusEnumConstants.InProgress,
        TodoStatusEnumConstants.Completed
    }.ToFrozenSet();
}
