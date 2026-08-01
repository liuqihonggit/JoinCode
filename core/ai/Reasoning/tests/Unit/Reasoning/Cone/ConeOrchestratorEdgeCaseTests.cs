namespace JoinCode.Reasoning.Tests.Cone;

public sealed class ConeOrchestratorEdgeCaseTests
{
    [Fact]
    public void TransferFragment_WhenFromRoleNotRegistered_ReturnsNull()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Defender, 5);

        var result = orchestrator.TransferFragment(AgentRole.Prosecutor, AgentRole.Defender, "f1");

        Assert.Null(result);
    }

    [Fact]
    public void TransferFragment_WhenToRoleNotRegistered_ReturnsNull()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        var cone = orchestrator.GetRole(AgentRole.Prosecutor)!;
        cone.AddFragment(CreateFragment("f1", AgentRole.Prosecutor));

        var result = orchestrator.TransferFragment(AgentRole.Prosecutor, AgentRole.Defender, "f1");

        Assert.Null(result);
    }

    [Fact]
    public void TransferFragment_WhenFragmentNotFound_ReturnsNull()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        orchestrator.RegisterRole(AgentRole.Defender, 5);

        var result = orchestrator.TransferFragment(AgentRole.Prosecutor, AgentRole.Defender, "missing");

        Assert.Null(result);
    }

    [Fact]
    public void TransferFragment_AppliesDecayFactor()
    {
        var orchestrator = new ConeOrchestrator { TransferDecayFactor = 0.5 };
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        orchestrator.RegisterRole(AgentRole.Defender, 5);
        var cone = orchestrator.GetRole(AgentRole.Prosecutor)!;
        cone.AddFragment(new ObservationFragment
        {
            FragmentId = "f1",
            SourceItemId = "item1",
            RoleChain = AgentRole.Prosecutor,
            RawText = "test",
            Fingerprint = new CognitiveFingerprint
            {
                EntryStimulus = "test",
                ProcessingPath = "test",
                OutputConclusion = "conclusion",
                Confidence = 1.0,
            },
            FoldedSummary = "[摘要] conclusion",
            ExpandCondition = "cross_role_review",
        });

        var result = orchestrator.TransferFragment(AgentRole.Prosecutor, AgentRole.Defender, "f1");

        Assert.NotNull(result);
        Assert.Equal(AgentRole.Defender, result.RoleChain);
        Assert.Equal(0.5, result.Fingerprint.Confidence, precision: 2);
        Assert.Contains("Prosecutor", result.FoldedSummary);
        Assert.Contains("f1", result.BackReferences);
    }

    [Fact]
    public void DetectConeConflict_WhenRoleANotRegistered_ReturnsNoConflict()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Defender, 5);

        var result = orchestrator.DetectConeConflict(AgentRole.Prosecutor, AgentRole.Defender);

        Assert.False(result.HasConflict);
        Assert.Equal(AgentRole.Prosecutor, result.RoleA);
        Assert.Equal(AgentRole.Defender, result.RoleB);
    }

    [Fact]
    public void DetectConeConflict_WhenNoActiveConclusions_ReturnsNoConflict()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        orchestrator.RegisterRole(AgentRole.Defender, 5);

        var result = orchestrator.DetectConeConflict(AgentRole.Prosecutor, AgentRole.Defender);

        Assert.False(result.HasConflict);
    }

    [Fact]
    public void GetAllRoles_ReturnsRegisteredRoles()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        orchestrator.RegisterRole(AgentRole.Judge, 6);

        var roles = orchestrator.GetAllRoles();

        Assert.Equal(2, roles.Count);
        Assert.Equal(5, roles[AgentRole.Prosecutor].MaxVisibleFragments);
        Assert.Equal(6, roles[AgentRole.Judge].MaxVisibleFragments);
    }

    [Fact]
    public void GetRole_WhenNotRegistered_ReturnsNull()
    {
        var orchestrator = new ConeOrchestrator();

        var result = orchestrator.GetRole(AgentRole.Prosecutor);

        Assert.Null(result);
    }

    [Fact]
    public void CreateFragmentFromItem_WithCustomEntryStimulus_UsesProvidedValue()
    {
        var orchestrator = new ConeOrchestrator();
        var item = new DataItem { Content = "内容", State = DataState.Assumption, Confidence = 80 };

        var fragment = orchestrator.CreateFragmentFromItem(AgentRole.Prosecutor, item, "custom stimulus");

        Assert.Equal("custom stimulus", fragment.Fingerprint.EntryStimulus);
    }

    [Fact]
    public void CreateFragmentFromEvidence_WithDirectEvidence_MapsHighConfidence()
    {
        var orchestrator = new ConeOrchestrator();
        var evidence = new EvidenceRecord
        {
            Content = "DNA",
            Category = EvidenceCategory.Physical,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Prosecutor,
        };

        var fragment = orchestrator.CreateFragmentFromEvidence(AgentRole.Prosecutor, evidence);

        Assert.Equal(evidence.Id, fragment.SourceItemId);
        Assert.Equal(1.0, fragment.Fingerprint.Confidence, precision: 2);
    }

    private static ObservationFragment CreateFragment(string id, AgentRole role)
    {
        return new ObservationFragment
        {
            FragmentId = id,
            SourceItemId = id,
            RoleChain = role,
            RawText = "test",
            Fingerprint = new CognitiveFingerprint
            {
                EntryStimulus = "test",
                ProcessingPath = "test",
                OutputConclusion = "conclusion",
                Confidence = 0.7,
            },
            FoldedSummary = "[摘要] conclusion",
            ExpandCondition = "cross_role_review",
        };
    }
}
