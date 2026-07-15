namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// AgentTool 提示词
/// </summary>
[ToolPrompt(ToolName = "Agent", Category = ToolPromptCategory.Agent)]
public static class AgentToolPrompt
{
    public const string ToolName = AgentToolNameConstants.Agent;

    /// <summary>
    /// 获取 Agent 工具提示词
    /// </summary>
    public static string GetPrompt(
        List<AgentDefinition> agentDefinitions,
        bool isCoordinator = false,
        List<string>? allowedAgentTypes = null,
        bool forkEnabled = false)
    {
        // 根据允许的类型过滤代理 - 使用 HashSet 优化查找
        List<AgentDefinition> effectiveAgents;
        if (allowedAgentTypes != null && allowedAgentTypes.Count > 0)
        {
            var allowedSet = new HashSet<string>(allowedAgentTypes);
            effectiveAgents = agentDefinitions.Where(a => allowedSet.Contains(a.AgentType)).ToList();
        }
        else
        {
            effectiveAgents = agentDefinitions;
        }

        // Fork 子代理部分
        var whenToForkSection = forkEnabled
            ? $@"


## 何时分叉

分叉自己（省略 `subagent_type`）当中间工具输出不值得保留在你的上下文中时。标准是定性的 —— ""我会再次需要这个输出吗"" —— 而不是任务大小。
- **研究**：分叉开放式问题。如果研究可以分解为独立的问题，在一个消息中启动并行分叉。分叉胜过新子代理 —— 它继承上下文并共享你的缓存。
- **实现**：优先分叉需要多次编辑的实现工作。在跳到实现之前先做研究。

分叉很便宜，因为它们共享你的提示词缓存。不要在分叉上设置 `model` —— 不同的模型无法重用父级的缓存。传递一个短的 `name`（一两个词，小写），以便用户可以在团队面板中看到分叉并中途引导它。

**不要偷看。** 工具结果包含一个 `output_file` 路径 —— 除非用户明确要求进度检查，否则不要 Read 或 tail 它。你会收到完成通知；相信它。中途读取记录会将分叉的工具噪音拉入你的上下文，这违背了分叉的目的。

**不要竞争。** 启动后，你对分叉发现的内容一无所知。永远不要以任何形式编造或预测分叉结果 —— 不是作为散文、摘要或结构化输出。通知在后续轮次中作为用户角色消息到达；它永远不是你写的东西。如果用户在通知落地之前问了一个后续问题，告诉他们分叉仍在运行 —— 给状态，不是猜测。

**编写分叉提示词。** 由于分叉继承你的上下文，提示词是一个*指令* —— 要做什么，而不是情况是什么。具体说明范围：什么在内，什么在外，另一个代理在处理什么。不要重新解释背景。
"
            : "";

        var writingThePromptSection = $@"


## 编写提示词

" + (forkEnabled ? "当生成新代理（使用 `subagent_type`）时，它从零上下文开始。" : "") + $@"像向刚走进房间的聪明同事介绍一样介绍代理 —— 它没有看到这个对话，不知道你尝试了什么，不理解为什么这个任务重要。
- 解释你试图完成什么以及为什么。
- 描述你已经学到或排除了什么。
- 提供足够的周围问题上下文，以便代理可以做出判断，而不是只遵循狭窄的指令。
- 如果你需要简短的回复，说出来（""在 200 字以内报告""）。
- 查找：交出确切的命令。调查：交出问题 —— 当前提错误时，规定的步骤成为死重。

" + (forkEnabled ? "对于新代理，简洁的" : "简洁的") + $@"命令式提示词产生肤浅、通用的工作。

**永远不要委托理解。** 不要写""根据你的发现，修复错误""或""根据研究，实现它""。这些短语将综合推给代理，而不是自己做。写证明你理解的提示词：包含文件路径、行号、具体要更改什么。
";

        var forkExamples = $@"示例用法：

<example>
user: ""这个分支在我们可以发货之前还有什么？""
assistant: <thinking>分叉这个 —— 这是一个调查问题。我想要要点清单，而不是我上下文中的 git 输出。</thinking>
Agent({{
  name: ""ship-audit"",
  description: ""分支发货准备审计"",
  prompt: ""审计这个分支在可以发货之前还有什么。检查：未提交的更改、领先 main 的提交、测试是否存在、GrowthBook 门是否连接、CI 相关文件是否更改。报告要点清单 —— 已完成 vs 缺失。200 字以内。""
}})
assistant: 发货准备审计运行中。
<commentary>
回合在这里结束。协调员对发现一无所知。接下来是一个单独的回合 —— 通知从外部到达，作为用户角色消息。它不是协调员写的东西。
</commentary>
[后续回合 —— 通知作为用户消息到达]
assistant: 审计回来了。三个阻塞：新提示词路径没有测试，GrowthBook 门已连接但不在 build_flags.yaml 中，一个未提交的文件。
</example>

<example>
user: ""所以门是连接了还是没有""
<commentary>
用户在中途等待时询问。审计分叉正是为此启动的，它还没有返回。协调员没有这个答案。给状态，不是编造的结果。
</commentary>
assistant: 仍在等待审计 —— 这是它正在检查的事情之一。应该很快落地。
</example>

<example>
user: ""你能对这个迁移是否安全获得第二意见吗？""
assistant: <thinking>我会问 code-reviewer 代理 —— 它不会看到我的分析，所以它可以给出独立的阅读。</thinking>
<commentary>
指定了 subagent_type，所以代理从头开始。它需要在提示词中提供完整的上下文。简报解释了要评估什么以及为什么。
</commentary>
Agent({{
  name: ""migration-review"",
  description: ""独立迁移审查"",
  subagent_type: ""code-reviewer"",
  prompt: ""审查 migration 0042_user_schema.sql 的安全性。上下文：我们正在向一个 5000 万行的表添加 NOT NULL 列。现有行获得回填默认值。我想要关于回填方法在并发写入下是否安全的第二意见 —— 我已经检查了锁定行为，但想要独立验证。报告：这是否安全，如果不安全，具体什么会中断？""
}})
</example>
";

        var currentExamples = $@"示例用法：

<example_agent_descriptions>
""test-runner"": 在你完成编写代码后使用此代理运行测试
""greeting-responder"": 使用此代理以友好的笑话回应用户问候
</example_agent_descriptions>

<example>
user: ""请编写一个检查数字是否为质数的函数""
assistant: 我将使用 FileWrite 工具编写以下代码：
<code>
function isPrime(n) {{
  if (n <= 1) return false
  for (let i = 2; i * i <= n; i++) {{
    if (n % i === 0) return false
  }}
  return true
}}
</code>
<commentary>
由于编写了重要的代码片段并且任务已完成，现在使用 test-runner 代理运行测试
</commentary>
assistant: 使用 Agent 工具启动 test-runner 代理
</example>

<example>
user: ""Hello""
<commentary>
由于用户在问候，使用 greeting-responder 代理以友好的笑话回应
</commentary>
assistant: ""我将使用 Agent 工具启动 greeting-responder 代理""
</example>
";

        // 代理列表部分
        var agentListSection = $"可用代理类型及其可访问的工具：\n{string.Join("\n", effectiveAgents.Select(FormatAgentLine).ToArray())}";

        // 共享核心提示词
        var shared = $@"启动新代理自主处理复杂的多步骤任务。

Agent 工具启动专门处理复杂任务的代理（子进程）。每种代理类型都有特定的能力和可用工具。

{agentListSection}

" + (forkEnabled
    ? $@"使用 Agent 工具时，指定 subagent_type 以使用专门代理，或省略它以分叉自己 —— 分叉继承你的完整对话上下文。"
    : $@"使用 Agent 工具时，指定 subagent_type 参数以选择要使用的代理类型。如果省略，则使用通用代理。");

