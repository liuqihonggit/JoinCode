namespace Core.Tests.LLM;

public sealed class WorkspaceServiceTests {
    [Fact]
    public async Task AddDirectory_ShouldAddPath() {
        await using var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();

        var added = service.AddDirectory(tempPath);

        added.Should().BeTrue();
        service.GetAdditionalDirectories().Should().Contain(Path.GetFullPath(tempPath));
    }

    [Fact]
    public async Task AddDirectory_SamePathTwice_ShouldReturnFalse() {
        await using var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();

        service.AddDirectory(tempPath);
        var added = service.AddDirectory(tempPath);

        added.Should().BeFalse();
        service.GetAdditionalDirectories().Should().HaveCount(1);
    }

    [Fact]
    public void AddDirectory_NullPath_ShouldThrow() {
        var service = new WorkspaceService();

        var act = () => service.AddDirectory(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RemoveDirectory_ShouldRemovePath() {
        await using var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();
        service.AddDirectory(tempPath);

        var removed = service.RemoveDirectory(tempPath);

        removed.Should().BeTrue();
        service.GetAdditionalDirectories().Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveDirectory_NonExistent_ShouldReturnFalse() {
        await using var service = new WorkspaceService();

        var removed = service.RemoveDirectory(Path.GetTempPath());

        removed.Should().BeFalse();
    }

    [Fact]
    public async Task GetAdditionalDirectories_ShouldReturnFullPath() {
        await using var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();
        var expected = Path.GetFullPath(tempPath);

        service.AddDirectory(tempPath);

        service.GetAdditionalDirectories().Should().Contain(expected);
    }

    [Fact]
    public async Task Clear_ShouldRemoveAllDirectories() {
        await using var service = new WorkspaceService();
        service.AddDirectory(Path.GetTempPath());

        service.Clear();

        service.GetAdditionalDirectories().Should().BeEmpty();
    }

    [Fact]
    public async Task AddDirectory_MultiplePaths_ShouldReturnAll() {
        await using var service = new WorkspaceService();
        var temp1 = Path.GetTempPath();
        var temp2 = Path.Combine(Path.GetTempPath(), "..");

        service.AddDirectory(temp1);
        service.AddDirectory(temp2);

        service.GetAdditionalDirectories().Should().HaveCount(2);
    }
}