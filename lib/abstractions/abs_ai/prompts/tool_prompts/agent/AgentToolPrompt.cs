namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// AgentTool 提示词
/// </summary>
[ToolPrompt(ToolName = AgentToolName.Agent, Category = ToolPromptCategory.Agent)]
public static class AgentToolPrompt {
    public const string ToolName = AgentToolNameEnumConstants.Agent;

    /// <summary>
    /// 获取 Agent 工具提示词
    /// </summary>
    public static string GetPrompt(
        List<AgentDefinition> agentDefinitions,
        bool isCoordinator = false,
        List<string>? allowedAgentTypes = null,
        bool forkEnabled = false) {
        List<AgentDefinition> effectiveAgents;
        if (allowedAgentTypes != null && allowedAgentTypes.Count > 0) {
            var allowedSet = new HashSet<string>(allowedAgentTypes);
            effectiveAgents = agentDefinitions.Where(a => allowedSet.Contains(a.DisplayId)).ToList();
        } else {
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

**不要把整个问题原样塞给子代理。** 如果用户请求包含多个独立子任务（例如先做 A 再做 B，还要验证 C），先在父级把它分解为子问题，再为每个子问题启动一个子代理 —— 而不是把整段用户消息原样作为 prompt 传给单个子代理。把整段问题传给孩子会让孩子重复你的综合工作、丢失上下文、且无法并行。判断标准：prompt 里出现并且 / 同时 / 以及 / 然后 等连接词串联多个不相关目标，就该拆分。
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
assistant: 我将使用 {FileToolNameEnumConstants.FileWrite} 工具编写以下代码：
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
assistant: 使用 {AgentToolNameEnumConstants.Agent} 工具启动 test-runner 代理
</example>

<example>
user: ""Hello""
<commentary>
由于用户在问候，使用 greeting-responder 代理以友好的笑话回应
</commentary>
assistant: ""我将使用 {AgentToolNameEnumConstants.Agent} 工具启动 greeting-responder 代理""
</example>
";

        // 代理列表部分
        var agentListSection = $"可用代理类型及其可访问的工具：\n{string.Join("\n", effectiveAgents.Select(FormatAgentLine))}";

        // 共享核心提示词
        var shared = $@"启动新代理自主处理复杂的多步骤任务。

{AgentToolNameEnumConstants.Agent} 工具启动专门处理复杂任务的代理（子进程）。每种代理类型都有特定的能力和可用工具。

{agentListSection}

" + (forkEnabled
    ? $@"使用 {AgentToolNameEnumConstants.Agent} 工具时，指定 subagent_type 以使用专门代理，或省略它以分叉自己 —— 分叉继承你的完整对话上下文。"
    : $@"使用 {AgentToolNameEnumConstants.Agent} 工具时，指定 subagent_type 参数以选择要使用的代理类型。如果省略，则使用通用代理。");

        // 协调器模式获得精简提示词
        if (isCoordinator) {
            return shared;
        }

        // 非协调器获得完整提示词
        return $@"{shared}

何时不使用 {AgentToolNameEnumConstants.Agent} 工具：
- 如果你想读取特定文件路径，使用 {FileToolNameEnumConstants.FileRead} 工具或 {SearchToolNameEnumConstants.Glob} 工具，而不是 {AgentToolNameEnumConstants.Agent} 工具，以更快地找到匹配
- 如果你正在搜索特定类定义如 ""class Foo""，使用 {SearchToolNameEnumConstants.Glob} 工具，以更快地找到匹配
- 如果你在特定文件或 2-3 个文件集合中搜索代码，使用 {FileToolNameEnumConstants.FileRead} 工具，而不是 {AgentToolNameEnumConstants.Agent} 工具，以更快地找到匹配
- 其他与上述代理描述无关的任务

用法说明：
- 始终包含简短描述（3-5 个词）总结代理将做什么
- 当代理完成时，它会返回一条消息给你。代理返回的结果对用户不可见。要向用户显示结果，你应该发送一条文本消息给用户，并附上结果的简洁摘要。
- 你可以使用 {AgentToolNameEnumConstants.AgentSendMessage} 继续先前生成的代理，使用代理的 ID 或名称作为 `to` 字段。代理以其完整上下文恢复。" + (forkEnabled ? $"每个带有 subagent_type 的新 {AgentToolNameEnumConstants.Agent} 调用都从零上下文开始 —— 提供完整的任务描述。" : $"每个 {AgentToolNameEnumConstants.Agent} 调用都从头开始 —— 提供完整的任务描述。") + $@"
- 代理的输出通常应该被信任
- 清楚地告诉代理你期望它编写代码还是只做研究（搜索、文件读取、网络获取等），因为它不知道用户的意图
- 如果代理描述提到应该主动使用，那么你应该尽力在用户不必先要求的情况下使用它。使用你的判断。
- 如果用户指定他们想要你""并行""运行代理，你必须发送一条包含多个 {AgentToolNameEnumConstants.Agent} 工具使用内容块的单一消息。例如，如果你需要并行启动 build-validator 代理和 test-runner 代理，发送一条包含两个工具调用的单一消息。
- 你可以将 `isolation: ""worktree""` 设置为在临时 git 工作树中运行代理，给它一个孤立的仓库副本。如果代理没有进行更改，工作树会自动清理；如果进行了更改，工作树路径和分支会在结果中返回。{whenToForkSection}{writingThePromptSection}

{(forkEnabled ? forkExamples : currentExamples)}";
    }

