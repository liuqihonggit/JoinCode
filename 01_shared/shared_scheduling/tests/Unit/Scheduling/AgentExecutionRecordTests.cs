
namespace Core.Tests.Scheduling;

public class AgentExecutionRecordTests
{
    [Fact]
    public void AllSuccess_EmptyResults_ShouldBeFalse()
    {
        var record = new AgentExecutionRecord
        {
            TaskId = "t1",
            TaskName = "task",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            TotalDuration = TimeSpan.Zero,
            AgentResults = new List<SubAgentResult>()
        };

        record.AllSuccess.Should().BeFalse();
    }

    [Fact]
    public void AllSuccess_NullResults_ShouldBeFalse()
    {
        var record = new AgentExecutionRecord
        {
            TaskId = "t1",
            TaskName = "task",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            TotalDuration = TimeSpan.Zero,
            AgentResults = null!
        };

        record.AllSuccess.Should().BeFalse();
    }

    [Fact]
    public void AllSuccess_AllSucceeded_ShouldBeTrue()
    {
        var record = new AgentExecutionRecord
        {
            TaskId = "t1",
            TaskName = "task",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            TotalDuration = TimeSpan.Zero,
            AgentResults = new List<SubAgentResult>
            {
                new() { AgentId = "a1", IsSuccess = true, Output = "o1" },
                new() { AgentId = "a2", IsSuccess = true, Output = "o2" }
            }
        };

        record.AllSuccess.Should().BeTrue();
    }

    [Fact]
    public void AllSuccess_OneFailed_ShouldBeFalse()
    {
        var record = new AgentExecutionRecord
        {
            TaskId = "t1",
            TaskName = "task",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            TotalDuration = TimeSpan.Zero,
            AgentResults = new List<SubAgentResult>
            {
                new() { AgentId = "a1", IsSuccess = true, Output = "o1" },
                new() { AgentId = "a2", IsSuccess = false, Output = "o2" }
            }
        };

        record.AllSuccess.Should().BeFalse();
    }

    [Fact]
    public void SuccessCount_And_FailureCount_ShouldMatchResults()
    {
        var record = new AgentExecutionRecord
        {
            TaskId = "t1",
            TaskName = "task",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            TotalDuration = TimeSpan.Zero,
            AgentResults = new List<SubAgentResult>
            {
                new() { AgentId = "a1", IsSuccess = true, Output = "o1" },
                new() { AgentId = "a2", IsSuccess = false, Output = "o2" },
                new() { AgentId = "a3", IsSuccess = true, Output = "o3" }
            }
        };

        record.SuccessCount.Should().Be(2);
        record.FailureCount.Should().Be(1);
    }

    [Fact]
    public void GetMergedOutput_EmptyOrNull_ShouldReturnEmptyString()
    {
        var emptyRecord = new AgentExecutionRecord
        {
            TaskId = "t1",
            TaskName = "task",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            TotalDuration = TimeSpan.Zero,
            AgentResults = new List<SubAgentResult>()
        };

        emptyRecord.GetMergedOutput().Should().BeEmpty();

        var nullRecord = new AgentExecutionRecord
        {
            TaskId = "t1",
            TaskName = "task",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            TotalDuration = TimeSpan.Zero,
            AgentResults = null!
        };

        nullRecord.GetMergedOutput().Should().BeEmpty();
    }

    [Fact]
    public void GetMergedOutput_ShouldSkipFailedAndEmptyOutputs()
    {
        var record = new AgentExecutionRecord
        {
            TaskId = "t1",
            TaskName = "task",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            TotalDuration = TimeSpan.Zero,
            AgentResults = new List<SubAgentResult>
            {
                new() { AgentId = "a1", IsSuccess = true, Output = "first" },
                new() { AgentId = "a2", IsSuccess = false, Output = "failed" },
                new() { AgentId = "a3", IsSuccess = true, Output = "" },
                new() { AgentId = "a4", IsSuccess = true, Output = "second" }
            }
        };

        var merged = record.GetMergedOutput();
        merged.Should().Contain("first");
        merged.Should().Contain("second");
        merged.Should().NotContain("failed");
        merged.Should().Contain("\n\n");
    }
}
