namespace Core.Goal;

using System.Collections.Frozen;
using JoinCode.Abstractions.Models.Goal;
using Structura.Dag;

/// <summary>
/// 预定义 Graph 模板 — 重构、修bug、调研报告
/// </summary>
public static class GoalGraphTemplates
{
    public static void RegisterAll(IGoalGraphTemplateRegistry registry)
    {
        registry.Register(RefactorTemplate);
        registry.Register(BugFixTemplate);
        registry.Register(ResearchTemplate);
    }

    /// <summary>
    /// 重构模板：explore → implement → review → {PASS: commit, FAIL: implement}
    /// </summary>
    public static GoalGraphTemplate RefactorTemplate => new()
    {
        Name = "refactor",
        Keywords = ["重构", "refactor", "重写", "rewrite", "优化", "optimize", "迁移", "migrate"],
        Description = "代码重构流水线：探索→实现→评审→提交/回退",
        BuildGraph = BuildRefactorGraph,
    };

    /// <summary>
    /// 修bug模板：reproduce → locate → fix → verify → {PASS: done, FAIL: fix}
    /// </summary>
    public static GoalGraphTemplate BugFixTemplate => new()
    {
        Name = "bugfix",
        Keywords = ["修复", "fix", "bug", "缺陷", "defect", "解决", "resolve", "调试", "debug"],
        Description = "Bug修复流水线：复现→定位→修复→验证→完成/回退",
        BuildGraph = BuildBugFixGraph,
    };

    /// <summary>
    /// 调研报告模板：research_A ∥ research_B → gather(Join) → synthesize → review
    /// </summary>
    public static GoalGraphTemplate ResearchTemplate => new()
    {
        Name = "research",
        Keywords = ["调研", "research", "分析", "analyze", "报告", "report", "调查", "investigate"],
        Description = "调研报告：并行研究→汇聚→综合→评审",
        BuildGraph = BuildResearchGraph,
    };

    private static GoalGraph BuildRefactorGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "explore", Payload = new() { Kind = GoalNodeKind.Agent, Name = "explorer", SystemPrompt = "You are a code exploration expert. Analyze module structure and identify refactoring opportunities.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "implement", Payload = new() { Kind = GoalNodeKind.Agent, Name = "implementer", SystemPrompt = "You are a code implementation expert. Execute refactoring based on analysis.", Instruction = "根据分析结果执行重构" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "review", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reviewer", SystemPrompt = "You are an independent code reviewer. Evaluate the refactoring objectively without assuming context. Check for correctness, completeness, and potential issues.", Instruction = "评审重构结果，确认正确性和完整性", FreshContext = true } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "commit", Payload = new() { Kind = GoalNodeKind.Agent, Name = "committer", SystemPrompt = "You are a commit expert. Create a clear, descriptive commit.", Instruction = "提交重构结果" } });

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "explore", ToId = "implement" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "implement", ToId = "review" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "review", ToId = "commit", Label = "PASS" });
        const string backEdge = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "review", ToId = "implement", Label = "FAIL" });
        dag.Nodes["implement"].InEdgeIds.Remove(backEdge);

        return new GoalGraph { Name = $"refactor: {objective}", Dag = dag, StartNodeId = "explore", EndNodeIds = FrozenSet.Create("commit") };
    }

    private static GoalGraph BuildBugFixGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "reproduce", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reproducer", SystemPrompt = "You are a bug reproduction expert. Create a minimal test case that reproduces the bug.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "locate", Payload = new() { Kind = GoalNodeKind.Agent, Name = "locator", SystemPrompt = "You are a root cause analysis expert. Find the exact location and cause of the bug.", Instruction = "根据复现结果定位根因" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "fix", Payload = new() { Kind = GoalNodeKind.Agent, Name = "fixer", SystemPrompt = "You are a bug fix expert. Implement a minimal, correct fix.", Instruction = "根据根因分析修复bug" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "verify", Payload = new() { Kind = GoalNodeKind.Agent, Name = "verifier", SystemPrompt = "You are an independent verifier. Confirm the bug is fixed without assuming context. Run tests and check the fix.", Instruction = "独立验证bug已修复，运行测试", FreshContext = true } });

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "reproduce", ToId = "locate" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "locate", ToId = "fix" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "fix", ToId = "verify" });
        const string backEdge = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "verify", ToId = "fix", Label = "FAIL" });
        dag.Nodes["fix"].InEdgeIds.Remove(backEdge);

        return new GoalGraph { Name = $"bugfix: {objective}", Dag = dag, StartNodeId = "reproduce", EndNodeIds = FrozenSet.Create("verify") };
    }

    private static GoalGraph BuildResearchGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "start", Payload = new() { Kind = GoalNodeKind.Function, Name = "start", Instruction = "开始调研" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "research_a", Payload = new() { Kind = GoalNodeKind.Agent, Name = "researcher-a", SystemPrompt = "You are a research expert. Investigate one aspect of the topic thoroughly.", Instruction = $"从技术实现角度调研: {objective}" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "research_b", Payload = new() { Kind = GoalNodeKind.Agent, Name = "researcher-b", SystemPrompt = "You are a research expert. Investigate one aspect of the topic thoroughly.", Instruction = $"从行业实践和替代方案角度调研: {objective}" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "gather", Payload = new() { Kind = GoalNodeKind.Join, Name = "gatherer" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "synthesize", Payload = new() { Kind = GoalNodeKind.Agent, Name = "synthesizer", SystemPrompt = "You are a report synthesis expert. Combine research findings into a coherent, comprehensive report.", Instruction = "综合调研结果，撰写完整报告" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "review", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reviewer", SystemPrompt = "You are an independent report reviewer. Evaluate the report quality objectively.", Instruction = "评审调研报告的完整性和准确性", FreshContext = true } });

        dag.AddEdge(new DagEdge { Id = "e0a", FromId = "start", ToId = "research_a" });
        dag.AddEdge(new DagEdge { Id = "e0b", FromId = "start", ToId = "research_b" });
        dag.AddEdge(new DagEdge { Id = "e1", FromId = "research_a", ToId = "gather" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "research_b", ToId = "gather" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "gather", ToId = "synthesize" });
        dag.AddEdge(new DagEdge { Id = "e4", FromId = "synthesize", ToId = "review" });

        engine.RegisterFunction("start", _ =>
            Task.FromResult(NodeResult.Succeeded("research-started")));

        return new GoalGraph { Name = $"research: {objective}", Dag = dag, StartNodeId = "start", EndNodeIds = FrozenSet.Create("review") };
    }
}
