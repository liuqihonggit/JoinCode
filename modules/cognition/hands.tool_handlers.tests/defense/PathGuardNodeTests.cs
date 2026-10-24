// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
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
    public void IsKeywordSectionsPath_KeywordPath_ReturnsTrue(string path) {
        Assert.True(PathGuardNode.IsKeywordSectionsPath(path));
    }

    [SkippableFact]
    public void IsKeywordSectionsPath_WindowsPath_ReturnsTrue() {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows 路径分隔符 \\");
        Assert.True(PathGuardNode.IsKeywordSectionsPath(@"C:\dir\KEYWORD-SECTIONS.json"));
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