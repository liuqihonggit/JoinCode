
namespace JoinCode.Dream;

/// <summary>
/// 做梦任务DTO - 用于JSON序列化
/// </summary>
public sealed class DreamTaskDto {
    /// <summary>任务 ID</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>任务状态</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>任务描述</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>开始时间</summary>
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    /// <summary>结束时间（可空）</summary>
    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    /// <summary>是否已通知</summary>
    [JsonPropertyName("notified")]
    public bool Notified { get; set; }

    /// <summary>当前阶段</summary>
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    /// <summary>正在审查的会话数</summary>
    [JsonPropertyName("sessionsReviewing")]
    public int SessionsReviewing { get; set; }

    /// <summary>触及的文件列表</summary>
    [JsonPropertyName("filesTouched")]
    public List<string> FilesTouched { get; set; } = new();

    /// <summary>回合列表</summary>
    [JsonPropertyName("turns")]
    public List<DreamTurnDto> Turns { get; set; } = new();

    /// <summary>先前修改时间</summary>
    [JsonPropertyName("priorMtime")]
    public long PriorMtime { get; set; }

    /// <summary>
    /// 从状态创建DTO
    /// </summary>
    /// <param name="state">做梦任务状态对象</param>
    /// <returns>对应的 DTO 实例</returns>
    public static DreamTaskDto FromState(DreamTaskState state) {
        return new DreamTaskDto {
            Id = state.Id,
            Status = state.Status.ToString(),
            Description = state.Description,
            StartTime = state.StartTime,
            EndTime = state.EndTime,
            Notified = state.Notified,
            Phase = state.Phase.ToString(),
            SessionsReviewing = state.SessionsReviewing,
            FilesTouched = new List<string>(state.FilesTouched),
            Turns = state.Turns.Select(t => new DreamTurnDto {
                Text = t.Text,
                ToolUseCount = t.ToolUseCount
            }).ToList(),
            PriorMtime = state.PriorMtime
        };
    }

    /// <summary>
    /// 转换为状态对象
    /// </summary>
    /// <returns>对应的状态对象</returns>
    public DreamTaskState ToState() {
        var state = new DreamTaskState {
            Id = Id,
            Description = Description,
            StartTime = StartTime,
            EndTime = EndTime,
            Notified = Notified,
            SessionsReviewing = SessionsReviewing,
            PriorMtime = PriorMtime
        };

        if (Enum.TryParse<DreamTaskStatus>(Status, out var status)) {
            state.Status = status;
        }

        if (Enum.TryParse<DreamPhase>(Phase, out var phase)) {
            state.Phase = phase;
        }

        foreach (var file in FilesTouched) {
            state.FilesTouched.Add(file);
        }

        foreach (var turn in Turns) {
            state.Turns.Add(new DreamTurn {
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
public sealed class DreamTurnDto {
    /// <summary>回合文本</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>工具使用次数</summary>
    [JsonPropertyName("toolUseCount")]
    public int ToolUseCount { get; set; }
}