namespace Dream.Tests.Models;

/// <summary>
/// 做梦任务 DTO 映射单元测试
/// </summary>
public sealed class DreamTaskDtoTests
{
    [Fact]
    public void FromState_MapsAllProperties()
    {
        var state = new DreamTaskState
        {
            Id = "d12345678",
            Description = "dreaming",
            StartTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            Notified = true,
            Status = DreamTaskStatus.Completed,
            Phase = DreamPhase.Updating,
            SessionsReviewing = 3,
            PriorMtime = 123
        };
        state.FilesTouched.Add("a.md");
        state.Turns.Add(new DreamTurn { Text = "turn", ToolUseCount = 2 });

        var dto = DreamTaskDto.FromState(state);

        Assert.Equal("d12345678", dto.Id);
        Assert.Equal("Completed", dto.Status);
        Assert.Equal("dreaming", dto.Description);
        Assert.Equal(state.StartTime, dto.StartTime);
        Assert.Equal(state.EndTime, dto.EndTime);
        Assert.True(dto.Notified);
        Assert.Equal("Updating", dto.Phase);
        Assert.Equal(3, dto.SessionsReviewing);
        Assert.Single(dto.FilesTouched);
        Assert.Single(dto.Turns);
        Assert.Equal(123, dto.PriorMtime);
    }

    [Fact]
    public void ToState_MapsAllProperties()
    {
        var dto = new DreamTaskDto
        {
            Id = "d12345678",
            Description = "dreaming",
            StartTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            Notified = true,
            Status = "Completed",
            Phase = "Updating",
            SessionsReviewing = 3,
            PriorMtime = 123,
            FilesTouched = new List<string> { "a.md" },
            Turns = new List<DreamTurnDto> { new() { Text = "turn", ToolUseCount = 2 } }
        };

        var state = dto.ToState();

        Assert.Equal("d12345678", state.Id);
        Assert.Equal(DreamTaskStatus.Completed, state.Status);
        Assert.Equal(DreamPhase.Updating, state.Phase);
        Assert.Equal("dreaming", state.Description);
        Assert.Equal(dto.StartTime, state.StartTime);
        Assert.Equal(dto.EndTime, state.EndTime);
        Assert.True(state.Notified);
        Assert.Equal(3, state.SessionsReviewing);
        Assert.Equal(123, state.PriorMtime);
        Assert.Single(state.FilesTouched);
        Assert.Single(state.Turns);
    }

    [Fact]
    public void ToState_WithInvalidStatus_ParsesDefault()
    {
        var dto = new DreamTaskDto
        {
            Id = "d12345678",
            Description = "dreaming",
            StartTime = DateTime.UtcNow,
            Status = "NotAStatus",
            Phase = "Updating",
            SessionsReviewing = 1,
            PriorMtime = 0
        };

        var state = dto.ToState();

        Assert.Equal(DreamTaskStatus.Running, state.Status);
    }

    [Fact]
    public void ToState_WithInvalidPhase_ParsesDefault()
    {
        var dto = new DreamTaskDto
        {
            Id = "d12345678",
            Description = "dreaming",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            Phase = "NotAPhase",
            SessionsReviewing = 1,
            PriorMtime = 0
        };

        var state = dto.ToState();

        Assert.Equal(DreamPhase.Starting, state.Phase);
    }

    [Fact]
    public void Roundtrip_PreservesValues()
    {
        var original = new DreamTaskState
        {
            Id = "d12345678",
            Description = "dreaming",
            StartTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            Notified = true,
            Status = DreamTaskStatus.Failed,
            Phase = DreamPhase.Updating,
            SessionsReviewing = 5,
            PriorMtime = 999
        };
        original.FilesTouched.AddRange(new[] { "a.md", "b.md" });
        original.Turns.Add(new DreamTurn { Text = "turn1", ToolUseCount = 1 });

        var roundtrip = DreamTaskDto.FromState(original).ToState();

        Assert.Equal(original.Id, roundtrip.Id);
        Assert.Equal(original.Status, roundtrip.Status);
        Assert.Equal(original.Phase, roundtrip.Phase);
        Assert.Equal(original.Description, roundtrip.Description);
        Assert.Equal(original.StartTime, roundtrip.StartTime);
        Assert.Equal(original.EndTime, roundtrip.EndTime);
        Assert.Equal(original.Notified, roundtrip.Notified);
        Assert.Equal(original.SessionsReviewing, roundtrip.SessionsReviewing);
        Assert.Equal(original.PriorMtime, roundtrip.PriorMtime);
        Assert.Equal(original.FilesTouched.Count, roundtrip.FilesTouched.Count);
        Assert.Equal(original.Turns.Count, roundtrip.Turns.Count);
    }
}