namespace Core.Goal;

using System.Collections.Frozen;
using JoinCode.Abstractions.Models.Goal;
using Structura.Dag;

/// <summary>
/// 预定义 Graph 模板 — 重构、修bug、调研报告、代码审查、测试生成
/// </summary>
public static class GoalGraphTemplates
{
    public static void RegisterAll(IGoalGraphTemplateRegistry registry)
    {
        registry.Register(RefactorTemplate);
        registry.Register(BugFixTemplate);
        registry.Register(ResearchTemplate);
        registry.Register(CodeReviewTemplate);
        registry.Register(TestGenTemplate);
    }

    public static GoalGraphTemplate RefactorTemplate => new()
    {
        Name = "refactor",
        Keywords = ["重构", "refactor", "重写", "rewrite", "优化", "optimize", "迁移", "migrate"],
        Description = "explore → implement → review → {PASS: commit, FAIL: implement}",
        BuildGraph = BuildRefactorGraph,
    };

    public static GoalGraphTemplate BugFixTemplate => new()
    {
        Name = "bugfix",
        Keywords = ["修复", "fix", "bug", "缺陷", "defect", "解决", "resolve", "调试", "debug"],
        Description = "reproduce → locate → fix → verify → {PASS: done, FAIL: fix}",
        BuildGraph = BuildBugFixGraph,
    };

    public static GoalGraphTemplate ResearchTemplate => new()
    {
        Name = "research",
        Keywords = ["调研", "research", "分析", "analyze", "报告", "report", "调查", "investigate"],
        Description = "start → [research_a ∥ research_b] → gather → synthesize → review",
        BuildGraph = BuildResearchGraph,
    };

    public static GoalGraphTemplate CodeReviewTemplate => new()
    {
        Name = "code_review",
        Keywords = ["审查", "review", "评审", "code review", "检查", "inspect", "审计", "audit"],
        Description = "read → analyze → {PASS: approve, FAIL: suggest_fixes}",
        BuildGraph = BuildCodeReviewGraph,
    };

    public static GoalGraphTemplate TestGenTemplate => new()
    {
        Name = "test_gen",
        Keywords = ["测试", "test", "单测", "unit test", "覆盖率", "coverage", "tdd"],
        Description = "analyze → write_tests → run_tests → {PASS: done, FAIL: write_tests}",
        BuildGraph = BuildTestGenGraph,
    };

