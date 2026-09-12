
namespace JoinCode.Dream;

/// <summary>
/// 做梦任务DTO - 用于JSON序列化
/// </summary>
public sealed class DreamTaskDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    [JsonPropertyName("notified")]
    public bool Notified { get; set; }

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    [JsonPropertyName("sessionsReviewing")]
    public int SessionsReviewing { get; set; }

    [JsonPropertyName("filesTouched")]
    public List<string> FilesTouched { get; set; } = new();

    [JsonPropertyName("turns")]
    public List<DreamTurnDto> Turns { get; set; } = new();

    [JsonPropertyName("priorMtime")]
    public long PriorMtime { get; set; }

    /// <summary>
    /// 从状态创建DTO
    /// </summary>
    public static DreamTaskDto FromState(DreamTaskState state)
    {
        return new DreamTaskDto
        {
            Id = state.Id,
            Status = state.Status.ToString(),
            Description = state.Description,
            StartTime = state.StartTime,
            EndTime = state.EndTime,
            Notified = state.Notified,
            Phase = state.Phase.ToString(),
            SessionsReviewing = state.SessionsReviewing,
            FilesTouched = new List<string>(state.FilesTouched),
            Turns = state.Turns.Select(t => new DreamTurnDto
            {
                Text = t.Text,
                ToolUseCount = t.ToolUseCount
            }).ToList(),
            PriorMtime = state.PriorMtime
        };
    }

    /// <summary>
    /// 转换为状态对象
    /// </summary>
    public DreamTaskState ToState()
    {
        var state = new DreamTaskState
        {
            Id = Id,
            Description = Description,
            StartTime = StartTime,
            EndTime = EndTime,
            Notified = Notified,
            SessionsReviewing = SessionsReviewing,
            PriorMtime = PriorMtime
        };

        if (Enum.TryParse<DreamTaskStatus>(Status, out var status))
        {
            state.Status = status;
        }

        if (Enum.TryParse<DreamPhase>(Phase, out var phase))
        {
            state.Phase = phase;
        }

        foreach (var file in FilesTouched)
        {
            state.FilesTouched.Add(file);
        }

        foreach (var turn in Turns)
        {
            state.Turns.Add(new DreamTurn
            {
                Text = turn.Text,
                ToolUseCount = turn.ToolUseCount
            });
        }

        return state;
    }
}

/// <summary>
/// 做梦回合DTO
/// </summary>
public sealed class DreamTurnDto
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("toolUseCount")]
    public int ToolUseCount { get; set; }
}