        // 协调器模式获得精简提示词
        if (isCoordinator)
        {
            return shared;
        }

        // 非协调器获得完整提示词
        return $@"{shared}

何时不使用 Agent 工具：
- 如果你想读取特定文件路径，使用 FileRead 工具或 Glob 工具，而不是 Agent 工具，以更快地找到匹配
- 如果你正在搜索特定类定义如 ""class Foo""，使用 Glob 工具，以更快地找到匹配
- 如果你在特定文件或 2-3 个文件集合中搜索代码，使用 FileRead 工具，而不是 Agent 工具，以更快地找到匹配
- 其他与上述代理描述无关的任务

用法说明：
- 始终包含简短描述（3-5 个词）总结代理将做什么
- 当代理完成时，它会返回一条消息给你。代理返回的结果对用户不可见。要向用户显示结果，你应该发送一条文本消息给用户，并附上结果的简洁摘要。
- 你可以使用 SendMessage 继续先前生成的代理，使用代理的 ID 或名称作为 `to` 字段。代理以其完整上下文恢复。" + (forkEnabled ? "每个带有 subagent_type 的新 Agent 调用都从零上下文开始 —— 提供完整的任务描述。" : "每个 Agent 调用都从头开始 —— 提供完整的任务描述。") + $@"
- 代理的输出通常应该被信任
- 清楚地告诉代理你期望它编写代码还是只做研究（搜索、文件读取、网络获取等），因为它不知道用户的意图
- 如果代理描述提到应该主动使用，那么你应该尽力在用户不必先要求的情况下使用它。使用你的判断。
- 如果用户指定他们想要你""并行""运行代理，你必须发送一条包含多个 Agent 工具使用内容块的单一消息。例如，如果你需要并行启动 build-validator 代理和 test-runner 代理，发送一条包含两个工具调用的单一消息。
- 你可以将 `isolation: ""worktree""` 设置为在临时 git 工作树中运行代理，给它一个孤立的仓库副本。如果代理没有进行更改，工作树会自动清理；如果进行了更改，工作树路径和分支会在结果中返回。{whenToForkSection}{writingThePromptSection}

{(forkEnabled ? forkExamples : currentExamples)}";
    }

    /// <summary>
    /// 格式化代理行
    /// </summary>
    private static string FormatAgentLine(AgentDefinition agent)
    {
        var toolsDescription = GetToolsDescription(agent);
        return $"- {agent.AgentType}: {agent.WhenToUse} (工具: {toolsDescription})";
    }

    /// <summary>
    /// 获取工具描述
    /// </summary>
    private static string GetToolsDescription(AgentDefinition agent)
    {
        var hasAllowlist = agent.Tools != null && agent.Tools.Count > 0;
        var hasDenylist = agent.DisallowedTools != null && agent.DisallowedTools.Count > 0;

        if (hasAllowlist && hasDenylist)
        {
            var denySet = new HashSet<string>(agent.DisallowedTools ?? []);
            var effectiveTools = (agent.Tools ?? []).Where(t => !denySet.Contains(t)).ToList();
            if (effectiveTools.Count == 0)
            {
                return "无";
            }
            return string.Join(", ", effectiveTools);
        }
        else if (hasAllowlist)
        {
            return string.Join(", ", agent.Tools ?? []);
        }
        else if (hasDenylist)
        {
            return $"除 {string.Join(", ", agent.DisallowedTools!)} 外的所有工具";
        }

        return "所有工具";
    }
}