    /// <summary>
    /// 格式化代理行
    /// </summary>
    private static string FormatAgentLine(AgentDefinition agent) {
        var toolsDescription = GetToolsDescription(agent);
        return $"- {agent.DisplayId}: {agent.WhenToUse} (工具: {toolsDescription})";
    }

    /// <summary>
    /// 获取工具描述
    /// </summary>
    private static string GetToolsDescription(AgentDefinition agent) {
        var hasAllowlist = agent.Tools != null && agent.Tools.Count > 0;
        var hasDenylist = agent.DisallowedTools != null && agent.DisallowedTools.Count > 0;

        if (hasAllowlist && hasDenylist) {
            var denySet = new HashSet<string>(agent.DisallowedTools ?? []);
            var effectiveTools = (agent.Tools ?? []).Where(t => !denySet.Contains(t)).ToList();
            if (effectiveTools.Count == 0) {
                return "无";
            }
            return string.Join(", ", effectiveTools);
        } else if (hasAllowlist) {
            return string.Join(", ", agent.Tools ?? []);
        } else if (hasDenylist) {
            return $"除 {string.Join(", ", agent.DisallowedTools!)} 外的所有工具";
        }

        return "所有工具";
    }
}

/// <summary>
/// 代理定义
/// </summary>
public class AgentDefinition {
    /// <summary>获取或设置代理角色。</summary>
    public required AgentRole Role { get; set; }
    /// <summary>获取或设置执行器变体。</summary>
    public ExecutorVariant? Variant { get; set; }
    /// <summary>获取或设置使用时机描述。</summary>
    public required string WhenToUse { get; set; }
    /// <summary>获取或设置允许使用的工具列表。</summary>
    public List<string> Tools { get; set; } = [];
    /// <summary>获取或设置禁止使用的工具列表。</summary>
    public List<string> DisallowedTools { get; set; } = [];
    /// <summary>获取或设置代理描述。</summary>
    public string? Description { get; set; }
    /// <summary>获取或设置系统提示词。</summary>
    public string? SystemPrompt { get; set; }
    /// <summary>获取或设置模型名称。</summary>
    public string? ModelName { get; set; }
    /// <summary>获取或设置采样温度。</summary>
    public float? Temperature { get; set; }
    /// <summary>获取或设置最大生成令牌数。</summary>
    public int? MaxTokens { get; set; }
    /// <summary>获取或设置是否为后台代理。</summary>
    public bool IsBackground { get; set; }
    /// <summary>获取或设置代理定义源文件路径。</summary>
    public string? SourcePath { get; set; }
    /// <summary>获取或设置技能列表。</summary>
    public List<string> Skills { get; set; } = [];
    /// <summary>获取或设置权限模式。</summary>
    public string? PermissionMode { get; set; }
    /// <summary>获取或设置钩子配置。</summary>
    public Dictionary<string, List<AgentHookMatcher>> Hooks { get; set; } = [];
    /// <summary>获取或设置 MCP 服务器规格列表。</summary>
    public List<AgentMcpServerSpec> McpServers { get; set; } = [];
    /// <summary>获取或设置必需的 MCP 服务器名称列表。</summary>
    public List<string> RequiredMcpServers { get; set; } = [];

