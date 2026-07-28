namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// SendMessageTool 提示词
/// </summary>
[ToolPrompt(ToolName = "SendMessage", Category = ToolPromptCategory.Agent, HasParameters = true)]
public static class SendMessageToolPrompt
{
    public const string ToolName = AgentToolNameConstants.AgentSendMessage;
    public const string Description = "向另一个代理发送消息";

    /// <summary>
    /// 获取工具提示词
    /// </summary>
    public static string GetPrompt(bool udsInboxEnabled = false)
    {
        var udsRow = udsInboxEnabled
            ? @"
| `""uds:/path/to.sock""` | 本地 Claude 会话的 socket（同一机器；使用 `ListPeers`） |
| `""bridge:session_...""` | 远程控制对等会话（跨机器；使用 `ListPeers`） |"
            : "";

        var udsSection = udsInboxEnabled
            ? $@"


## 跨会话

使用 `ListPeers` 发现目标，然后：

```json
{{""to"": ""uds:/tmp/cc-socks/1234.sock"", ""message"": ""check if tests pass over there""}}
{{""to"": ""bridge:session_01AbCd..."", ""message"": ""what branch are you on?""}}
```

列出的对等点是活动的，将处理你的消息 —— 没有""忙碌""状态；消息在接收方的下一个工具轮次时入队并排出。你的消息以 `<cross-session-message from=""..."">` 的形式到达。**要回复传入的消息，将其 `from` 属性复制为你的 `to`。**
"
            : "";

        return $@"# SendMessage

向另一个代理发送消息。

```json
{{""to"": ""researcher"", ""summary"": ""assign task 1"", ""message"": ""start on task #1""}}
```

| `to` | |
|---|---|
| `""researcher""` | 按名称的团队成员 |
| `""*""` | 广播给所有团队成员 —— 昂贵（与团队大小成线性关系），仅在每个人真正需要时使用 |{udsRow}

你的纯文本输出对其他代理不可见 —— 要通信，你必须调用此工具。来自团队成员的消息自动传递；你不需要检查收件箱。按名称引用团队成员，永远不要按 UUID。在转发时，不要引用原文 —— 它已经渲染给用户了。{udsSection}

## 协议响应（传统）

如果你收到带有 `type: ""shutdown_request""` 或 `type: ""plan_approval_request""` 的 JSON 消息，请用匹配的 `_response` 类型响应 —— 回显 `request_id`，将 `approve` 设置为 true/false：

```json
{{""to"": ""team-lead"", ""message"": {{""type"": ""shutdown_response"", ""request_id"": ""..."", ""approve"": true}}}}
{{""to"": ""researcher"", ""message"": {{""type"": ""plan_approval_response"", ""request_id"": ""..."", ""approve"": false, ""feedback"": ""add error handling""}}}}
```

批准关闭会终止你的进程。拒绝计划会将团队成员送回修改。除非被要求，否则不要发起 `shutdown_request`。不要发送结构化 JSON 状态消息 —— 使用 TaskUpdate。
";
    }
}
