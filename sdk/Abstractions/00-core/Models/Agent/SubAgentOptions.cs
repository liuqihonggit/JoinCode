namespace JoinCode.Abstractions.Models.Agent;

public sealed class SubAgentOptions
{
    public string? AgentType { get; init; }
    public string? AdditionalInstructions { get; init; }
    public int MaxIterations { get; init; } = 50;
    public bool EnableThinking { get; init; } = false;
    public string? ModelName { get; init; }
    public float Temperature { get; init; } = 0.7f;
    public string? DisplayName { get; init; }
    public string? ColorHex { get; init; }
    public string? SpinnerVerb { get; init; }
    public string? SystemPrompt { get; init; }
    public List<string>? AllowedTools { get; init; }
    public List<string>? DeniedTools { get; init; }
    public MessageList? InitialMessageList { get; init; }
    public List<string>? PreloadSkills { get; init; }
    public string? PermissionMode { get; init; }
    public string? WorktreePath { get; set; }
    public string? WorktreeBranch { get; set; }
    public string? SubagentName { get; init; }
    public bool IsBuiltIn { get; init; }
    public JoinCode.Abstractions.LLM.Chat.CacheSafeParams? CacheSafeParams { get; init; }
    public JoinCode.Abstractions.Interfaces.IProgressTracker? ProgressTracker { get; init; }
    public JoinCode.Abstractions.LLM.Chat.ContentReplacementState? ContentReplacementState { get; init; }
    public string? SessionId { get; init; }
    public JoinCode.Abstractions.Interfaces.IFileStateCache? ReadFileState { get; init; }
    public string? Effort { get; init; }
}
