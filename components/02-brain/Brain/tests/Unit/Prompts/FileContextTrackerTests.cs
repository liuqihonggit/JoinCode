namespace Core.Tests.Prompts;

public sealed class FileContextTrackerTests
{
    [Fact]
    public void Initial_State_Should_Be_Empty()
    {
        var tracker = new FileContextTracker();

        tracker.CurrentFilePaths.Should().BeEmpty();
    }

    [Fact]
    public void UpdateFilePaths_Should_Set_Paths()
    {
        var tracker = new FileContextTracker();
        var paths = new[] { "file1.cs", "file2.ts" };

        tracker.UpdateFilePaths(paths);

        tracker.CurrentFilePaths.Should().HaveCount(2);
        tracker.CurrentFilePaths[0].Should().Be("file1.cs");
        tracker.CurrentFilePaths[1].Should().Be("file2.ts");
    }

    [Fact]
    public void UpdateFilePaths_With_Null_Should_Clear()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["file1.cs"]);

        tracker.UpdateFilePaths(null!);

        tracker.CurrentFilePaths.Should().BeEmpty();
    }

    [Fact]
    public void Clear_Should_Reset_To_Empty()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["file1.cs", "file2.ts"]);

        tracker.Clear();

        tracker.CurrentFilePaths.Should().BeEmpty();
    }

    [Fact]
    public void UpdateFilePaths_Should_Replace_Previous_Paths()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["old.cs"]);

        tracker.UpdateFilePaths(["new1.cs", "new2.ts"]);

        tracker.CurrentFilePaths.Should().HaveCount(2);
        tracker.CurrentFilePaths[0].Should().Be("new1.cs");
        tracker.CurrentFilePaths[1].Should().Be("new2.ts");
    }

    [Fact]
    public void UpdateFilePaths_With_Empty_Array_Should_Clear()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["file1.cs"]);

        tracker.UpdateFilePaths([]);

        tracker.CurrentFilePaths.Should().BeEmpty();
    }

    [Fact]
    public void Initial_User_Message_Should_Be_Empty()
    {
        var tracker = new FileContextTracker();

        tracker.CurrentUserMessage.Should().BeEmpty();
    }

    [Fact]
    public void UpdateUserMessage_Should_Set_Message()
    {
        var tracker = new FileContextTracker();

        tracker.UpdateUserMessage("修复bug");

        tracker.CurrentUserMessage.Should().Be("修复bug");
    }

    [Fact]
    public void UpdateUserMessage_With_Null_Should_Set_Empty()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("修复bug");

        tracker.UpdateUserMessage(null!);

        tracker.CurrentUserMessage.Should().BeEmpty();
    }

    [Fact]
    public void Clear_Should_Reset_User_Message()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("修复bug");

        tracker.Clear();

        tracker.CurrentUserMessage.Should().BeEmpty();
    }
}
