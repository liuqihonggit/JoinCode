namespace Abs.Tests.Constants;

/// <summary>
/// ExcludedDirectoryCatalog 确定性测试 — 验证排除目录单数据源的分层集合、数组形式、路径检测
/// 单数据源+委托消费:本测试验证 catalog 自身正确性,各消费方(HotFileDetector/MarkdownWalker/FileFilter/SessionInitStep/CodeIndexExcludedDirCatalog)直接引用 catalog 属性,委托正确性由编译期保证
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class ExcludedDirectoryCatalogTests {
    #region 核心集 CodeIndexExcluded

    [Fact]
    public void CodeIndexExcluded_ContainsExpected4Dirs() {
        ExcludedDirectoryCatalog.CodeIndexExcluded.Should().HaveCount(4);
        ExcludedDirectoryCatalog.CodeIndexExcluded.Should().Contain("bin");
        ExcludedDirectoryCatalog.CodeIndexExcluded.Should().Contain("obj");
        ExcludedDirectoryCatalog.CodeIndexExcluded.Should().Contain(".git");
        ExcludedDirectoryCatalog.CodeIndexExcluded.Should().Contain(".x");
    }

    [Fact]
    public void CodeIndexExcludedArray_MatchesFrozenSet() {
        ExcludedDirectoryCatalog.CodeIndexExcludedArray.Should().HaveCount(ExcludedDirectoryCatalog.CodeIndexExcluded.Count);
        foreach (var d in ExcludedDirectoryCatalog.CodeIndexExcludedArray) {
            ExcludedDirectoryCatalog.CodeIndexExcluded.Should().Contain(d);
        }
    }

    #endregion

    #region 搜索集 SearchExcluded

    [Fact]
    public void SearchExcluded_ContainsExpected7Dirs() {
        ExcludedDirectoryCatalog.SearchExcluded.Should().HaveCount(7);
        // 核心 4 个
        ExcludedDirectoryCatalog.SearchExcluded.Should().Contain("bin").And.Contain("obj").And.Contain(".git").And.Contain(".x");
        // 扩展 3 个
        ExcludedDirectoryCatalog.SearchExcluded.Should().Contain(".vs").And.Contain(".idea").And.Contain("node_modules");
    }

    [Fact]
    public void SearchExcludedArray_MatchesFrozenSet() {
        ExcludedDirectoryCatalog.SearchExcludedArray.Should().HaveCount(ExcludedDirectoryCatalog.SearchExcluded.Count);
        foreach (var d in ExcludedDirectoryCatalog.SearchExcludedArray) {
            ExcludedDirectoryCatalog.SearchExcluded.Should().Contain(d);
        }
    }

    #endregion

    #region 热文件集 HotFileExcluded

    [Fact]
    public void HotFileExcluded_ContainsExpected17Dirs() {
        ExcludedDirectoryCatalog.HotFileExcluded.Should().HaveCount(17);
        // 核心 4 + 搜索 3 + 热文件 10
        ExcludedDirectoryCatalog.HotFileExcluded.Should()
            .Contain("bin").And.Contain("obj").And.Contain(".git").And.Contain(".x")
            .And.Contain(".vs").And.Contain(".idea").And.Contain("node_modules")
            .And.Contain(".vscode").And.Contain(".svn").And.Contain("__pycache__")
            .And.Contain(".gradle").And.Contain("build").And.Contain("dist")
            .And.Contain("target").And.Contain("artifacts").And.Contain(".codegraph").And.Contain(".jcc");
    }

    [Fact]
    public void HotFileExcludedArray_MatchesFrozenSet() {
        ExcludedDirectoryCatalog.HotFileExcludedArray.Should().HaveCount(ExcludedDirectoryCatalog.HotFileExcluded.Count);
        foreach (var d in ExcludedDirectoryCatalog.HotFileExcludedArray) {
            ExcludedDirectoryCatalog.HotFileExcluded.Should().Contain(d);
        }
    }

    #endregion

    #region Markdown 遍历集 MarkdownWalkExcluded

    [Fact]
    public void MarkdownWalkExcluded_ContainsExpected13Dirs() {
        ExcludedDirectoryCatalog.MarkdownWalkExcluded.Should().HaveCount(13);
        // 核心 4 + Markdown 9
        ExcludedDirectoryCatalog.MarkdownWalkExcluded.Should()
            .Contain("bin").And.Contain("obj").And.Contain(".git").And.Contain(".x")
            .And.Contain(".svn").And.Contain(".hg").And.Contain("node_modules")
            .And.Contain(".vs").And.Contain(".vscode").And.Contain(".idea")
            .And.Contain("dist").And.Contain("build").And.Contain("out");
    }

    [Fact]
    public void MarkdownWalkExcludedArray_MatchesFrozenSet() {
        ExcludedDirectoryCatalog.MarkdownWalkExcludedArray.Should().HaveCount(ExcludedDirectoryCatalog.MarkdownWalkExcluded.Count);
        foreach (var d in ExcludedDirectoryCatalog.MarkdownWalkExcludedArray) {
            ExcludedDirectoryCatalog.MarkdownWalkExcluded.Should().Contain(d);
        }
    }

    #endregion

    #region 审计集 AuditExcluded

    [Fact]
    public void AuditExcluded_ContainsExpected9Dirs() {
        ExcludedDirectoryCatalog.AuditExcluded.Should().HaveCount(9);
        // 核心 4 + 审计 5
        ExcludedDirectoryCatalog.AuditExcluded.Should()
            .Contain("bin").And.Contain("obj").And.Contain(".git").And.Contain(".x")
            .And.Contain(".xxx").And.Contain(".vs").And.Contain("artifacts")
            .And.Contain("node_modules").And.Contain(".nuget");
    }

    [Fact]
    public void AuditExcludedArray_MatchesFrozenSet() {
        ExcludedDirectoryCatalog.AuditExcludedArray.Should().HaveCount(ExcludedDirectoryCatalog.AuditExcluded.Count);
        foreach (var d in ExcludedDirectoryCatalog.AuditExcludedArray) {
            ExcludedDirectoryCatalog.AuditExcluded.Should().Contain(d);
        }
    }

    #endregion

    #region 分层关系

    [Fact]
    public void Layered_SearchExcludedIsSupersetOfCodeIndex() {
        foreach (var d in ExcludedDirectoryCatalog.CodeIndexExcluded) {
            ExcludedDirectoryCatalog.SearchExcluded.Should().Contain(d);
        }
    }

    [Fact]
    public void Layered_HotFileExcludedIsSupersetOfSearch() {
        foreach (var d in ExcludedDirectoryCatalog.SearchExcluded) {
            ExcludedDirectoryCatalog.HotFileExcluded.Should().Contain(d);
        }
    }

    [Fact]
    public void Layered_HotFileExcludedIsSupersetOfCodeIndex() {
        foreach (var d in ExcludedDirectoryCatalog.CodeIndexExcluded) {
            ExcludedDirectoryCatalog.HotFileExcluded.Should().Contain(d);
        }
    }

    #endregion

    #region IsInExcludedDirectory

    [Theory]
    [InlineData("src/bin/output.dll", true)]
    [InlineData("src/obj/temp.cs", true)]
    [InlineData("src/.git/config", true)]
    [InlineData("src/.x/old.cs", true)]
    [InlineData("src/core/service.cs", false)]
    [InlineData("src/lib/utils.cs", false)]
    [InlineData("service.cs", false)]
    public void IsInExcludedDirectory_DetectsExcludedSegments(string path, bool expected) {
        ExcludedDirectoryCatalog.IsInExcludedDirectory(path).Should().Be(expected);
    }

    [Fact]
    public void IsInExcludedDirectory_CaseInsensitive_MatchesExcludedDirs() {
        ExcludedDirectoryCatalog.IsInExcludedDirectory("src/BIN/upper.cs").Should().BeTrue();
        ExcludedDirectoryCatalog.IsInExcludedDirectory("src/Obj/mixed.cs").Should().BeTrue();
        ExcludedDirectoryCatalog.IsInExcludedDirectory("src/.GIT/config").Should().BeTrue();
    }

    [Fact]
    public void IsInExcludedDirectory_NestedExcludedDir_Detected() {
        ExcludedDirectoryCatalog.IsInExcludedDirectory("src/bin/obj/deep.cs").Should().BeTrue();
    }

    [Fact]
    public void IsInExcludedDirectory_EmptyString_ReturnsFalse() {
        ExcludedDirectoryCatalog.IsInExcludedDirectory("").Should().BeFalse();
    }

    [Fact]
    public void IsInExcludedDirectory_OnlyExcludedName_ReturnsTrue() {
        ExcludedDirectoryCatalog.IsInExcludedDirectory("bin").Should().BeTrue();
        ExcludedDirectoryCatalog.IsInExcludedDirectory("obj").Should().BeTrue();
        ExcludedDirectoryCatalog.IsInExcludedDirectory(".git").Should().BeTrue();
        ExcludedDirectoryCatalog.IsInExcludedDirectory(".x").Should().BeTrue();
    }

    #endregion
}
