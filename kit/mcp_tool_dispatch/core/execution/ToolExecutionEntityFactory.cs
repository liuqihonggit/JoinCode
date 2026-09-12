namespace McpToolRegistry;

/// <summary>
/// 工具执行实体工厂 — 所有工具都创建 Entity，部分工具创建子类（有额外字段）
/// 不做白名单预判：所有工具都有 Entity，子类仅为需要额外字段的工具服务
/// sessionId 默认从 SessionContext.Current 继承，无需显式传参
/// </summary>
public static class ToolExecutionEntityFactory
{
    public static ToolExecutionEntity Create(
        string toolName,
        string? toolUseId = null,
        string? spanId = null,
        Dictionary<string, JsonElement>? arguments = null,
        ObjectId sessionId = default)
    {
        return toolName.ToLowerInvariant() switch
        {
            "bash" or "powershell" => CreateBashEntity(arguments, toolUseId, spanId, sessionId),
            "web_fetch" or "web_search" => CreateWebEntity(arguments, toolUseId, spanId, sessionId),
            "sleep" or "sleep_until" => CreateSleepEntity(arguments, toolUseId, spanId, sessionId),
            "repl" => CreateReplEntity(arguments, toolUseId, spanId, sessionId),
            "ask_user" => CreateUserInteractionEntity(arguments, toolUseId, spanId, sessionId),
            _ => new ToolExecutionEntity(toolName, toolUseId, spanId, sessionId: sessionId)
        };
    }

    private static BashProcessEntity CreateBashEntity(
        Dictionary<string, JsonElement>? arguments, string? toolUseId, string? spanId, ObjectId sessionId)
    {
        var command = arguments?.TryGetValue("command", out var cmd) == true ? cmd.GetString() : null;
        var workingDir = arguments?.TryGetValue("working_directory", out var wd) == true ? wd.GetString() : null;
        return new BashProcessEntity(command: command, workingDirectory: workingDir, toolUseId: toolUseId, spanId: spanId, sessionId: sessionId);
    }

    private static WebFetchEntity CreateWebEntity(
        Dictionary<string, JsonElement>? arguments, string? toolUseId, string? spanId, ObjectId sessionId)
    {
        var url = arguments?.TryGetValue("url", out var u) == true ? u.GetString() : null;
        return new WebFetchEntity(url: url, toolUseId: toolUseId, spanId: spanId, sessionId: sessionId);
    }

    private static SleepEntity CreateSleepEntity(
        Dictionary<string, JsonElement>? arguments, string? toolUseId, string? spanId, ObjectId sessionId)
    {
        var duration = arguments?.TryGetValue("duration_seconds", out var d) == true ? d.GetInt32() : 0;
        var reason = arguments?.TryGetValue("reason", out var r) == true ? r.GetString() : null;
        return new SleepEntity(durationSeconds: duration, reason: reason, toolUseId: toolUseId, spanId: spanId, sessionId: sessionId);
    }

    private static ReplSessionEntity CreateReplEntity(
        Dictionary<string, JsonElement>? arguments, string? toolUseId, string? spanId, ObjectId sessionId)
    {
        var language = arguments?.TryGetValue("language", out var l) == true ? l.GetString() ?? "csharp" : "csharp";
        return new ReplSessionEntity(language: language, toolUseId: toolUseId, spanId: spanId, sessionId: sessionId);
    }

    private static UserInteractionEntity CreateUserInteractionEntity(
        Dictionary<string, JsonElement>? arguments, string? toolUseId, string? spanId, ObjectId sessionId)
    {
        var question = arguments?.TryGetValue("question", out var q) == true ? q.GetString() : null;
        return new UserInteractionEntity(question: question, toolUseId: toolUseId, spanId: spanId, sessionId: sessionId);
    }
}
