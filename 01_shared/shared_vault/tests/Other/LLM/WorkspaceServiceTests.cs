namespace Core.Tests.LLM;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public void AddDirectory_ShouldAddPath()
    {
        var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();

        var added = service.AddDirectory(tempPath);

        added.Should().BeTrue();
        service.GetAdditionalDirectories().Should().Contain(Path.GetFullPath(tempPath));
    }

    [Fact]
    public void AddDirectory_SamePathTwice_ShouldReturnFalse()
    {
        var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();

        service.AddDirectory(tempPath);
        var added = service.AddDirectory(tempPath);

        added.Should().BeFalse();
        service.GetAdditionalDirectories().Should().HaveCount(1);
    }

    [Fact]
    public void AddDirectory_NullPath_ShouldThrow()
    {
        var service = new WorkspaceService();

        var act = () => service.AddDirectory(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveDirectory_ShouldRemovePath()
    {
        var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();
        service.AddDirectory(tempPath);

        var removed = service.RemoveDirectory(tempPath);

        removed.Should().BeTrue();
        service.GetAdditionalDirectories().Should().BeEmpty();
    }

    [Fact]
    public void RemoveDirectory_NonExistent_ShouldReturnFalse()
    {
        var service = new WorkspaceService();

        var removed = service.RemoveDirectory(Path.GetTempPath());

        removed.Should().BeFalse();
    }

    [Fact]
    public void GetAdditionalDirectories_ShouldReturnFullPath()
    {
        var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();
        var expected = Path.GetFullPath(tempPath);

        service.AddDirectory(tempPath);

        service.GetAdditionalDirectories().Should().Contain(expected);
    }

    [Fact]
    public void Clear_ShouldRemoveAllDirectories()
    {
        var service = new WorkspaceService();
        service.AddDirectory(Path.GetTempPath());

        service.Clear();

        service.GetAdditionalDirectories().Should().BeEmpty();
    }

    [Fact]
    public void AddDirectory_MultiplePaths_ShouldReturnAll()
    {
        var service = new WorkspaceService();
        var temp1 = Path.GetTempPath();
        var temp2 = Path.Combine(Path.GetTempPath(), "..");

        service.AddDirectory(temp1);
        service.AddDirectory(temp2);

        service.GetAdditionalDirectories().Should().HaveCount(2);
    }
}
