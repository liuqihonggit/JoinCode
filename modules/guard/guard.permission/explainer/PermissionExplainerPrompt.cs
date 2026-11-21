namespace Core.Permission;

/// <summary>
/// 权限解释的提示词与响应解析 — **纯函数**（无 IO、无 DI、无日志），
/// 因此「模型会怎么答」「答歪了怎么收场」这两件最容易出错的事可以逐条单测。
/// <para>对齐基准 <c>utils/permissions/permissionExplainer.ts</c> 的 <c>SYSTEM_PROMPT</c> /
/// <c>EXPLAIN_COMMAND_TOOL</c> / <c>RiskAssessmentSchema</c> 三段，但结构化输出手段不同：
/// 基准用强制 <c>tool_choice</c> + zod 校验，本仓库 provider 契约没有「强制指定工具」这一档，
/// 改为「只输出 JSON」+ 容错解析 + 严格校验 —— 校验不过同样得到 null（= 基准 safeParse 失败的落点）。</para>
/// </summary>
internal static class PermissionExplainerPrompt {
    /// <summary>上下文预算 — 基准 <c>extractConversationContext(messages, maxChars = 1000)</c>。</summary>
    internal const int ContextMaxChars = 1000;

    /// <summary>取最近几条 assistant 消息 — 基准 <c>slice(-3)</c>。</summary>
    internal const int ContextMessageCount = 3;

    /// <summary>
    /// 输出格式示例 — 由 DTO 序列化而来，而不是手写 JSON 字面量：
    /// 字段名与 <see cref="TryParse"/> 的解析端同源，改了一处不会出现「提示词说 A、解析要 B」。
    /// </summary>
    private static readonly string ResponseExample = JsonSerializer.Serialize(
        new PermissionExplanationDto {
            Explanation = "这条命令做什么（1-2 句）",
            Reasoning = "你为什么要运行它（以「我需要」开头）",
            Risk = "可能出什么问题（一句话，15 词内）",
            RiskLevel = "LOW | MEDIUM | HIGH",
        },
        PermissionExplainerJsonContext.Default.PermissionExplanationDto);

    /// <summary>系统提示 — 基准原文「Analyze shell commands and explain what they do, why you're running them, and potential risks.」</summary>
    internal const string SystemPrompt =
        "分析 shell 命令，说明它做什么、你为什么要运行它、以及潜在风险。";

    /// <summary>
    /// 构造用户提示 — 对齐基准 userPrompt 的四段（Tool / Description / Input / 最近对话上下文）。
    /// <para>基准末句是「Explain this command in context.」；这里额外把输出契约写进提示词，
    /// 因为本仓库没有强制 tool_choice 兜底，输出形状只能靠提示词约束。</para>
    /// </summary>
    /// <param name="request">待解释的工具调用。</param>
    /// <param name="conversationContext">最近对话上下文（可为空串）。</param>
    internal static string BuildUserPrompt(PermissionExplainRequest request, string conversationContext) {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder(512);
        builder.Append("工具: ").AppendLine(request.ToolName);
        if (!string.IsNullOrWhiteSpace(request.ToolDescription)) {
            builder.Append("描述: ").AppendLine(request.ToolDescription);
        }
        builder.AppendLine("输入:");
        builder.AppendLine(string.IsNullOrEmpty(request.ToolInput) ? "(无)" : request.ToolInput);
        if (!string.IsNullOrEmpty(conversationContext)) {
            builder.AppendLine();
            builder.AppendLine("最近对话上下文:");
            builder.AppendLine(conversationContext);
        }
        builder.AppendLine();
        builder.AppendLine("结合上下文解释这次调用。");
        builder.AppendLine("只返回一个 JSON 对象，不要代码围栏、不要多余文字，字段如下:");
        builder.AppendLine(ResponseExample);
        return builder.ToString();
    }

    /// <summary>
    /// 抽取最近对话上下文 — 对齐基准 <c>extractConversationContext</c>：只取 assistant 文本，
    /// 取最后 3 条、从旧到新拼接、总量不超过 1000 字符。
    /// <para>为什么只要 assistant：解释要回答的是「**我**为什么跑这条命令」，用户说过的话不构成动机；
    /// 混进用户消息只会让模型把用户意图当自己的意图。</para>
    /// </summary>
    /// <param name="messages">会话消息（旧 → 新）。</param>
    internal static string ExtractConversationContext(IReadOnlyList<ApiMessageRecord> messages) {
        ArgumentNullException.ThrowIfNull(messages);

        var parts = new List<string>(ContextMessageCount);
        var total = 0;
        for (var i = messages.Count - 1; i >= 0 && parts.Count < ContextMessageCount; i--) {
            var message = messages[i];
            if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var text = message.Content?.Trim();
            if (string.IsNullOrEmpty(text)) {
                continue;
            }

            var remaining = ContextMaxChars - total;
            if (remaining <= 0) {
                break;
            }
            var piece = text.Length > remaining ? text[..remaining] + "..." : text;
            parts.Add(piece);
            total += piece.Length;
        }

        // 倒序收集（新 → 旧），拼接前翻回旧 → 新 —— 避免 O(n²) 头部插入，也保住因果顺序
        parts.Reverse();
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// 解析模型响应 — 容错到「能救就救」：允许 ```json 围栏、前后闲话；
    /// 但只要四字段有一项缺失/空白、或 riskLevel 不是三档之一，就返回 null（宁可说「不可用」，
    /// 也不要把半截解释渲染成完整结论 —— 那是拿用户的安全判断赌模型这一刻的服从度）。
    /// </summary>
    /// <param name="responseText">模型返回的原始文本。</param>
    internal static PermissionExplanation? TryParse(string? responseText) {
        if (string.IsNullOrWhiteSpace(responseText)) {
            return null;
        }

        var json = ExtractJsonObject(responseText);
        if (json is null) {
            return null;
        }

        PermissionExplanationDto? dto;
        try {
            dto = JsonSerializer.Deserialize(json, PermissionExplainerJsonContext.Default.PermissionExplanationDto);
        } catch (JsonException) {
            return null;
        }

        if (dto is null) {
            return null;
        }

        var explanation = dto.Explanation?.Trim();
        var reasoning = dto.Reasoning?.Trim();
        var risk = dto.Risk?.Trim();
        if (string.IsNullOrEmpty(explanation) || string.IsNullOrEmpty(reasoning) || string.IsNullOrEmpty(risk)) {
            return null;
        }

        var level = PermissionRiskLevelExtensions.FromValue(dto.RiskLevel?.Trim().ToUpperInvariant());
        if (level is null) {
            return null;
        }

        return new PermissionExplanation {
            RiskLevel = level.Value,
            Explanation = explanation,
            Reasoning = reasoning,
            Risk = risk,
        };
    }

    /// <summary>取首个 '{' 到末个 '}' 之间的内容（模型常带围栏或前后说明），取不到返回 null。</summary>
    private static string? ExtractJsonObject(string text) {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }
}
