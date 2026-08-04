namespace Core.Goal;

using System.Collections.Frozen;
using JoinCode.Abstractions.Models.Agent;
using JoinCode.Abstractions.Models.Goal;
using JoinCode.Abstractions.Interfaces;
using Core.Hooks.Lifecycle;
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
        registry.Register(NegativeReviewLoopTemplate);
        registry.Register(ClusterTemplate);
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

    /// <summary>
    /// 负向评价循环模板 — execute → neg_review ⟲ fix_neg
    /// 终止条件: 负评≤5→停止 | 6~10→ask_user | token预算耗尽 | 协调者终止 | 16轮硬上限
    /// </summary>
    public static GoalGraphTemplate NegativeReviewLoopTemplate => new()
    {
        Name = "negative_review_loop",
        Keywords = ["负评", "负向评价", "negative review", "质量循环", "quality loop", "迭代改进", "iterative improvement"],
        Description = "execute → neg_review → {NEG_CONTINUE: fix_neg, NEG_STOP: done} ⟲ neg_review",
        BuildGraph = BuildNegativeReviewLoopGraph,
    };


    private static GoalGraph BuildRefactorGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "explore", Payload = new() { Kind = GoalNodeKind.Agent, Name = "explorer", Role = AgentRole.Executor, Variant = ExecutorVariant.Explore, SystemPrompt = "You are a code exploration expert. Analyze module structure, identify refactoring opportunities, and create a detailed refactoring plan.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "implement", Payload = new() { Kind = GoalNodeKind.Agent, Name = "implementer", Role = AgentRole.Executor, Variant = ExecutorVariant.Code, SystemPrompt = "You are a code implementation expert. Execute refactoring step by step based on the analysis. Compile after each change to verify correctness.", Instruction = "Execute the refactoring based on the exploration results. Compile and fix any errors." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "review", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reviewer", Role = AgentRole.Coordinator, SystemPrompt = "You are an independent code reviewer. Evaluate the refactoring WITHOUT assuming context from the implementation. Check: correctness, no regressions, code quality, completeness.", Instruction = "Review the refactoring result objectively. Verify correctness and completeness.", FreshContext = true } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "commit", Payload = new() { Kind = GoalNodeKind.Agent, Name = "committer", Role = AgentRole.Executor, Variant = ExecutorVariant.Code, SystemPrompt = "You are a commit expert. Create a clear, descriptive commit message and commit the changes.", Instruction = "Commit the refactoring result with a descriptive message." } });

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

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "reproduce", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reproducer", Role = AgentRole.Executor, Variant = ExecutorVariant.Code, SystemPrompt = "You are a bug reproduction expert. Create a minimal test case that reliably reproduces the bug.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "locate", Payload = new() { Kind = GoalNodeKind.Agent, Name = "locator", Role = AgentRole.Executor, Variant = ExecutorVariant.Explore, SystemPrompt = "You are a root cause analysis expert. Trace the bug from the reproduction to its exact source. Identify the specific code location and the mechanism causing the bug.", Instruction = "Locate the root cause based on the reproduction results." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "fix", Payload = new() { Kind = GoalNodeKind.Agent, Name = "fixer", Role = AgentRole.Executor, Variant = ExecutorVariant.Code, SystemPrompt = "You are a bug fix expert. Implement a minimal, correct fix that addresses the root cause without introducing side effects.", Instruction = "Fix the bug based on the root cause analysis. Make minimal changes." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "verify", Payload = new() { Kind = GoalNodeKind.Agent, Name = "verifier", Role = AgentRole.Coordinator, SystemPrompt = "You are an independent verifier. Confirm the bug is fixed WITHOUT assuming context. Run the reproduction test and all related tests.", Instruction = "Independently verify the bug is fixed. Run tests and check the fix.", FreshContext = true } });

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
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "research_a", Payload = new() { Kind = GoalNodeKind.Agent, Name = "researcher-technical", Role = AgentRole.Executor, Variant = ExecutorVariant.Explore, SystemPrompt = "You are a research expert. Investigate technical implementation aspects thoroughly.", Instruction = $"Research technical implementation aspects: {objective}" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "research_b", Payload = new() { Kind = GoalNodeKind.Agent, Name = "researcher-alternatives", Role = AgentRole.Executor, Variant = ExecutorVariant.Explore, SystemPrompt = "You are a research expert. Investigate industry practices and alternative approaches thoroughly.", Instruction = $"Research industry practices and alternatives: {objective}" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "gather", Payload = new() { Kind = GoalNodeKind.Join, Name = "gatherer" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "synthesize", Payload = new() { Kind = GoalNodeKind.Agent, Name = "synthesizer", Role = AgentRole.Coordinator, SystemPrompt = "You are a report synthesis expert. Combine research findings into a coherent, comprehensive report with clear conclusions and recommendations.", Instruction = "Synthesize all research findings into a comprehensive report." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "review", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reviewer", Role = AgentRole.Coordinator, SystemPrompt = "You are an independent report reviewer. Evaluate the report quality objectively: completeness, accuracy, clarity, and actionable recommendations.", Instruction = "Review the research report for completeness and accuracy.", FreshContext = true } });

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

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "read", Payload = new() { Kind = GoalNodeKind.Agent, Name = "reader", Role = AgentRole.Executor, Variant = ExecutorVariant.Explore, SystemPrompt = "You are a code reading expert. Thoroughly read and understand the code changes, identifying all modified files, functions, and logic.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "analyze", Payload = new() { Kind = GoalNodeKind.Agent, Name = "analyzer", Role = AgentRole.Coordinator, SystemPrompt = "You are an independent code review analyst. Evaluate code WITHOUT assuming context from the author. Check: correctness, security, performance, maintainability, error handling, edge cases.", Instruction = "Independently analyze the code for issues. Be thorough and objective.", FreshContext = true } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "approve", Payload = new() { Kind = GoalNodeKind.Agent, Name = "approver", Role = AgentRole.Coordinator, SystemPrompt = "You are a review approver. The code has passed review. Summarize the review conclusion.", Instruction = "Summarize the approved review with key findings." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "suggest_fixes", Payload = new() { Kind = GoalNodeKind.Agent, Name = "fix-suggester", Role = AgentRole.Executor, Variant = ExecutorVariant.Code, SystemPrompt = "You are a code improvement expert. Based on the review findings, suggest specific fixes and improvements.", Instruction = "Suggest specific fixes based on the review findings." } });

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "read", ToId = "analyze" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "analyze", ToId = "approve", Label = "PASS" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "analyze", ToId = "suggest_fixes", Label = "FAIL" });

        return new GoalGraph { Name = $"code-review: {objective}", Dag = dag, StartNodeId = "read", EndNodeIds = FrozenSet.Create("approve", "suggest_fixes") };
    }

    private static GoalGraph BuildTestGenGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload> { Id = "analyze", Payload = new() { Kind = GoalNodeKind.Agent, Name = "analyzer", Role = AgentRole.Coordinator, SystemPrompt = "You are a test analysis expert. Analyze the code to identify all testable behaviors, edge cases, and error paths.", Instruction = objective } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "write_tests", Payload = new() { Kind = GoalNodeKind.Agent, Name = "test-writer", Role = AgentRole.Executor, Variant = ExecutorVariant.Code, SystemPrompt = "You are a test writing expert. Write comprehensive unit tests covering all identified behaviors. Follow TDD principles: arrange-act-assert, one assertion per concept.", Instruction = "Write comprehensive unit tests based on the analysis." } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "run_tests", Payload = new() { Kind = GoalNodeKind.Agent, Name = "test-runner", Role = AgentRole.Executor, Variant = ExecutorVariant.Code, SystemPrompt = "You are an independent test verifier. Run the tests and verify they pass WITHOUT assuming context. Check coverage and correctness.", Instruction = "Run all tests and verify they pass. Check coverage.", FreshContext = true } });

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "analyze", ToId = "write_tests" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "write_tests", ToId = "run_tests" });
        const string backEdge = "e3";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "run_tests", ToId = "write_tests", Label = "FAIL" });
        dag.Nodes["write_tests"].InEdgeIds.Remove(backEdge);

        return new GoalGraph { Name = $"test-gen: {objective}", Dag = dag, StartNodeId = "analyze", EndNodeIds = FrozenSet.Create("run_tests") };
    }

    /// <summary>
    /// 构建负向评价循环图: execute → neg_review → {NEG_CONTINUE: fix_neg, NEG_STOP: done}
    /// fix_neg 完成后回退到 neg_review 形成循环
    /// </summary>
    private static GoalGraph BuildNegativeReviewLoopGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload>
        {
            Id = "execute",
            Payload = new()
            {
                Kind = GoalNodeKind.Agent,
                Name = "executor",
                Role = AgentRole.Executor, Variant = ExecutorVariant.Code,
                SystemPrompt = "You are a code execution expert. Complete the task thoroughly and precisely. After completion, summarize what was done.",
                Instruction = objective,
            }
        });

        dag.AddNode(new DagNode<GoalNodePayload>
        {
            Id = "neg_review",
            Payload = new()
            {
                Kind = GoalNodeKind.Agent,
                Name = "negative-reviewer",
                Role = AgentRole.Coordinator,
                FreshContext = true,
                MaxLoopIterations = 16,
                SystemPrompt = BuildNegReviewSystemPrompt(),
                Instruction = BuildNegReviewInstruction(objective),
                RouteMatchMode = RouteMatchMode.ConditionalOnly,
            }
        });

        dag.AddNode(new DagNode<GoalNodePayload>
        {
            Id = "fix_neg",
            Payload = new()
            {
                Kind = GoalNodeKind.Agent,
                Name = "fix-negative-review",
                Role = AgentRole.Executor, Variant = ExecutorVariant.Code,
                SystemPrompt = BuildFixNegSystemPrompt(),
                Instruction = "根据负向评价要求完成任务。完成后决定：\n- 如果你想再经历一轮负向评价以保证工程质量，输出路由 NEG_CONTINUE\n- 如果当前负评超过10条，建议输出路由 NEG_STOP\n- 否则输出路由 NEG_STOP",
                RouteMatchMode = RouteMatchMode.ConditionalOnly,
            }
        });

        dag.AddNode(new DagNode<GoalNodePayload>
        {
            Id = "done",
            Payload = new()
            {
                Kind = GoalNodeKind.Function,
                Name = "loop-done",
                Instruction = "Negative review loop completed",
            }
        });

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "neg_review", ToId = "fix_neg", Label = "NEG_CONTINUE" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });

        const string backEdge = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "fix_neg", ToId = "neg_review", Label = "NEG_CONTINUE" });
        dag.Nodes["neg_review"].InEdgeIds.Remove(backEdge);

        dag.AddEdge(new DagEdge { Id = "e5", FromId = "fix_neg", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("negative-review-loop-completed")));

        return new GoalGraph
        {
            Name = $"negative-review-loop: {objective}",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
            HardMaxLoopIterations = 16,
        };
    }

    private static string BuildNegReviewSystemPrompt()
    {
        return """
你是一个严格的负向评价专家。你的职责是勇敢说出不足，而非赞美。

## 评价清单（必须逐项执行）

1. **代码负向评价** — 找到代码不足：命名、结构、重复、死代码、异常处理、线程安全
2. **功能遗留检查** — 是否有功能没有完成？是否只做了表面功夫？
3. **更优做法搜索** — 通过网络搜索等手段寻找更适合的做法，包括：
   - 架构优化（是否过度耦合？是否违反SOLID？）
   - 性能优化（是否有O(n²)可降为O(n)？是否有不必要的分配？）
   - 安全优化（是否有注入风险？是否有敏感信息泄露？）
   - 测试优化（覆盖率是否足够？边界条件是否覆盖？）
4. **连带修改检查** — 同类型功能是否需要连带修改？
   - 修改了A模块，B模块做了一模一样的事情，必须也改B
   - 即使用户没有提及，为了项目健壮性，理应继续思考和执行
5. **清理任务**
   - 遗忘清理的文件（临时文件、调试代码、TODO标记）
   - 历史兼容性：如果不允许兼容就直接删掉
   - 可合并的同名类/函数：名称相差不大就有合并价值
   - 禁止因"引用位置太多"而不合并 — 可以用脚本构造AST替换
6. **历史负担调整**
   - 不断叠加字段使类冗长 → 提取为配置类、DTO、工厂等
   - 优先选择最少代码的设计模式
   - 当发现代码已工程化，积极利用已有模式（管道、中间件、洋葱模型）
   - 除非用户明确要求，否则渐进式调整，不大规模重构
7. **总结** — 使用 task_create 工具构造任务（每条负评一个任务）

## 路由规则（必须严格遵守）

- 负评条数 ≤ 5 → route = "NEG_STOP"（质量可接受）
- 负评条数 6~10 → 使用 ask_user 工具询问用户是否继续（1分钟超时后协调者接管）
- 负评条数 > 10 → route = "NEG_CONTINUE"（必须继续修复）
- 循环次数 ≥ 16 → route = "NEG_STOP"（纵深防御硬上限）

## 输出格式（必须严格遵守）

评价完成后，必须在输出的最后输出一个 JSON 块，格式如下：

```json
{
  "negativeReviewCount": 7,
  "route": "NEG_CONTINUE",
  "taskId": "task-xxx",
  "items": [
    { "category": "代码不足", "description": "命名不规范", "severity": "medium" },
    { "category": "功能遗留", "description": "未处理空引用", "severity": "high" }
  ],
  "summary": "发现7条不足，主要集中在代码质量和功能遗留"
}
```

字段说明：
- negativeReviewCount: 负评总条数（整数）
- route: 路由决策，只能是 "NEG_CONTINUE" 或 "NEG_STOP"
- taskId: 通过 task_create 创建的任务ID（如有）
- items: 每条负评的详细信息
- summary: 评价总结

⚠️ JSON 块必须放在输出的最后，用 ```json 和 ``` 包裹。这是程序解析的唯一数据源。
""";
    }

    private static string BuildNegReviewInstruction(string objective)
    {
        return $"""
对以下任务执行负向评价:

原始任务: {objective}

请严格按照评价清单逐项执行，不要遗漏任何一项。
完成后根据路由规则决定路由，并在输出末尾输出 JSON 块。
""";
    }

    private static string BuildFixNegSystemPrompt()
    {
        return """
你是一个修复专家。根据负向评价的要求去完成任务。

## 修复原则

1. 每条负评必须对应一个修复动作
2. 修复后编译验证，确保不引入新问题
3. 使用 task_update 工具更新负评任务状态为 completed
4. 修复完成后决定是否需要再经历一轮负向评价

## 循环控制

- 如果你想再经历一轮负向评价以保证工程质量 → route = "NEG_CONTINUE"
- 如果当前负评超过10条 → 建议 route = "NEG_STOP"（让用户决定）
- 否则 → route = "NEG_STOP"

## 输出格式（必须严格遵守）

修复完成后，必须在输出的最后输出一个 JSON 块，格式如下：

```json
{
  "route": "NEG_STOP",
  "fixedCount": 5,
  "remainingCount": 2,
  "summary": "修复了5条，剩余2条需要下一轮处理"
}
```

字段说明：
- route: 路由决策，只能是 "NEG_CONTINUE" 或 "NEG_STOP"
- fixedCount: 已修复条数
- remainingCount: 剩余未修复条数
- summary: 修复总结

⚠️ JSON 块必须放在输出的最后，用 ```json 和 ``` 包裹。这是程序解析的唯一数据源。
""";
    }

    public static GoalGraphTemplate ClusterTemplate => new()
    {
        Name = "cluster",
        Keywords = ["集群", "cluster", "并行", "parallel", "批量", "batch", "多任务", "multi-task", "同时", "concurrent"],
        Description = "cluster_analyze → [动态展开并行Worker] → gather → merge → review",
        BuildGraph = BuildClusterGraph,
    };

    private static GoalGraph BuildClusterGraph(GoalGraphEngine engine, string objective)
    {
        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload>
        {
            Id = "cluster_analyze",
            Payload = new()
            {
                Kind = GoalNodeKind.Agent,
                Name = "cluster-analyzer",
                Role = AgentRole.Coordinator,
                SystemPrompt = BuildClusterAnalyzerSystemPrompt(),
                Instruction = $"Analyze whether this objective can be decomposed into parallel subtasks: {objective}",
            }
        });

        dag.AddNode(new DagNode<GoalNodePayload>
        {
            Id = "cluster_expand",
            Payload = new()
            {
                Kind = GoalNodeKind.Function,
                Name = "cluster-expander",
                Instruction = "Dynamically expand parallel worker nodes based on analysis results",
            }
        });

        dag.AddNode(new DagNode<GoalNodePayload>
        {
            Id = "cluster_review",
            Payload = new()
            {
                Kind = GoalNodeKind.Agent,
                Name = "cluster-reviewer",
                Role = AgentRole.Coordinator,
                SystemPrompt = "You are an independent reviewer for a parallel cluster execution. Evaluate the overall result: were all subtasks completed? Is the merged result correct? Are there any regressions or conflicts?",
                Instruction = "Review the cluster execution results objectively.",
                FreshContext = true,
            }
        });

        dag.AddEdge(new DagEdge { Id = "e-ca-ce", FromId = "cluster_analyze", ToId = "cluster_expand" });
        dag.AddEdge(new DagEdge { Id = "e-ce-cr", FromId = "cluster_expand", ToId = "cluster_review" });

        engine.RegisterFunction("cluster_expand", ClusterExpandFunction);

        return new GoalGraph
        {
            Name = $"cluster: {objective}",
            Dag = dag,
            StartNodeId = "cluster_analyze",
            EndNodeIds = FrozenSet.Create("cluster_review"),
        };
    }

    private static async Task<NodeResult> ClusterExpandFunction(NodeContext ctx)
    {
        var mutator = ctx.GraphMutator;
        if (mutator is null)
        {
            return NodeResult.Failed("GraphMutator not available — cannot expand cluster graph");
        }

        var analyzer = ctx.Services.GetService<IDecomposabilityAnalyzer>();
        var validator = ctx.Services.GetService<IClusterPlanValidator>();
        var approvalHook = ctx.Services.GetService<IClusterPlanApprovalHookManager>();

        if (analyzer is null)
        {
            return NodeResult.Failed("IDecomposabilityAnalyzer not available");
        }

        var objective = ctx.GlobalState.Objective;
        var constraints = ctx.GlobalState.Constraints;

        var decomposition = await analyzer.AnalyzeAsync(objective, constraints, ctx.CancellationToken).ConfigureAwait(false);

        if (!decomposition.IsDecomposable)
        {
            return NodeResult.Routed($"任务不可分解: {decomposition.Reason}，回退到单Agent模式", ["FALLBACK"]);
        }

        var plan = new ClusterPlan
        {
            Objective = objective,
            Decomposition = decomposition,
            ExecutionOptions = new ClusterExecutionOptions()
        };

        if (validator is not null)
        {
            var validationResult = validator.Validate(plan);
            plan.ValidationResult = validationResult;

            if (!validationResult.IsValid)
            {
                return NodeResult.Routed($"计划验证失败: {string.Join(", ", validationResult.Errors)}", ["FALLBACK"]);
            }
        }

        if (approvalHook is not null)
        {
            var approvalResult = await approvalHook.OnClusterPlanApprovalAsync(
                new ClusterPlanApprovalHookContext
                {
                    SessionId = ctx.GlobalState.GoalId,
                    Objective = objective,
                    Plan = plan,
                }, ctx.CancellationToken).ConfigureAwait(false);

            if (!approvalResult.ShouldProceed)
            {
                return NodeResult.Routed($"计划审批被阻止: {approvalResult.Message}", ["FALLBACK"]);
            }
        }

        var subTasks = decomposition.SubTasks;
        var workerIds = new List<string>();
        var edgeCounter = 0;

        foreach (var task in subTasks)
        {
            var workerId = $"worker_{task.Id}";
            workerIds.Add(workerId);

            var variant = task.Variant.Equals("explore", StringComparison.OrdinalIgnoreCase)
                ? ExecutorVariant.Explore
                : ExecutorVariant.Code;

            mutator.AddNode(workerId, new GoalNodePayload
            {
                Kind = GoalNodeKind.Agent,
                Name = $"worker-{task.Id}",
                Role = AgentRole.Executor,
                Variant = variant,
                SystemPrompt = $"You are a parallel worker for subtask '{task.Title}'. Focus ONLY on your assigned task. Files you own: {string.Join(", ", task.OwnedFiles)}",
                Instruction = task.Description,
            });

            if (task.DependsOn is { Count: > 0 })
            {
                foreach (var depId in task.DependsOn)
                {
                    var depWorkerId = $"worker_{depId}";
                    mutator.AddEdge($"e-w-{edgeCounter++}", depWorkerId, workerId, "depends-on");
                }
            }
            else
            {
                mutator.AddEdge($"e-ce-{edgeCounter++}", "cluster_expand", workerId);
            }
        }

        mutator.AddNode("cluster_gather", new GoalNodePayload
        {
            Kind = GoalNodeKind.Join,
            Name = "cluster-gatherer",
        });

        foreach (var workerId in workerIds)
        {
            mutator.AddEdge($"e-g-{edgeCounter++}", workerId, "cluster_gather");
        }

        mutator.AddNode("cluster_merge", new GoalNodePayload
        {
            Kind = GoalNodeKind.Agent,
            Name = "cluster-merger",
            Role = AgentRole.Coordinator,
            SystemPrompt = "You are a merge coordinator. Collect all worker results and merge them into a coherent final output. Resolve any conflicts.",
            Instruction = "Merge all parallel worker results into a unified output.",
        });

        mutator.AddEdge($"e-gm-{edgeCounter++}", "cluster_gather", "cluster_merge");
        mutator.AddEdge($"e-mr-{edgeCounter++}", "cluster_merge", "cluster_review");

        mutator.AddEndNode("cluster_review");

        foreach (var workerId in workerIds)
        {
            mutator.EnqueueNode(workerId);
        }

        return NodeResult.Succeeded($"集群图已展开: {workerIds.Count} 个并行Worker");
    }

    private static string BuildClusterAnalyzerSystemPrompt()
    {
        return """
            You are a cluster analysis agent. Your job is to:
            1. Use the DecomposabilityAnalyzer to determine if the objective can be split into parallel subtasks
            2. If decomposable, validate the plan and get approval
            3. Output the analysis result as structured JSON

            You have access to the following tools:
            - decomposability_analyzer: Analyze whether a task can be decomposed
            - cluster_plan_validator: Validate the decomposition plan
            - cluster_plan_approval: Get approval for the plan

            If the task is NOT decomposable, output: NOT_DECOMPOSABLE
            If the task IS decomposable and approved, output: DECOMPOSABLE with the subtask list
            If approval is blocked, output: BLOCKED with the reason

            IMPORTANT: You must actually call the tools to perform the analysis. Do not guess or fabricate results.
            """;
    }
}
