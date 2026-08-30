namespace Infra.Tests.HotSpot;


public sealed class TaskTableGeneratorTests
{
    private readonly ITaskTableGenerator _sut = new TaskTableGenerator();

    private static TaskTableEntry MakeEntry(string id, string desc, bool hot = false, string status = "pending") =>
        new()
        {
            Id = id,
            Description = desc,
            Files = [$"src/{id}.cs"],
            Role = "worker",
            Dependencies = [],
            Verification = "编译通过",
            IsHotFile = hot,
            HotSpotAnnotation = hot ? "队长收口" : "",
            Status = status
        };

    [Fact]
    public void Generate_Empty_ShouldReturnNoTasks()
    {
        var result = _sut.Generate([]);
        result.Should().Contain("无任务");
    }

    [Fact]
    public void Generate_SingleEntry_ShouldContainTableRow()
    {
        var result = _sut.Generate([MakeEntry("T1", "实现功能A")]);

        result.Should().Contain("| 编号 |");
        result.Should().Contain("T1");
        result.Should().Contain("实现功能A");
        result.Should().Contain("src/T1.cs");
    }

    [Fact]
    public void Generate_HotFile_ShouldHaveFireEmoji()
    {
        var result = _sut.Generate([MakeEntry("T1", "改接口", hot: true)]);

        result.Should().Contain("🔥");
        result.Should().Contain("队长收口");
    }

    [Fact]
    public void Generate_NormalFile_ShouldNotHaveFireEmoji()
    {
        var result = _sut.Generate([MakeEntry("T1", "改内部")]);

        result.Should().NotContain("🔥");
    }

    [Fact]
    public void Generate_MultipleEntries_ShouldHaveAllRows()
    {
        var entries = new List<TaskTableEntry>
        {
            MakeEntry("T1", "任务1"),
            MakeEntry("T2", "任务2", hot: true),
            MakeEntry("T3", "任务3", status: "completed")
        };

        var result = _sut.Generate(entries);

        result.Should().Contain("T1");
        result.Should().Contain("T2");
        result.Should().Contain("T3");
    }

    [Fact]
    public void UpdateStatus_ShouldChangeStatusAndRegenerate()
    {
        var entries = new List<TaskTableEntry>
        {
            MakeEntry("T1", "任务1", status: "pending"),
            MakeEntry("T2", "任务2")
        };

        var result = _sut.UpdateStatus(entries, "T1", "completed");

        result.Should().Contain("completed");
        result.Should().NotContain("| T1 | 任务1 | src/T1.cs | worker |  | 编译通过 |  |  | pending |");
    }

    [Fact]
    public void Generate_WithDependencies_ShouldShowDependencyIds()
    {
        var entry = new TaskTableEntry
        {
            Id = "T2",
            Description = "依赖T1的任务",
            Files = ["src/T2.cs"],
            Role = "worker",
            Dependencies = ["T1"],
            Verification = "测试通过",
            IsHotFile = false,
            Status = "pending"
        };

        var result = _sut.Generate([entry]);

        result.Should().Contain("T1");
    }
}
