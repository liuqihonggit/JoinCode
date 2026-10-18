namespace JoinCode.Gui.WebView;

/// <summary>
/// WebUI 桥接协议（ADR 0141）：.NET ⇄ Web 类型化消息面。
/// 判别字段 "type" + 类型内联字段；<see cref="BridgeJsonContext"/> 源生成双向转换（NativeAOT 安全，ADR 0133）。
/// 与 <c>webui/js/bridge.js</c> 的消息注册对照维护；新增消息类型须双侧同步 + 单测 roundtrip（BridgeProtocolTests）。
/// </summary>
public static class BridgeProtocol {
    /// <summary>序列化桥接消息为 JSON 文本（host→web 的 PostWebMessageAsJson 载荷）</summary>
    public static string Serialize(BridgeMessage message) =>
        JsonSerializer.Serialize(message, BridgeJsonContext.Default.BridgeMessage);

    /// <summary>解析 web→host 消息（未知 type / 非法 JSON fail-closed 返回 false，不抛出）</summary>
    public static bool TryParse(string json, [NotNullWhen(true)] out BridgeMessage? message) {
        try {
            message = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeMessage);
            return message is not null;
        } catch (JsonException) {
            message = null;
            return false;
        }
    }
}

/// <summary>桥接消息基类（判别字段 type，取值见各 <see cref="JsonDerivedTypeAttribute"/>）</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WebReadyMessage), "web/ready")]
[JsonDerivedType(typeof(ChatSendMessage), "chat/send")]
[JsonDerivedType(typeof(ChatStopMessage), "chat/stop")]
[JsonDerivedType(typeof(PermissionResponseMessage), "permission/response")]
[JsonDerivedType(typeof(SmokePongMessage), "smoke/pong")]
[JsonDerivedType(typeof(AppInitMessage), "app/init")]
[JsonDerivedType(typeof(ChatDeltaMessage), "chat/delta")]
[JsonDerivedType(typeof(ChatDoneMessage), "chat/done")]
[JsonDerivedType(typeof(PermissionRequestMessage), "permission/request")]
[JsonDerivedType(typeof(SmokePingMessage), "smoke/ping")]
[JsonDerivedType(typeof(AppErrorMessage), "app/error")]
[JsonDerivedType(typeof(SessionsListMessage), "sessions/list")]
[JsonDerivedType(typeof(SessionSelectMessage), "session/select")]
[JsonDerivedType(typeof(WebSessionsRenderedMessage), "web/sessions-rendered")]
[JsonDerivedType(typeof(ChatHistoryMessage), "chat/history")]
public abstract record BridgeMessage;

/// <summary>web→host：页面脚本就绪（握手起点，host 收到后回 app/init）</summary>
public sealed record WebReadyMessage : BridgeMessage;

/// <summary>web→host：用户发送提示词</summary>
/// <param name="Prompt">提示词全文</param>
public sealed record ChatSendMessage(string Prompt) : BridgeMessage;

/// <summary>web→host：用户请求停止本轮生成（宿主取消在途流，已产出的内容保留）</summary>
public sealed record ChatStopMessage : BridgeMessage;

/// <summary>web→host：权限确认结果</summary>
/// <param name="Id">权限请求标识（对应 permission/request）</param>
/// <param name="Allow">是否放行</param>
public sealed record PermissionResponseMessage(string Id, bool Allow) : BridgeMessage;

/// <summary>web→host：冒烟回环应答</summary>
/// <param name="Nonce">回环随机数（须与 smoke/ping 一致）</param>
public sealed record SmokePongMessage(string Nonce) : BridgeMessage;

/// <summary>host→web：初始化（握手应答，主题与版本注入）</summary>
/// <param name="Theme">主题（dark/light）</param>
/// <param name="Version">应用版本</param>
public sealed record AppInitMessage(string Theme, string Version) : BridgeMessage;

/// <summary>host→web：会话文本增量</summary>
/// <param name="Text">增量文本</param>
public sealed record ChatDeltaMessage(string Text) : BridgeMessage;

/// <summary>host→web：会话文本结束（本轮唯一收尾信号，web 侧据此恢复发送按钮）</summary>
/// <param name="Stopped">是否由用户主动停止（true=用户点了停止/按 Esc，非故障语义；false=正常完成）</param>
public sealed record ChatDoneMessage(bool Stopped) : BridgeMessage;

/// <summary>host→web：权限确认请求</summary>
/// <param name="Id">请求标识（web 经 permission/response 回传）</param>
/// <param name="Command">触发的命令/工具名</param>
/// <param name="Description">面向用户的说明文案</param>
public sealed record PermissionRequestMessage(string Id, string Command, string Description) : BridgeMessage;

/// <summary>host→web：冒烟回环发起</summary>
/// <param name="Nonce">回环随机数</param>
public sealed record SmokePingMessage(string Nonce) : BridgeMessage;

/// <summary>host→web：错误指引（为什么拒绝 / 是什么触发 / 接下来怎么做）</summary>
/// <param name="Reason">原因</param>
/// <param name="Guidance">下一步正确做法</param>
public sealed record AppErrorMessage(string Reason, string Guidance) : BridgeMessage;

/// <summary>host→web：会话列表快照（web/ready 后宿主读取 GuiSessionStore 下发；空列表表示无历史会话）</summary>
/// <param name="Sessions">会话摘要列表（按最近修改倒序）</param>
public sealed record SessionsListMessage(IReadOnlyList<SessionSummaryDto> Sessions) : BridgeMessage;

/// <summary>会话摘要（sessions/list 传输单元）</summary>
/// <param name="Id">会话标识</param>
/// <param name="Title">显示标题（宿主保证非空，缺省回填会话 Id）</param>
public sealed record SessionSummaryDto(string Id, string Title);

/// <summary>web→host：用户点选侧栏会话</summary>
/// <param name="Id">会话标识</param>
public sealed record SessionSelectMessage(string Id) : BridgeMessage;

/// <summary>web→host：会话列表渲染完成（冒烟验收钩子；宿主据此确认真实数据链路通）</summary>
/// <param name="Count">实际渲染的会话行数</param>
public sealed record WebSessionsRenderedMessage(int Count) : BridgeMessage;

/// <summary>host→web：历史会话消息快照（session/select 后宿主读取持久化下发；空列表表示空会话）</summary>
/// <param name="Messages">按时间顺序的历史消息</param>
public sealed record ChatHistoryMessage(IReadOnlyList<ChatHistoryEntry> Messages) : BridgeMessage;

/// <summary>历史消息条目（chat/history 传输单元；Role 为持久化原始值 user/assistant/tool 等）</summary>
/// <param name="Role">角色原始值</param>
/// <param name="Content">消息正文</param>
/// <param name="Timestamp">本机墙钟时间（宿主已 ToLocalTime；序列化无时区后缀，Web 侧按本地解析显示 HH:mm）</param>
public sealed record ChatHistoryEntry(string Role, string Content, DateTime Timestamp);

/// <summary>桥接协议源生成上下文（属性 camelCase；判别字段 type 由 [JsonPolymorphic] 注入）</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BridgeMessage))]
internal partial class BridgeJsonContext : JsonSerializerContext;
