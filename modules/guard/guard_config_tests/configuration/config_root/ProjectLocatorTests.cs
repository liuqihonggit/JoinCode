namespace Guard.Config.Tests.Configuration.ConfigRoot;

public sealed class ProjectLocatorTests {
    private static string Norm(string path) => Path.GetFullPath(path);

    [Fact]
    public async Task TryLocateProjectRoot_CwdHasGitDir_ReturnsTrueAndCwd() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/proj/.git");
        fs.SetCurrentDirectory("/proj");

        var locator = new ProjectLocator(fs);
        var found = locator.TryLocateProjectRoot(out var root);

        Assert.True(found);
        Assert.Equal(Norm("/proj"), Norm(root ?? string.Empty));
    }

    [Fact]
    public async Task TryLocateProjectRoot_DeepCwd_ParentHasGitDir_ReturnsTrueAndParent() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/proj/.git");
        fs.CreateDirectory("/proj/sub/deep");
        fs.SetCurrentDirectory("/proj/sub/deep");

        var locator = new ProjectLocator(fs);
        var found = locator.TryLocateProjectRoot(out var root);

        Assert.True(found);
        Assert.Equal(Norm("/proj"), Norm(root ?? string.Empty));
    }

    [Fact]
    public async Task TryLocateProjectRoot_NoGitOrJcc_ReturnsFalse() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/a/b/c");
        fs.SetCurrentDirectory("/a/b/c");

        var locator = new ProjectLocator(fs);
        var found = locator.TryLocateProjectRoot(out var root);

        Assert.False(found);
        Assert.Null(root);
    }

    [Fact]
    public async Task TryLocateProjectRoot_OnlyJccDir_ReturnsTrueAndJccParent() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/proj/.jcc");
        fs.CreateDirectory("/proj/sub");
        fs.SetCurrentDirectory("/proj/sub");

        var locator = new ProjectLocator(fs);
        var found = locator.TryLocateProjectRoot(out var root);

        Assert.True(found);
        Assert.Equal(Norm("/proj"), Norm(root ?? string.Empty));
    }

    [Fact]
    public async Task TryLocateProjectRoot_BothGitAndJcc_ReturnsTrueAndNearest() {
        await using var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/outer/.git");
        fs.CreateDirectory("/outer/inner/.jcc");
        fs.SetCurrentDirectory("/outer/inner");

        var locator = new ProjectLocator(fs);
        var found = locator.TryLocateProjectRoot(out var root);

        Assert.True(found);
        Assert.Equal(Norm("/outer/inner"), Norm(root ?? string.Empty));
    }

    [Fact]
    public async Task TryLocateProjectRoot_AtRoot_ReturnsFalse() {
        await using var fs = new InMemoryFileSystem();
        fs.SetCurrentDirectory("/");

        var locator = new ProjectLocator(fs);
        var found = locator.TryLocateProjectRoot(out var root);

        Assert.False(found);
        Assert.Null(root);
    }
}
