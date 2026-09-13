namespace Core.Scheduling;

/// <summary>
/// 调度任务 JSON 序列化上下文 — 紧凑格式（不缩进），覆盖 Workflow、Step、RemoteAgent、Teammate、McpMonitor 等类型
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RemoteAgentExecuteRequest))]
[JsonSerializable(typeof(RemoteAgentExecuteResponse))]
[JsonSerializable(typeof(RemoteAgentTaskDefinition))]
[JsonSerializable(typeof(WorkflowDefinition))]
[JsonSerializable(typeof(WorkflowStep))]
[JsonSerializable(typeof(List<WorkflowStep>))]
[JsonSerializable(typeof(WorkflowResult))]
[JsonSerializable(typeof(WorkflowStatus))]
[JsonSerializable(typeof(StepStatus))]
[JsonSerializable(typeof(McpMonitorConfig))]
[JsonSerializable(typeof(McpMonitorStatus))]
[JsonSerializable(typeof(McpMonitorEventArgs))]
[JsonSerializable(typeof(LocalShellTaskDefinition))]
[JsonSerializable(typeof(InProcessTeammateDefinition))]
[JsonSerializable(typeof(List<RuntimeTask>))]
[JsonSerializable(typeof(WorkflowSnapshot))]
public partial class SchedulingTasksJsonContext : JsonSerializerContext;

/// <summary>
/// 调度任务 JSON 序列化上下文 — 缩进格式，覆盖任务分配计划相关类型
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TaskAssignmentPlan))]
[JsonSerializable(typeof(TaskAgentAssignment))]
[JsonSerializable(typeof(ExecutionPhase))]
public partial class SchedulingIndentedTasksJsonContext : JsonSerializerContext;
