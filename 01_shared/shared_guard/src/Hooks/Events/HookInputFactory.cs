namespace Core.Hooks.Events;

/// <summary>
/// HookInput 工厂方法（依赖 HooksJsonContext，保留在 Guard 内部）
/// </summary>
public static class HookInputFactory
{
    public static HookInput ForToolUse(
        HookEvent hookEvent,
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string? sessionId = null)
    {
        return new HookInput
        {
            Event = hookEvent,
            Matcher = toolName,
            ToolName = toolName,
            ToolUseId = toolUseId,
            SessionId = sessionId,
            Payload = new Dictionary<string, JsonElement>
            {
                [nameof(toolName)] = JsonElementHelper.FromString(toolName),
                [nameof(toolUseId)] = JsonElementHelper.FromString(toolUseId),
                ["input"] = JsonSerializer.SerializeToElement(input, HooksJsonContext.Default.DictionaryStringJsonElement)
            }
        };
    }

    public static HookInput ForSession(
        HookEvent hookEvent,
        string source,
        Dictionary<string, JsonElement>? additionalPayload = null,
        string? sessionId = null)
    {
        var payload = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonElementHelper.FromString(source)
        };

        if (additionalPayload != null)
        {
            foreach (var kvp in additionalPayload)
            {
                payload[kvp.Key] = kvp.Value;
            }
        }

        return new HookInput
        {
            Event = hookEvent,
            SessionId = sessionId,
            Payload = payload
        };
    }

    public static HookInput ForPermissionRequest(
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string? permissionMode = null,
        List<PermissionUpdate>? suggestions = null,
        string? sessionId = null)
    {
        return new HookInput
        {
            Event = HookEvent.PermissionRequest,
            Matcher = toolName,
            ToolName = toolName,
            ToolUseId = toolUseId,
            SessionId = sessionId,
            Payload = new Dictionary<string, JsonElement>
            {
                [nameof(toolName)] = JsonElementHelper.FromString(toolName),
                [nameof(toolUseId)] = JsonElementHelper.FromString(toolUseId),
                ["input"] = JsonSerializer.SerializeToElement(input, HooksJsonContext.Default.DictionaryStringJsonElement),
                ["permissionMode"] = JsonElementHelper.FromString(permissionMode),
                ["suggestions"] = suggestions != null
                    ? JsonSerializer.SerializeToElement(suggestions, HooksJsonContext.Default.ListPermissionUpdate)
                    : JsonElementHelper.NullElement()
            }
        };
    }
}
