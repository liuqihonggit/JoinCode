namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// directoryPicker/list|createDirectory 测试（TASK066 C6）：浏览宿主目录与创建子目录，
/// 语义对齐 dsh-host-directory-picker-browse（只列目录、隐藏项标记、面包屑、1000 条截断、单段名校验）。
/// </summary>
public sealed class DshDirectoryBrowserTests {
    private static async Task<DshDirectoryBrowser> CreateBrowserAsync(InMemoryFileSystem fs, int maxEntries = 1000) {
        fs.CreateDirectory("/home/user/projects");
        fs.CreateDirectory("/home/user/a");
        fs.CreateDirectory("/home/user/.config");
        fs.CreateDirectory("/home/user/projects/demo");
        await fs.WriteAllTextAsync("/home/user/projects/readme.md", "x");
        return new DshDirectoryBrowser(fs, () => "/home/user", maxEntries);
    }

    [Fact]
    public async Task List_DefaultsToHomeWithCrumbsAndDirectoryRows() {
        var browser = await CreateBrowserAsync(new InMemoryFileSystem());

        var listing = browser.List(null);

        listing.Path.Should().Be("/home/user");
        listing.Home.Should().Be("/home/user");
        listing.Crumbs.Select(crumb => crumb.Name).Should().Equal("/", "home", "user");
        listing.Crumbs.Select(crumb => crumb.Path).Should().Equal("/", "/home", "/home/user");
        listing.Entries.Select(entry => entry.Name).Should().Equal(".config", "a", "projects");
        listing.Entries.Select(entry => entry.Hidden).Should().Equal(true, false, false);
        listing.Truncated.Should().BeFalse("只有目录参与列表，文件不应占位");
    }

    [Fact]
    public async Task List_TruncatesAtMaxEntries() {
        var browser = await CreateBrowserAsync(new InMemoryFileSystem(), maxEntries: 2);

        var listing = browser.List("/home/user");

        listing.Entries.Should().HaveCount(2);
        listing.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task List_RejectsRelativeAndMissingPaths() {
        var browser = await CreateBrowserAsync(new InMemoryFileSystem());

        var relative = Assert.Throws<DshDirectoryBrowserException>(() => browser.List("home/user"));
        relative.Code.Should().Be("directory-unreadable");
        relative.Message.Should().Contain("绝对路径");

        var missing = Assert.Throws<DshDirectoryBrowserException>(() => browser.List("/home/user/missing"));
        missing.Code.Should().Be("directory-unreadable");
        missing.Message.Should().Contain("directoryPicker/list");
    }

    [Fact]
    public async Task CreateDirectory_JoinsSingleSegmentAndReportsExisting() {
        await using var fs = new InMemoryFileSystem();
        var browser = await CreateBrowserAsync(fs);

        var created = browser.CreateDirectory("/home/user", "new-project");

        created.Should().Be("/home/user/new-project");
        fs.DirectoryExists("/home/user/new-project").Should().BeTrue();

        var existing = Assert.Throws<DshDirectoryBrowserException>(() => browser.CreateDirectory("/home/user", "new-project"));
        existing.Code.Should().Be("directory-exists");
        existing.Message.Should().Contain("已存在");
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task CreateDirectory_RejectsMultiSegmentNames(string name) {
        var browser = await CreateBrowserAsync(new InMemoryFileSystem());

        var error = Assert.Throws<DshDirectoryBrowserException>(() => browser.CreateDirectory("/home/user", name));

        error.Code.Should().Be("directory-create-failed");
        error.Message.Should().Contain("单段目录名");
    }

    [Fact]
    public async Task CreateDirectory_RejectsRelativeParent() {
        var browser = await CreateBrowserAsync(new InMemoryFileSystem());

        var error = Assert.Throws<DshDirectoryBrowserException>(() => browser.CreateDirectory("home/user", "x"));

        error.Code.Should().Be("directory-create-failed");
        error.Message.Should().Contain("绝对父目录");
    }
}
