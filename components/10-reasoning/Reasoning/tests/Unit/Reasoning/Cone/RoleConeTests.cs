namespace JoinCode.Reasoning.Tests.Cone;

public sealed class RoleConeTests
{
    [Fact]
    public void AddFragment_ShouldIncreaseActiveCount()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        var fragment = CreateFragment("frag1", AgentRole.Prosecutor);

        cone.AddFragment(fragment);

        Assert.Single(cone.ActiveFragmentIds);
        Assert.Single(cone.AllFragments);
    }

    [Fact]
    public void AddFragment_ShouldFoldWhenWindowExceeds()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 2 };

        cone.AddFragment(CreateFragment("f1", AgentRole.Prosecutor));
        cone.AddFragment(CreateFragment("f2", AgentRole.Prosecutor));
        cone.AddFragment(CreateFragment("f3", AgentRole.Prosecutor));

        Assert.Equal(2, cone.ActiveFragmentIds.Count);
        Assert.Equal(3, cone.AllFragments.Count);
        Assert.False(cone.AllFragments["f1"].IsExpanded);
    }

    [Fact]
    public void ExpandFragment_ShouldExpandOnMatchingCondition()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 2 };
        var f1 = CreateFragment("f1", AgentRole.Prosecutor, expandCondition: "cross_role_review");
        cone.AddFragment(f1);
        cone.AddFragment(CreateFragment("f2", AgentRole.Prosecutor));
        cone.AddFragment(CreateFragment("f3", AgentRole.Prosecutor));

        var expanded = cone.ExpandFragment("f1", "cross_role_review");

        Assert.NotNull(expanded);
        Assert.True(expanded.IsExpanded);
        Assert.Contains("f1", cone.ActiveFragmentIds);
    }

    [Fact]
    public void ExpandFragment_ShouldReturnNullOnNonMatchingCondition()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        var f1 = CreateFragment("f1", AgentRole.Prosecutor, expandCondition: "specific_trigger");
        cone.AddFragment(f1);

        var result = cone.ExpandFragment("f1", "wrong_trigger");

        Assert.Null(result);
    }

    [Fact]
    public void ExpandFragment_WildcardShouldAlwaysExpand()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        var f1 = CreateFragment("f1", AgentRole.Prosecutor, expandCondition: "specific_trigger");
        cone.AddFragment(f1);
        f1.IsExpanded = false;

        var result = cone.ExpandFragment("f1", "*");

        Assert.NotNull(result);
        Assert.True(result.IsExpanded);
    }

    [Fact]
    public void GetConeContext_ShouldReturnVisibleFragmentsText()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        cone.AddFragment(CreateFragment("f1", AgentRole.Prosecutor));

        var context = cone.GetConeContext();

        Assert.Contains("f1", context);
        Assert.Contains("Prosecutor", context);
    }

    [Fact]
    public void GetActiveConclusions_ShouldReturnConclusionsForActiveFragments()
    {
        var cone = new RoleCone { RoleName = AgentRole.Prosecutor, MaxVisibleFragments = 5 };
        cone.AddFragment(CreateFragment("f1", AgentRole.Prosecutor, conclusion: "test conclusion"));

        var conclusions = cone.GetActiveConclusions();

        Assert.Single(conclusions);
        Assert.Equal("test conclusion", conclusions[0]);
    }

    private static ObservationFragment CreateFragment(
        string id, AgentRole role, string conclusion = "test", string expandCondition = "cross_role_review")
    {
        return new ObservationFragment
        {
            FragmentId = id,
            SourceItemId = id,
            RoleChain = role,
            RawText = $"raw text for {id}",
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