    private static GoalGraph BuildRefactorGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "explore", Payload = new() { Kind = GoalNodeKind.Agent, Name = "explorer", SystemPrompt = "You are a code exploration expert. Analyze module structure, identify refactoring opportunities, and create a detailed refactoring plan.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "implement", Payload = new() { Kind = GoalNodeKind.Agent, Name = "implementer", SystemPrompt = "You are a code implementation expert. Execute refactoring step by step based on the analysis. Compile after each change to verify correctness.", Instruction = "Execute the refactoring based on the exploration results. Compile and fix any errors." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "review", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reviewer", SystemPrompt = "You are an independent code reviewer. Evaluate the refactoring WITHOUT assuming context from the implementation. Check: correctness, no regressions, code quality, completeness.", Instruction = "Review the refactoring result objectively. Verify correctness and completeness.", FreshContext = true } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "commit", Payload = new() { Kind = GoalNodeKind.Agent, Name = "committer", SystemPrompt = "You are a commit expert. Create a clear, descriptive commit message and commit the changes.", Instruction = "Commit the refactoring result with a descriptive message." } });

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

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "reproduce", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reproducer", SystemPrompt = "You are a bug reproduction expert. Create a minimal test case that reliably reproduces the bug.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "locate", Payload = new() { Kind = GoalNodeKind.Agent, Name = "locator", SystemPrompt = "You are a root cause analysis expert. Trace the bug from the reproduction to its exact source. Identify the specific code location and the mechanism causing the bug.", Instruction = "Locate the root cause based on the reproduction results." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "fix", Payload = new() { Kind = GoalNodeKind.Agent, Name = "fixer", SystemPrompt = "You are a bug fix expert. Implement a minimal, correct fix that addresses the root cause without introducing side effects.", Instruction = "Fix the bug based on the root cause analysis. Make minimal changes." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "verify", Payload = new() { Kind = GoalNodeKind.Agent, Name = "verifier", SystemPrompt = "You are an independent verifier. Confirm the bug is fixed WITHOUT assuming context. Run the reproduction test and all related tests.", Instruction = "Independently verify the bug is fixed. Run tests and check the fix.", FreshContext = true } });

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

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "start", Payload = new() { Kind = GoalNodeKind.Function, Name = "start", Instruction = "Start research" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "research_a", Payload = new() { Kind = GoalNodeKind.Agent, Name = "researcher-technical", SystemPrompt = "You are a research expert. Investigate technical implementation aspects thoroughly.", Instruction = $"Research technical implementation aspects: {objective}" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "research_b", Payload = new() { Kind = GoalNodeKind.Agent, Name = "researcher-alternatives", SystemPrompt = "You are a research expert. Investigate industry practices and alternative approaches thoroughly.", Instruction = $"Research industry practices and alternatives: {objective}" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "gather", Payload = new() { Kind = GoalNodeKind.Join, Name = "gatherer" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "synthesize", Payload = new() { Kind = GoalNodeKind.Agent, Name = "synthesizer", SystemPrompt = "You are a report synthesis expert. Combine research findings into a coherent, comprehensive report with clear conclusions and recommendations.", Instruction = "Synthesize all research findings into a comprehensive report." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "review", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reviewer", SystemPrompt = "You are an independent report reviewer. Evaluate the report quality objectively: completeness, accuracy, clarity, and actionable recommendations.", Instruction = "Review the research report for completeness and accuracy.", FreshContext = true } });

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

    private static GoalGraph BuildCodeReviewGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "read", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reader", SystemPrompt = "You are a code reading expert. Thoroughly read and understand the code changes, identifying all modified files, functions, and logic.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "analyze", Payload = new() { Kind = GoalNodeKind.Agent, Name = "analyzer", SystemPrompt = "You are an independent code review analyst. Evaluate code WITHOUT assuming context from the author. Check: correctness, security, performance, maintainability, error handling, edge cases.", Instruction = "Independently analyze the code for issues. Be thorough and objective.", FreshContext = true } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "approve", Payload = new() { Kind = GoalNodeKind.Agent, Name = "approver", SystemPrompt = "You are a review approver. The code has passed review. Summarize the review conclusion.", Instruction = "Summarize the approved review with key findings." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "suggest_fixes", Payload = new() { Kind = GoalNodeKind.Agent, Name = "fix-suggester", SystemPrompt = "You are a code improvement expert. Based on the review findings, suggest specific fixes and improvements.", Instruction = "Suggest specific fixes based on the review findings." } });

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "read", ToId = "analyze" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "analyze", ToId = "approve", Label = "PASS" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "analyze", ToId = "suggest_fixes", Label = "FAIL" });

        return new GoalGraph { Name = $"code-review: {objective}", Dag = dag, StartNodeId = "read", EndNodeIds = FrozenSet.Create("approve", "suggest_fixes") };
    }

    private static GoalGraph BuildTestGenGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "analyze", Payload = new() { Kind = GoalNodeKind.Agent, Name = "analyzer", SystemPrompt = "You are a test analysis expert. Analyze the code to identify all testable behaviors, edge cases, and error paths.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "write_tests", Payload = new() { Kind = GoalNodeKind.Agent, Name = "test-writer", SystemPrompt = "You are a test writing expert. Write comprehensive unit tests covering all identified behaviors. Follow TDD principles: arrange-act-assert, one assertion per concept.", Instruction = "Write comprehensive unit tests based on the analysis." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "run_tests", Payload = new() { Kind = GoalNodeKind.Agent, Name = "test-runner", SystemPrompt = "You are an independent test verifier. Run the tests and verify they pass WITHOUT assuming context. Check coverage and correctness.", Instruction = "Run all tests and verify they pass. Check coverage.", FreshContext = true } });

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "analyze", ToId = "write_tests" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "write_tests", ToId = "run_tests" });
        const string backEdge = "e3";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "run_tests", ToId = "write_tests", Label = "FAIL" });
        dag.Nodes["write_tests"].InEdgeIds.Remove(backEdge);

        return new GoalGraph { Name = $"test-gen: {objective}", Dag = dag, StartNodeId = "analyze", EndNodeIds = FrozenSet.Create("run_tests") };
    }
}