/// <summary>
/// 代理定义
/// </summary>
public class AgentDefinition
{
    public required string AgentType { get; set; }
    public required string WhenToUse { get; set; }
    public List<string>? Tools { get; set; }
    public List<string>? DisallowedTools { get; set; }
    public string? Description { get; set; }
    public string? SystemPrompt { get; set; }
    public string? ModelName { get; set; }
    public float? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public bool IsBackground { get; set; }
    public string? SourcePath { get; set; }
    public List<string>? Skills { get; set; }
    public string? PermissionMode { get; set; }
    public Dictionary<string, List<AgentHookMatcher>>? Hooks { get; set; }
    public List<AgentMcpServerSpec>? McpServers { get; set; }
    public List<string>? RequiredMcpServers { get; set; }

    /// <summary>
    /// 记忆作用域 — 对齐 TS AgentDefinition.memory
    /// null 表示不启用记忆
    /// </summary>
    public AgentMemoryScope? Memory { get; set; }

    /// <summary>
    /// 是否省略 claudeMd — 只读 Agent (Explore/Plan) 不需要 CLAUDE.md 上下文
    /// 对齐 TS: agentDefinition.omitClaudeMd
    /// </summary>
    public bool OmitClaudeMd { get; init; }

    /// <summary>
    /// 是否省略 gitStatus — Explore/Plan 不需要 git status（~1-3 Gtok/周节省）
    /// 对齐 TS: resolvedSystemContext = Explore/Plan ? systemContextNoGit : baseSystemContext
    /// </summary>
    public bool OmitGitStatus { get; init; }
}

/// <summary>
/// Agent Hook 匹配器配置 - frontmatter 中的 hooks 定义
/// </summary>
public sealed class AgentHookMatcher
{
    public string? Matcher { get; set; }
    public required List<AgentHookCommand> Hooks { get; set; }
}

/// <summary>
/// Agent Hook 命令配置
/// </summary>
public sealed class AgentHookCommand
{
    public required string Type { get; set; }
    public string? Command { get; set; }
    public string? Prompt { get; set; }
    public string? If { get; set; }
    public int? Timeout { get; set; }
}

public sealed class AgentMcpServerSpec
{
    public string? ServerNameRef { get; init; }
    public AgentMcpServerInlineConfig? InlineConfig { get; init; }

    public static AgentMcpServerSpec FromReference(string serverName) =>
        new() { ServerNameRef = serverName };

    public static AgentMcpServerSpec FromInline(string name, AgentMcpServerInlineConfig config) =>
        new() { ServerNameRef = name, InlineConfig = config };
}

public sealed class AgentMcpServerInlineConfig
{
    public string? Command { get; init; }
    public List<string>? Args { get; init; }
    public Dictionary<string, string>? Env { get; init; }
    public string? Url { get; init; }
    public string? TransportType { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    /// <summary>
    /// 认证配置名称 — 引用 mcp_auth_* 工具配置的认证
    /// </summary>
    public string? AuthName { get; init; }
}
