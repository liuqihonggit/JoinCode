namespace Guard.Config.Tests.Configuration.ConfigRoot;

public sealed class ProjectLocatorTests {
    private static string Norm(string path) => Path.GetFullPath(path);

    [Fact]
    public async Task LocateProjectRoot_CwdHasGitDir_ReturnsCwd() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/proj/.git");
        fs.SetCurrentDirectory("/proj");

        var locator = new ProjectLocator(fs);
        var root = locator.LocateProjectRoot();

        Assert.Equal(Norm("/proj"), Norm(root ?? string.Empty));
    }

    [Fact]
    public async Task LocateProjectRoot_DeepCwd_ParentHasGitDir_ReturnsParent() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/proj/.git");
        fs.CreateDirectory("/proj/sub/deep");
        fs.SetCurrentDirectory("/proj/sub/deep");

        var locator = new ProjectLocator(fs);
        var root = locator.LocateProjectRoot();

        Assert.Equal(Norm("/proj"), Norm(root ?? string.Empty));
    }

    [Fact]
    public async Task LocateProjectRoot_NoGitOrJcc_ReturnsNull() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/a/b/c");
        fs.SetCurrentDirectory("/a/b/c");

        var locator = new ProjectLocator(fs);
        var root = locator.LocateProjectRoot();

        Assert.Null(root);
    }

    [Fact]
    public async Task LocateProjectRoot_OnlyJccDir_ReturnsJccParent() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/proj/.jcc");
        fs.CreateDirectory("/proj/sub");
        fs.SetCurrentDirectory("/proj/sub");

        var locator = new ProjectLocator(fs);
        var root = locator.LocateProjectRoot();

        Assert.Equal(Norm("/proj"), Norm(root ?? string.Empty));
    }

    [Fact]
    public async Task LocateProjectRoot_BothGitAndJcc_ReturnsNearest() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/outer/.git");
        fs.CreateDirectory("/outer/inner/.jcc");
        fs.SetCurrentDirectory("/outer/inner");

        var locator = new ProjectLocator(fs);
        var root = locator.LocateProjectRoot();

        Assert.Equal(Norm("/outer/inner"), Norm(root ?? string.Empty));
    }

    [Fact]
    public async Task LocateProjectRoot_AtRoot_ReturnsNull() {
        await using var fs = new InMemoryFileSystem();
        fs.SetCurrentDirectory("/");

        var locator = new ProjectLocator(fs);
        var root = locator.LocateProjectRoot();

        Assert.Null(root);
    }
}
