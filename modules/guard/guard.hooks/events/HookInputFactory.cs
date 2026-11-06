namespace Core.Hooks.Events;

/// <summary>
/// HookInput 工厂方法（依赖 HooksJsonContext，保留在 Guard 内部）
/// </summary>
public static class HookInputFactory {
    /// <summary>
    /// 构造工具使用事件的 HookInput
    /// </summary>
    /// <param name="hookEvent">钩子事件类型</param>
    /// <param name="toolName">工具名称</param>
    /// <param name="toolUseId">工具调用唯一标识</param>
    /// <param name="input">工具输入参数</param>
    /// <param name="sessionId">可选的会话标识</param>
    /// <returns>组装完成的 HookInput 实例</returns>
    public static HookInput ForToolUse(
        HookEvent hookEvent,
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string? sessionId = null) {
        return new HookInput {
            Event = hookEvent,
            Matcher = toolName,
            ToolName = toolName,
            ToolUseId = toolUseId,
            SessionId = sessionId,
            Payload = new Dictionary<string, JsonElement> {
                [nameof(toolName)] = JsonElementHelper.FromString(toolName),
                [nameof(toolUseId)] = JsonElementHelper.FromString(toolUseId),
                ["input"] = JsonSerializer.SerializeToElement(input, HooksJsonContext.Default.DictionaryStringJsonElement)
            }
        };
    }

    /// <summary>
    /// 构造会话级事件的 HookInput
    /// </summary>
    /// <param name="hookEvent">钩子事件类型</param>
    /// <param name="source">触发来源</param>
    /// <param name="additionalPayload">附加负载,可选</param>
    /// <param name="sessionId">可选的会话标识</param>
    /// <returns>组装完成的 HookInput 实例</returns>
    public static HookInput ForSession(
        HookEvent hookEvent,
        string source,
        Dictionary<string, JsonElement>? additionalPayload = null,
        string? sessionId = null) {
        var payload = new Dictionary<string, JsonElement> {
            ["source"] = JsonElementHelper.FromString(source)
        };

        if (additionalPayload != null) {
            foreach (var kvp in additionalPayload) {
                payload[kvp.Key] = kvp.Value;
            }
        }

        return new HookInput {
            Event = hookEvent,
            SessionId = sessionId,
            Payload = payload
        };
    }

    /// <summary>
    /// 构造权限请求事件的 HookInput
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="toolUseId">工具调用唯一标识</param>
    /// <param name="input">工具输入参数</param>
    /// <param name="permissionMode">可选的权限模式</param>
    /// <param name="suggestions">可选的权限建议列表</param>
    /// <param name="sessionId">可选的会话标识</param>
    /// <returns>组装完成的 HookInput 实例</returns>
    public static HookInput ForPermissionRequest(
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string? permissionMode = null,
        List<PermissionUpdate>? suggestions = null,
        string? sessionId = null) {
        return new HookInput {
            Event = HookEvent.PermissionRequest,
            Matcher = toolName,
            ToolName = toolName,
            ToolUseId = toolUseId,
            SessionId = sessionId,
            Payload = new Dictionary<string, JsonElement> {
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