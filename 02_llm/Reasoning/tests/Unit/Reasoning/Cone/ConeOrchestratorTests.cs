namespace JoinCode.Reasoning.Tests.Cone;

public sealed class ConeOrchestratorTests
{
    [Fact]
    public void RegisterRole_ShouldCreateCone()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 3);

        var cone = orchestrator.GetRole(AgentRole.Prosecutor);

        Assert.NotNull(cone);
        Assert.Equal(3, cone.MaxVisibleFragments);
    }

    [Fact]
    public void TransferFragment_ShouldCreateCopyWithDecay()
    {
        var orchestrator = new ConeOrchestrator { TransferDecayFactor = 0.9 };
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        orchestrator.RegisterRole(AgentRole.Defender, 5);

        var prosCone = orchestrator.GetRole(AgentRole.Prosecutor)!;
        var fragment = new ObservationFragment
        {
            FragmentId = "f1",
            SourceItemId = "item1",
            RoleChain = AgentRole.Prosecutor,
            RawText = "test evidence",
            Fingerprint = new CognitiveFingerprint
            {
                EntryStimulus = "test",
                ProcessingPath = "test",
                OutputConclusion = "conclusion",
                Confidence = 0.8,
            },
            FoldedSummary = "[摘要] conclusion",
            ExpandCondition = "cross_role_review",
        };
        prosCone.AddFragment(fragment);

        var transferred = orchestrator.TransferFragment(AgentRole.Prosecutor, AgentRole.Defender, "f1");

        Assert.NotNull(transferred);
        Assert.Equal(AgentRole.Defender, transferred.RoleChain);
        Assert.InRange(transferred.Fingerprint.Confidence, 0.8 * 0.9 - 0.01, 0.8 * 0.9 + 0.01);
        Assert.Contains("f1", transferred.BackReferences);
    }

    [Fact]
    public void DetectConeConflict_ShouldReturnConflictResult()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        orchestrator.RegisterRole(AgentRole.Defender, 5);

        var prosCone = orchestrator.GetRole(AgentRole.Prosecutor)!;
        prosCone.AddFragment(new ObservationFragment
        {
            FragmentId = "pf1",
            SourceItemId = "item1",
            RoleChain = AgentRole.Prosecutor,
            Fingerprint = new CognitiveFingerprint { OutputConclusion = "guilty", Confidence = 0.8 },
            FoldedSummary = "[摘要] guilty",
        });

        var defCone = orchestrator.GetRole(AgentRole.Defender)!;
        defCone.AddFragment(new ObservationFragment
        {
            FragmentId = "df1",
            SourceItemId = "item2",
            RoleChain = AgentRole.Defender,
            Fingerprint = new CognitiveFingerprint { OutputConclusion = "innocent", Confidence = 0.7 },
            FoldedSummary = "[摘要] innocent",
        });

        var result = orchestrator.DetectConeConflict(AgentRole.Prosecutor, AgentRole.Defender);

        Assert.True(result.HasConflict);
        Assert.Contains("guilty", result.RoleAConclusions);
        Assert.Contains("innocent", result.RoleBConclusions);
    }

    [Fact]
    public void CreateFragmentFromItem_ShouldMapDataItemFields()
    {
        var orchestrator = new ConeOrchestrator();
        var item = new DataItem { Content = "测试假定", State = DataState.Assumption, Confidence = 80 };

        var fragment = orchestrator.CreateFragmentFromItem(AgentRole.Judge, item);

        Assert.Equal(item.Id, fragment.SourceItemId);
        Assert.Equal(AgentRole.Judge, fragment.RoleChain);
        Assert.Equal("测试假定", fragment.RawText);
        Assert.InRange(fragment.Fingerprint.Confidence, 0.79, 0.81);
    }

    [Fact]
    public void CreateFragmentFromEvidence_ShouldMapEvidenceFields()
    {
        var orchestrator = new ConeOrchestrator();
        var evidence = new EvidenceRecord
        {
            Content = "DNA匹配",
            Category = EvidenceCategory.Physical,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Prosecutor,
        };

        var fragment = orchestrator.CreateFragmentFromEvidence(AgentRole.Prosecutor, evidence);

        Assert.Equal(evidence.Id, fragment.SourceItemId);
        Assert.Equal(AgentRole.Prosecutor, fragment.RoleChain);
        Assert.Equal("DNA匹配", fragment.RawText);
    }
}