    /// <summary>
    /// 记忆作用域 — 对齐 TS AgentDefinition.memory
    /// null 表示不启用记忆
    /// </summary>
    public AgentMemoryScope? Memory { get; set; }

    /// <summary>
    /// 是否省略项目规则上下文 — 只读 Agent (Explore/Plan) 不需要 CLAUDE.md 上下文
    /// 对齐 TS: agentDefinition.omitClaudeMd
    /// </summary>
    public bool OmitProjectRules { get; init; }

    /// <summary>
    /// 是否省略 gitStatus — Explore/Plan 不需要 git status（~1-3 Gtok/周节省）
    /// 对齐 TS: resolvedSystemContext = Explore/Plan ? systemContextNoGit : baseSystemContext
    /// </summary>
    public bool OmitGitStatus { get; init; }

    /// <summary>每轮重注入的关键系统提醒 — 对齐 TS 原版 criticalSystemReminder_EXPERIMENTAL</summary>
    public string? CriticalSystemReminder { get; init; }

    /// <summary>首轮前置 prompt — spawn 时作为第一条 user message 注入,支持斜杠命令 — 对齐 TS 原版 initialPrompt</summary>
    public string? InitialPrompt { get; init; }

    /// <summary>
    /// 获取显示标识 — "coordinator" 或 "executor:code"
    /// </summary>
    public string DisplayId => Variant.HasValue
        ? $"{Role.ToValue()}:{Variant.Value.ToValue()}"
        : Role.ToValue();
}

/// <summary>
/// Agent Hook 匹配器配置 - frontmatter 中的 hooks 定义
/// </summary>
public sealed class AgentHookMatcher {
    /// <summary>获取或设置匹配器表达式。</summary>
    public string? Matcher { get; set; }
    /// <summary>获取或设置钩子命令列表。</summary>
    public required List<AgentHookCommand> Hooks { get; set; }
}

/// <summary>
/// Agent Hook 命令配置
/// </summary>
public sealed class AgentHookCommand {
    /// <summary>获取或设置钩子类型。</summary>
    public required string Type { get; set; }
    /// <summary>获取或设置要执行的命令。</summary>
    public string? Command { get; set; }
    /// <summary>获取或设置提示词。</summary>
    public string? Prompt { get; set; }
    /// <summary>获取或设置执行条件表达式。</summary>
    public string? If { get; set; }
    /// <summary>获取或设置超时时间(毫秒)。</summary>
    public int? Timeout { get; set; }
}

public sealed class AgentMcpServerSpec {
    /// <summary>获取服务器名称引用。</summary>
    public string? ServerNameRef { get; init; }
    /// <summary>获取内联服务器配置。</summary>
    public AgentMcpServerInlineConfig? InlineConfig { get; init; }

    /// <summary>从服务器名称引用创建规格。</summary>
    /// <param name="serverName">服务器名称。</param>
    public static AgentMcpServerSpec FromReference(string serverName) =>
        new() { ServerNameRef = serverName };

    /// <summary>从内联配置创建规格。</summary>
    /// <param name="name">服务器名称。</param>
    /// <param name="config">内联配置。</param>
    public static AgentMcpServerSpec FromInline(string name, AgentMcpServerInlineConfig config) =>
        new() { ServerNameRef = name, InlineConfig = config };
}

public sealed class AgentMcpServerInlineConfig {
    /// <summary>获取启动命令。</summary>
    public string? Command { get; init; }
    /// <summary>获取命令参数列表。</summary>
    public List<string> Args { get; init; } = [];
    /// <summary>获取环境变量字典。</summary>
    public Dictionary<string, string> Env { get; init; } = [];
    /// <summary>获取服务器 URL。</summary>
    public string? Url { get; init; }
    /// <summary>获取传输类型。</summary>
    public string? TransportType { get; init; }
    /// <summary>获取请求头字典。</summary>
    public Dictionary<string, string> Headers { get; init; } = [];
    /// <summary>
    /// 认证配置名称 — 引用 mcp_auth_* 工具配置的认证
    /// </summary>
    public string? AuthName { get; init; }
}