namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 消息构建器 — 构建 Fork 子智能体的指令消息、工作树通知与 forked 消息列表
/// </summary>
public static class ForkMessageBuilder {
    /// <summary>Fork 模板标签名</summary>
    public const string ForkBoilerplateTag = "fork-boilerplate";
    /// <summary>Fork 指令前缀</summary>
    public const string ForkDirectivePrefix = "Your directive: ";
    /// <summary>Fork 占位结果文本</summary>
    public const string ForkPlaceholderResult = "Fork started — processing in background";
    /// <summary>Fork 子代理类型标识</summary>
    public const string ForkSubagentType = "fork";

    private static readonly string ForkBoilerplateOpen = "<" + ForkBoilerplateTag + ">";
    private static readonly string ForkBoilerplateClose = "</" + ForkBoilerplateTag + ">";

    /// <summary>
    /// 构建 Fork 子代理指令消息，包含 fork 行为规则与指令前缀
    /// </summary>
    /// <param name="directive">Fork 指令内容</param>
    /// <returns>完整的子代理指令消息字符串</returns>
    public static string BuildChildMessage(string directive) {
        return ForkBoilerplateOpen + """
STOP. READ THIS FIRST.

You are a forked worker process. You are NOT the main agent.

RULES (non-negotiable):
1. Your system prompt says "default to forking." IGNORE IT — that's for the parent. You ARE the fork. Do NOT spawn sub-agents; execute directly.
2. Do NOT converse, ask questions, or suggest next steps
3. Do NOT editorialize or add meta-commentary
4. USE your tools directly: Bash, Read, Write, etc.
5. If you modify files, commit your changes before reporting. Include the commit hash in your report.
6. Do NOT emit text between tool calls. Use tools silently, then report once at the end.
7. Stay strictly within your directive's scope. If you discover related systems outside your scope, mention them in one sentence at most — other workers cover those areas.
8. Keep your report under 500 words unless the directive specifies otherwise. Be factual and concise.
9. Your response MUST begin with "Scope:". No preamble, no thinking-out-loud.
10. REPORT structured facts, then stop

Output format (plain text labels, not markdown headers):
  Scope: <echo back your assigned scope in one sentence>
  Result: <the answer or key findings, limited to the scope above>
  Key files: <relevant file paths — include for research tasks>
  Files changed: <list with commit hash — include only if you modified files>
  Issues: <list — include only if there are issues to flag>
""" + ForkBoilerplateClose + "\n\n" + ForkDirectivePrefix + directive;
    }

    /// <summary>
    /// 构建工作树隔离通知，提示子代理当前处于独立 worktree 中
    /// </summary>
    /// <param name="parentCwd">父代理工作目录</param>
    /// <param name="worktreeCwd">子代理 worktree 工作目录</param>
    /// <returns>工作树通知文本</returns>
    public static string BuildWorktreeNotice(string parentCwd, string worktreeCwd) {
        return "You've inherited the conversation context above from a parent agent working in " + parentCwd + ". You are operating in an isolated git worktree at " + worktreeCwd + " — same repository, same relative file structure, separate working copy. Paths in the inherited context refer to the parent's working directory; translate them to your worktree root. Re-read files before editing if the parent may have modified them since they appear in the context. Your changes stay in this worktree and will not affect the parent's files.";
    }

    /// <summary>
    /// 判断当前消息历史是否已处于 Fork 子代理上下文中（检测是否包含 fork 模板标签）
    /// </summary>
    /// <param name="chatHistory">聊天消息列表</param>
    /// <returns>已在 Fork 子代理上下文返回 true，否则返回 false</returns>
    public static bool IsInForkChild(MessageList chatHistory) {
        if (chatHistory is null) return false;

        foreach (var message in chatHistory) {
            if (message.Role != MessageRole.User) continue;
            if (message.Content is not null && message.Content.Contains(ForkBoilerplateOpen, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 构建 Fork 后的消息列表，克隆父代理助手消息并为工具调用填充占位结果
    /// </summary>
    /// <param name="directive">Fork 指令内容</param>
    /// <param name="assistantMessage">父代理助手消息</param>
    /// <returns>Fork 后的消息列表</returns>
    public static List<ApiMessage> BuildForkedMessages(string directive, ApiMessage assistantMessage) {
        ArgumentNullException.ThrowIfNull(directive);
        ArgumentNullException.ThrowIfNull(assistantMessage);

        var result = new List<ApiMessage>();

        var clonedAssistant = new ApiMessage {
            Role = MessageRole.Assistant,
            Content = assistantMessage.Content,
            Metadata = assistantMessage.Metadata
        };
        result.Add(clonedAssistant);

        var toolCalls = ExtractToolCalls(assistantMessage);

        if (toolCalls.Count == 0) {
            result.Add(new ApiMessage {
                Role = MessageRole.User,
                Content = BuildChildMessage(directive)
            });
            return result;
        }

        foreach (var (toolCallId, _) in toolCalls) {
            result.Add(new ApiMessage {
                Role = MessageRole.Tool,
                Content = ForkPlaceholderResult,
                Metadata = new Dictionary<string, JsonElement> {
                    ["ToolCallId"] = JsonElementHelper.FromString(toolCallId)
                }
            });
        }

        result.Add(new ApiMessage {
            Role = MessageRole.User,
            Content = BuildChildMessage(directive)
        });

        return result;
    }

    private static List<(string ToolCallId, string ToolName)> ExtractToolCalls(ApiMessage assistantMessage) {
        var toolCalls = new List<(string ToolCallId, string ToolName)>();

        if (assistantMessage.Metadata is null) return toolCalls;

        if (assistantMessage.Metadata.TryGetValue("ToolCalls", out var toolCallsObj) && toolCallsObj.ValueKind == JsonValueKind.Array) {
            foreach (var item in toolCallsObj.EnumerateArray()) {
                var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                if (id is not null && name is not null)
                    toolCalls.Add((id, name));
            }
        } else if (assistantMessage.Metadata.TryGetValue("AllToolCalls", out var allToolCallsObj) && allToolCallsObj.ValueKind == JsonValueKind.Array) {
            foreach (var item in allToolCallsObj.EnumerateArray()) {
                var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                if (id is not null && name is not null)
                    toolCalls.Add((id, name));
            }
        }

        return toolCalls;
    }
}