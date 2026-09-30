namespace Abs.Tests.Constants;

/// <summary>
/// VcsDirectoryExclusions 确定性测试 — 验证 VCS 目录单数据源、glob 模式派生、SecurityPatterns 委托一致性
/// 单数据源+委托消费:VcsDirectoryExclusions.GlobPatterns 是唯一数据源,SecurityPatterns.VcsInternal 委托它
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class VcsDirectoryExclusionsTests {
    #region Names

    [Fact]
    public void Names_Contains6VcsDirs() {
        VcsDirectoryExclusions.Names.Should().HaveCount(6);
        VcsDirectoryExclusions.Names.Should()
            .Contain(".git").And.Contain(".svn").And.Contain(".hg")
            .And.Contain(".bzr").And.Contain(".jj").And.Contain(".sl");
    }

    #endregion

    #region GlobPatterns

    [Fact]
    public void GlobPatterns_Contains12Patterns() {
        // 6 目录 + 6 glob = 12
        VcsDirectoryExclusions.GlobPatterns.Should().HaveCount(12);
    }

    [Fact]
    public void GlobPatterns_ContainsDirAndGlobForEachVcs() {
        foreach (var dir in VcsDirectoryExclusions.Names) {
            VcsDirectoryExclusions.GlobPatterns.Should().Contain(dir, $"dir pattern {dir} should exist");
            VcsDirectoryExclusions.GlobPatterns.Should().Contain(dir + "/**", $"glob pattern {dir}/** should exist");
        }
    }

    [Fact]
    public void GlobPatterns_CaseInsensitive() {
        VcsDirectoryExclusions.GlobPatterns.Should().Contain(".GIT");
        VcsDirectoryExclusions.GlobPatterns.Should().Contain(".Git/**");
    }

    #endregion

    #region SecurityPatterns 委托一致性

    [Fact]
    public void SecurityPatterns_VcsInternal_DelegatesToGlobPatterns() {
        // 验证 SecurityPatterns.VcsInternal 委托 VcsDirectoryExclusions.GlobPatterns
        var vcsInternalPatterns = SecurityPatterns.GetPatternsForCategory(SensitiveFilePattern.VcsInternal);
        vcsInternalPatterns.Should().HaveCount(VcsDirectoryExclusions.GlobPatterns.Count);
        foreach (var p in vcsInternalPatterns) {
            VcsDirectoryExclusions.GlobPatterns.Should().Contain(p);
        }
    }

    [Fact]
    public void SecurityPatterns_VcsInternal_ContainsAll6VcsDirs() {
        // 扩展验证:原 3 个(.git/.svn/.hg) → 6 个(补全 .bzr/.jj/.sl)
        var vcsInternalPatterns = SecurityPatterns.GetPatternsForCategory(SensitiveFilePattern.VcsInternal);
        vcsInternalPatterns.Should().Contain(".git").And.Contain(".svn").And.Contain(".hg");
        vcsInternalPatterns.Should().Contain(".bzr").And.Contain(".jj").And.Contain(".sl");
    }

    [Fact]
    public void SecurityPatterns_MatchesVcsInternalPath_SupportsAll6Vcs() {
        // 验证 MatchesVcsInternalPath 支持所有 6 个 VCS 目录(委托 VcsInternalPathSegments 派生)
        SecurityPatterns.MatchesVcsInternalPath("src/.git/config").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath("src/.svn/wc.db").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath("src/.hg/store").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath("src/.bzr/branch").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath("src/.jj/conf").Should().BeTrue();
        SecurityPatterns.MatchesVcsInternalPath("src/.sl/state").Should().BeTrue();
    }

    [Fact]
    public void SecurityPatterns_MatchesVcsInternalPath_RejectsNonVcs() {
        SecurityPatterns.MatchesVcsInternalPath("src/core/file.cs").Should().BeFalse();
        SecurityPatterns.MatchesVcsInternalPath("src/bin/output.dll").Should().BeFalse();
        SecurityPatterns.MatchesVcsInternalPath("").Should().BeFalse();
        SecurityPatterns.MatchesVcsInternalPath(null).Should().BeFalse();
    }

    #endregion

    #region IsVcsPath

    [Fact]
    public void IsVcsPath_DetectsVcsSegments() {
        VcsDirectoryExclusions.IsVcsPath(".git/HEAD").Should().BeTrue();
        VcsDirectoryExclusions.IsVcsPath("src/.git/config").Should().BeTrue();
        VcsDirectoryExclusions.IsVcsPath("src/.svn/wc.db").Should().BeTrue();
        VcsDirectoryExclusions.IsVcsPath("src/.hg/store").Should().BeTrue();
        VcsDirectoryExclusions.IsVcsPath("src/.bzr/branch").Should().BeTrue();
        VcsDirectoryExclusions.IsVcsPath("src/.jj/conf").Should().BeTrue();
        VcsDirectoryExclusions.IsVcsPath("src/.sl/state").Should().BeTrue();
    }

    [Fact]
    public void IsVcsPath_RejectsNonVcsPath() {
        VcsDirectoryExclusions.IsVcsPath("src/core/file.cs").Should().BeFalse();
        VcsDirectoryExclusions.IsVcsPath("src/bin/output.dll").Should().BeFalse();
    }

    #endregion
}
