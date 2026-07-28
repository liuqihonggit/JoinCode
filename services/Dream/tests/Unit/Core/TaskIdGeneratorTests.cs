
namespace Dream.Tests;

/// <summary>
/// 任务ID生成器测试
/// </summary>
public sealed class TaskIdGeneratorTests
{
    [Theory]
    [InlineData(TaskType.Dream, 'd')]
    [InlineData(TaskType.LocalBash, 'b')]
    [InlineData(TaskType.LocalAgent, 'a')]
    [InlineData(TaskType.RemoteAgent, 'r')]
    [InlineData(TaskType.InProcessTeammate, 't')]
    [InlineData(TaskType.LocalWorkflow, 'w')]
    [InlineData(TaskType.MonitorMcp, 'm')]
    public void GenerateTaskId_ShouldStartWithCorrectPrefix(TaskType type, char expectedPrefix)
    {
        // Act
        var taskId = TaskIdGenerator.GenerateTaskId(type);

        // Assert
        Assert.StartsWith(expectedPrefix.ToString(), taskId);
    }

    [Fact]
    public void GenerateTaskId_DreamType_ShouldHaveCorrectLength()
    {
        // Act
        var taskId = TaskIdGenerator.GenerateTaskId(TaskType.Dream);

        // Assert
        Assert.Equal(9, taskId.Length); // 1位前缀 + 8位随机
    }

    [Fact]
    public void GenerateTaskId_ShouldGenerateUniqueIds()
    {
        // Act
        var ids = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            ids.Add(TaskIdGenerator.GenerateTaskId(TaskType.Dream));
        }

        // Assert
        Assert.Equal(100, ids.Count);
    }

    [Fact]
    public void GenerateTaskId_ShouldOnlyContainValidCharacters()
    {
        // Arrange
        const string validChars = "0123456789abcdefghijklmnopqrstuvwxyz";

        // Act
        var taskId = TaskIdGenerator.GenerateTaskId(TaskType.Dream);

        // Assert
        foreach (var c in taskId.ToLowerInvariant())
        {
            Assert.Contains(c, validChars);
        }
    }
}
