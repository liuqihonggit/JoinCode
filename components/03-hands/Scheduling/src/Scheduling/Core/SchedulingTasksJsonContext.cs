namespace Core.Scheduling;

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
public partial class SchedulingTasksJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TaskAssignmentPlan))]
[JsonSerializable(typeof(TaskAgentAssignment))]
[JsonSerializable(typeof(ExecutionPhase))]
public partial class SchedulingIndentedTasksJsonContext : JsonSerializerContext;
