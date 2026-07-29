namespace JoinCode.Abstractions.State;

/// <summary>
/// AppState 与 Document 之间的转换器
/// </summary>
public static class AppStateConverter
{
    public static AppStateDocument ToDocument(AppState state, DateTime? savedAt = null)
    {
        return new AppStateDocument
        {
            Id = "current",
            Session = new SessionStateDocument
            {
                SessionId = state.Session.SessionId,
                SystemPrompt = state.Session.SystemPrompt,
                MessageList = state.Session.MessageList.Select(m => new ApiMessageDocument
                {
                    Role = m.Role,
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    Metadata = m.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                }).ToList(),
                StartedAt = state.Session.StartedAt,
                LastActivityAt = state.Session.LastActivityAt,
                CurrentModel = state.Session.CurrentModel,
                IsPlanMode = state.Session.IsPlanMode,
                CurrentPlan = state.Session.CurrentPlan
            },
            Agents = state.Agents.ToDictionary(
                kvp => kvp.Key,
                kvp => new AgentStateDocument
                {
                    AgentId = kvp.Value.AgentId,
                    Name = kvp.Value.Name,
                    AgentType = kvp.Value.AgentType,
                    Status = kvp.Value.Status,
                    WorkingDirectory = kvp.Value.WorkingDirectory,
                    CurrentTaskId = kvp.Value.CurrentTaskId,
                    Metadata = kvp.Value.Metadata.ToDictionary(m => m.Key, m => m.Value),
                    LastActivityAt = kvp.Value.LastActivityAt
                }),
            Tasks = state.Tasks.ToDictionary(
                kvp => kvp.Key,
                kvp => new TaskStateDocument
                {
                    TaskId = kvp.Value.TaskId,
                    Name = kvp.Value.Name,
                    Description = kvp.Value.Description,
                    Status = kvp.Value.Status,
                    AgentId = kvp.Value.AgentId,
                    ParentTaskId = kvp.Value.ParentTaskId,
                    SubTaskIds = kvp.Value.SubTaskIds.ToList(),
                    Progress = kvp.Value.Progress,
                    Result = kvp.Value.Result,
                    Error = kvp.Value.Error,
                    CreatedAt = kvp.Value.CreatedAt,
                    StartedAt = kvp.Value.StartedAt,
                    CompletedAt = kvp.Value.CompletedAt,
                    Metadata = kvp.Value.Metadata.ToDictionary(m => m.Key, m => m.Value)
                }),
            Config = new ConfigStateDocument
            {
                Verbose = state.Config.Verbose,
                IsBriefMode = state.Config.IsBriefMode,
                Theme = state.Config.Theme,
                AutoConfirm = state.Config.AutoConfirm,
                MaxTokenBudget = state.Config.MaxTokenBudget,
                UsedTokens = state.Config.UsedTokens,
                Settings = state.Config.Settings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            },
            SavedAt = savedAt ?? DateTime.UtcNow,
            Version = 1
        };
    }

    public static AppState FromDocument(AppStateDocument doc)
    {
        return new AppState
        {
            Session = new SessionState
            {
                SessionId = doc.Session.SessionId,
                SystemPrompt = doc.Session.SystemPrompt,
                MessageList = doc.Session.MessageList
                    .Select(m => new ApiMessageState
                    {
                        Role = m.Role,
                        Content = m.Content,
                        Timestamp = m.Timestamp,
                        Metadata = m.Metadata?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty
                    })
                    .ToImmutableList(),
                StartedAt = doc.Session.StartedAt,
                LastActivityAt = doc.Session.LastActivityAt,
                CurrentModel = doc.Session.CurrentModel,
                IsPlanMode = doc.Session.IsPlanMode,
                CurrentPlan = doc.Session.CurrentPlan
            },
            Agents = doc.Agents?.ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => new AgentState
                {
                    AgentId = kvp.Value.AgentId,
                    Name = kvp.Value.Name,
                    AgentType = kvp.Value.AgentType,
                    Status = kvp.Value.Status,
                    WorkingDirectory = kvp.Value.WorkingDirectory,
                    CurrentTaskId = kvp.Value.CurrentTaskId,
                    Metadata = kvp.Value.Metadata?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty,
                    LastActivityAt = kvp.Value.LastActivityAt
                }) ?? ImmutableDictionary<string, AgentState>.Empty,
            Tasks = doc.Tasks?.ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => new TaskState
                {
                    TaskId = kvp.Value.TaskId,
                    Name = kvp.Value.Name,
                    Description = kvp.Value.Description,
                    Status = kvp.Value.Status,
                    AgentId = kvp.Value.AgentId,
                    ParentTaskId = kvp.Value.ParentTaskId,
                    SubTaskIds = kvp.Value.SubTaskIds?.ToImmutableList() ?? ImmutableList<string>.Empty,
                    Progress = kvp.Value.Progress,
                    Result = kvp.Value.Result,
                    Error = kvp.Value.Error,
                    CreatedAt = kvp.Value.CreatedAt,
                    StartedAt = kvp.Value.StartedAt,
                    CompletedAt = kvp.Value.CompletedAt,
                    Metadata = kvp.Value.Metadata?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty
                }) ?? ImmutableDictionary<string, TaskState>.Empty,
            Config = new ConfigState
            {
                Verbose = doc.Config.Verbose,
                IsBriefMode = doc.Config.IsBriefMode,
                Theme = doc.Config.Theme,
                AutoConfirm = doc.Config.AutoConfirm,
                MaxTokenBudget = doc.Config.MaxTokenBudget,
                UsedTokens = doc.Config.UsedTokens,
                Settings = doc.Config.Settings?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty
            },
            Ui = new UiState(),
            Mcp = new McpState(),
            Bridge = new BridgeState(),
            Permission = new PermissionState()
        };
    }
}
