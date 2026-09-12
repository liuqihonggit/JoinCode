namespace JoinCode.Reasoning.Tests.Cone;

public sealed class RoleConeEdgeCaseTests
{
    [Fact]
    public void FoldOldestFragment_WhenActiveFragmentIdsIsEmpty_DoesNothing()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };

        cone.FoldOldestFragment();

        Assert.Empty(cone.ActiveFragmentIds);
        Assert.Empty(cone.AllFragments);
    }

    [Fact]
    public void ExpandFragment_WhenFragmentNotFound_ReturnsNull()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };

        var result = cone.ExpandFragment("missing", "*");

        Assert.Null(result);
    }

    [Fact]
    public void ExpandFragment_WhenExpandConditionIsEmpty_AlwaysExpands()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        var fragment = CreateFragment("f1", AgentRole.Prosecutor, expandCondition: string.Empty);
        cone.AddFragment(fragment);
        fragment.IsExpanded = false;
        cone.ActiveFragmentIds.Clear();

        var result = cone.ExpandFragment("f1", "any");

        Assert.NotNull(result);
        Assert.True(result.IsExpanded);
        Assert.Contains("f1", cone.ActiveFragmentIds);
    }

    [Fact]
    public void ExpandFragment_WhenAlreadyActive_KeepsInActiveList()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        var fragment = CreateFragment("f1", AgentRole.Prosecutor);
        cone.AddFragment(fragment);

        var result = cone.ExpandFragment("f1", "cross_role_review");

        Assert.NotNull(result);
        Assert.Single(cone.ActiveFragmentIds);
    }

    [Fact]
    public void GetConeContext_ShouldTruncateLongRawText()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        var fragment = CreateFragment("f1", AgentRole.Prosecutor, rawText: new string('x', 100));
        cone.AddFragment(fragment);

        var context = cone.GetConeContext();

        Assert.Contains("...", context);
        Assert.DoesNotContain(new string('x', 60), context);
    }

    [Fact]
    public void GetConeContext_ShouldNotTruncateShortRawText()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        var fragment = CreateFragment("f1", AgentRole.Prosecutor, rawText: "short");
        cone.AddFragment(fragment);

        var context = cone.GetConeContext();

        Assert.Contains("short", context);
        Assert.DoesNotContain("...", context);
    }

    [Fact]
    public void GetActiveConclusions_WhenActiveIdNotInAllFragments_ReturnsEmpty()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        cone.ActiveFragmentIds.Add("missing");

        var conclusions = cone.GetActiveConclusions();

        Assert.Empty(conclusions);
    }

    [Fact]
    public void AddFragment_SetsIncrementalLoadOrder()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };

        cone.AddFragment(CreateFragment("f1", AgentRole.Prosecutor));
        cone.AddFragment(CreateFragment("f2", AgentRole.Prosecutor));

        Assert.Equal(1, cone.AllFragments["f1"].LoadOrder);
        Assert.Equal(2, cone.AllFragments["f2"].LoadOrder);
    }

    [Fact]
    public void FoldOldestFragment_SetsFoldedSummary()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 1 };
        var fragment = CreateFragment("f1", AgentRole.Prosecutor, conclusion: "结论");
        cone.AddFragment(fragment);
        cone.AddFragment(CreateFragment("f2", AgentRole.Prosecutor));

        Assert.Contains("[折叠]", cone.AllFragments["f1"].FoldedSummary);
        Assert.Contains("结论", cone.AllFragments["f1"].FoldedSummary);
    }

    [Fact]
    public void GetConeContext_IncludesOnlyVisibleFragments()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 2 };
        var f1 = CreateFragment("f1", AgentRole.Prosecutor, conclusion: "old");
        var f2 = CreateFragment("f2", AgentRole.Prosecutor, conclusion: "active1");
        var f3 = CreateFragment("f3", AgentRole.Prosecutor, conclusion: "active2");
        cone.AddFragment(f1);
        cone.AddFragment(f2);
        cone.AddFragment(f3);

        var context = cone.GetConeContext();

        Assert.DoesNotContain("old", context);
        Assert.Contains("active1", context);
        Assert.Contains("active2", context);
    }

    private static ObservationFragment CreateFragment(
        string id, AgentRole role, string conclusion = "test", string expandCondition = "cross_role_review", string rawText = "")
    {
        return new ObservationFragment
        {
            FragmentId = id,
            SourceItemId = id,
            RoleChain = role,
            RawText = string.IsNullOrEmpty(rawText) ? $"raw text for {id}" : rawText,
            Fingerprint = new CognitiveFingerprint
            {
                EntryStimulus = $"stimulus for {id}",
                ProcessingPath = "test_path",
                OutputConclusion = conclusion,
                Confidence = 0.7,
            },
            FoldedSummary = $"[摘要] {conclusion}",
            ExpandCondition = expandCondition,
        };
    }
}
