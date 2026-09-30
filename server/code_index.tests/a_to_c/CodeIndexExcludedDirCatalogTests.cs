namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// CodeIndexExcludedDirCatalog 确定性测试 — 验证排除目录检测的各分支
/// </summary>
public sealed class CodeIndexExcludedDirCatalogTests {
    [Theory]
    [InlineData("src/bin/output.dll", true)]
    [InlineData("src/obj/temp.cs", true)]
    [InlineData("src/.git/config", true)]
    [InlineData("src/.x/old.cs", true)]
    [InlineData("src/core/service.cs", false)]
    [InlineData("src/lib/utils.cs", false)]
    [InlineData("service.cs", false)]
    public void IsInExcludedDirectory_DetectsExcludedSegments(string path, bool expected) {
        Assert.Equal(expected, CodeIndexExcludedDirCatalog.IsInExcludedDirectory(path));
    }

    [Fact]
    public void IsInExcludedDirectory_CaseInsensitive_MatchesExcludedDirs() {
        // ExcludedDirs 用 OrdinalIgnoreCase,大小写不敏感应匹配
        Assert.True(CodeIndexExcludedDirCatalog.IsInExcludedDirectory("src/BIN/upper.cs"));
        Assert.True(CodeIndexExcludedDirCatalog.IsInExcludedDirectory("src/Obj/mixed.cs"));
    }

    [Fact]
    public void IsInExcludedDirectory_NestedExcludedDir_Detected() {
        Assert.True(CodeIndexExcludedDirCatalog.IsInExcludedDirectory("src/bin/obj/deep.cs"));
    }

    [Fact]
    public void IsInExcludedDirectory_EmptyString_ReturnsFalse() {
        Assert.False(CodeIndexExcludedDirCatalog.IsInExcludedDirectory(""));
    }

    [Fact]
    public void IsInExcludedDirectory_OnlyExcludedName_ReturnsTrue() {
        Assert.True(CodeIndexExcludedDirCatalog.IsInExcludedDirectory("bin"));
        Assert.True(CodeIndexExcludedDirCatalog.IsInExcludedDirectory("obj"));
    }

    [Fact]
    public void ExcludedDirs_ContainsExpectedSet() {
        Assert.Equal(4, CodeIndexExcludedDirCatalog.ExcludedDirs.Count);
        Assert.Contains("bin", CodeIndexExcludedDirCatalog.DefaultExcludedDirs);
        Assert.Contains("obj", CodeIndexExcludedDirCatalog.DefaultExcludedDirs);
        Assert.Contains(".git", CodeIndexExcludedDirCatalog.DefaultExcludedDirs);
        Assert.Contains(".x", CodeIndexExcludedDirCatalog.DefaultExcludedDirs);
    }

    [Fact]
    public void DefaultExcludedDirs_MatchesExcludedDirs() {
        Assert.Equal(CodeIndexExcludedDirCatalog.ExcludedDirs.Count, CodeIndexExcludedDirCatalog.DefaultExcludedDirs.Length);
        foreach (var d in CodeIndexExcludedDirCatalog.DefaultExcludedDirs) {
            Assert.True(CodeIndexExcludedDirCatalog.ExcludedDirs.Contains(d), $"Missing: {d}");
        }
    }

    #region 委托 ExcludedDirectoryCatalog 一致性验证

    /// <summary>
    /// 验证 CodeIndexExcludedDirCatalog.ExcludedDirs 委托 ExcludedDirectoryCatalog.CodeIndexExcluded(同一引用)
    /// </summary>
    [Fact]
    public void ExcludedDirs_DelegatesToExcludedDirectoryCatalog() {
        Assert.Same(ExcludedDirectoryCatalog.CodeIndexExcluded, CodeIndexExcludedDirCatalog.ExcludedDirs);
    }

    /// <summary>
    /// 验证 CodeIndexExcludedDirCatalog.DefaultExcludedDirs 委托 ExcludedDirectoryCatalog.CodeIndexExcludedArray(同一引用)
    /// </summary>
    [Fact]
    public void DefaultExcludedDirs_DelegatesToExcludedDirectoryCatalog() {
        Assert.Same(ExcludedDirectoryCatalog.CodeIndexExcludedArray, CodeIndexExcludedDirCatalog.DefaultExcludedDirs);
    }

    /// <summary>
    /// 验证 CodeIndexExcludedDirCatalog.IsInExcludedDirectory 委托 ExcludedDirectoryCatalog.IsInExcludedDirectory(行为一致)
    /// </summary>
    [Theory]
    [InlineData("src/bin/x.dll", true)]
    [InlineData("src/obj/y.cs", true)]
    [InlineData("src/.git/config", true)]
    [InlineData("src/.x/old.cs", true)]
    [InlineData("src/core/file.cs", false)]
    [InlineData("", false)]
    public void IsInExcludedDirectory_DelegatesToExcludedDirectoryCatalog(string path, bool expected) {
        Assert.Equal(
            ExcludedDirectoryCatalog.IsInExcludedDirectory(path),
            CodeIndexExcludedDirCatalog.IsInExcludedDirectory(path));
        Assert.Equal(expected, CodeIndexExcludedDirCatalog.IsInExcludedDirectory(path));
    }

    #endregion
}
