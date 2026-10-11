namespace Core.Tests;

public class PathGuardNodeTests {
    [Theory]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"//server/share/file.txt")]
    public void IsUncPath_UncPath_ReturnsTrue(string path) {
        Assert.True(PathGuardNode.IsUncPath(path));
    }

    [Theory]
    [InlineData(@"/home/user/file.txt")]
    [InlineData(@"C:\Users\file.txt")]
    [InlineData(@"relative/path.txt")]
    public void IsUncPath_NonUncPath_ReturnsFalse(string path) {
        Assert.False(PathGuardNode.IsUncPath(path));
    }

    [Theory]
    [InlineData("notebook.ipynb")]
    [InlineData("/path/to/notebook.IPYNB")]
    public void IsNotebookPath_NotebookPath_ReturnsTrue(string path) {
        Assert.True(PathGuardNode.IsNotebookPath(path));
    }

    [Theory]
    [InlineData("notebook.txt")]
    [InlineData("notebook.py")]
    [InlineData("notebook.ipynb.bak")]
    public void IsNotebookPath_NonNotebookPath_ReturnsFalse(string path) {
        Assert.False(PathGuardNode.IsNotebookPath(path));
    }

    [Theory]
    [InlineData("keyword-sections.json")]
    [InlineData("/path/to/keyword-sections.json")]
    [InlineData(@"C:\dir\KEYWORD-SECTIONS.json")]
    public void IsKeywordSectionsPath_KeywordPath_ReturnsTrue(string path) {
        Assert.True(PathGuardNode.IsKeywordSectionsPath(path));
    }

    [Theory]
    [InlineData("keyword.json")]
    [InlineData("sections.json")]
    [InlineData("")]
    public void IsKeywordSectionsPath_NonKeywordPath_ReturnsFalse(string path) {
        Assert.False(PathGuardNode.IsKeywordSectionsPath(path));
    }

    [Theory]
    [InlineData("/home/.jcc/diag/file.txt")]
    [InlineData("/home/.jcc/reflexion/file.txt")]
    [InlineData("/home/worktree/branch/file.txt")]
    public void IsDoctorAllowedEditPath_AllowedPath_ReturnsTrue(string path) {
        Assert.True(PathGuardNode.IsDoctorAllowedEditPath(path));
    }

    [Theory]
    [InlineData("/home/regular/file.txt")]
    [InlineData("/home/.jcc/other/file.txt")]
    [InlineData("")]
    public void IsDoctorAllowedEditPath_DisallowedPath_ReturnsFalse(string path) {
        Assert.False(PathGuardNode.IsDoctorAllowedEditPath(path));
    }
}