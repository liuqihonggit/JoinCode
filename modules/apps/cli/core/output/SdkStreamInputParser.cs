namespace JoinCode.Cli.Output;

/// <summary>
/// SDK 流式输入行解析结果（TASK138）— 一行 stdin 要么产出一段用户文本，要么产出一条诊断。
/// <para>Text 非空 = 本回合输入；Text 为 null 且 Diagnostic 为 null = 空行（静默忽略）；
/// Diagnostic 非空 = 该行不产生回合（调用方写 stderr 后继续）。</para>
/// </summary>
/// <param name="Text">本回合用户文本。</param>
/// <param name="Diagnostic">诊断（写给 stderr，不中断循环）。</param>
public sealed record SdkStreamInputLine(string? Text, string? Diagnostic) {
    /// <summary>空行（既不产生回合也不产生诊断）。</summary>
    public static SdkStreamInputLine Blank { get; } = new(null, null);

    /// <summary>诊断为唯一产出。</summary>
    /// <param name="diagnostic">诊断文本。</param>
    /// <returns>解析结果。</returns>
    public static SdkStreamInputLine Failed(string diagnostic) => new(null, diagnostic);

    /// <summary>本回合用户文本。</summary>
    /// <param name="text">用户文本。</param>
    /// <returns>解析结果。</returns>
    public static SdkStreamInputLine Turn(string text) => new(text, null);

    /// <summary>control_request / interrupt 的请求 ID（TASK142-B）；非空表示要取消当前回合。</summary>
    public string? InterruptRequestId { get; init; }

    /// <summary>构造一条 interrupt 控制行。</summary>
    /// <param name="requestId">控制请求 ID（用于回 control_response）。</param>
    /// <returns>解析结果。</returns>
    public static SdkStreamInputLine Interrupt(string requestId) => new(null, null) { InterruptRequestId = requestId };
}

/// <summary>
/// SDK 流式输入行解析（TASK138）— 纯函数，便于单测：把一行 NDJSON 解释成「本回合的用户文本」
/// 或一条诊断（诊断写给 stderr，不打断循环）。
/// </summary>
public static class SdkStreamInputParser {
    /// <summary>
    /// 解释一行 stdin 输入。
    /// </summary>
    /// <param name="line">原始行（可能为空行）。</param>
    /// <returns>解析结果（见 <see cref="SdkStreamInputLine"/>）。</returns>
    public static SdkStreamInputLine Interpret(string? line) {
        if (string.IsNullOrWhiteSpace(line))
            return SdkStreamInputLine.Blank;

        SdkStreamInputMessage? message;
        try {
            message = System.Text.Json.JsonSerializer.Deserialize(
                line, CliOutputJsonContext.Default.SdkStreamInputMessage);
        } catch (System.Text.Json.JsonException ex) {
            return SdkStreamInputLine.Failed(
                $"[INPUT] 无法解析的 NDJSON 行（{ex.Message}）；已跳过。每行需是 type=user、message.content 为字符串或内容块数组的对象。");
        }

        if (message is null)
            return SdkStreamInputLine.Failed("[INPUT] 空消息，已跳过。");

        // TASK142-B：控制行与用户行走两条不同通道 —— 控制行要能打断在途回合。
        if (string.Equals(message.Type, "control_request", StringComparison.OrdinalIgnoreCase))
            return InterpretControlRequest(line);

        // 基准侧 structuredIO.processLine 对这两类是静默忽略（本仓不发 control_request，
        // 没有待匹配的应答；环境变量更新在本轮无消费方）。
        if (message.Type is "control_response" or "keep_alive" or "update_environment_variables")
            return SdkStreamInputLine.Blank;

        if (!string.Equals(message.Type, "user", StringComparison.OrdinalIgnoreCase)) {
            return SdkStreamInputLine.Failed(
                $"[INPUT] 忽略 type=\"{message.Type}\" 的消息：当前仅支持 type=\"user\"（控制协议未实现，见 TASK138）");
        }

        var content = message.Message?.Content;
        if (content is not { } element
            || element.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined) {
            return SdkStreamInputLine.Failed("[INPUT] user 消息缺 content，已跳过。");
        }

        return element.ValueKind switch {
            System.Text.Json.JsonValueKind.String => FromText(element.GetString()),
            System.Text.Json.JsonValueKind.Array => FromText(JoinTextBlocks(element)),
            _ => SdkStreamInputLine.Failed(
                $"[INPUT] 不支持的 content 类型 {element.ValueKind}（支持字符串或内容块数组），已跳过。"),
        };
    }

    /// <summary>
    /// 解释 control_request — 形状取自基准侧 SDKControlRequestSchema；
    /// 本轮只实现 interrupt，其余子类型给诱导式诊断（说明当前支持什么、下一步怎么做）。
    /// </summary>
    /// <param name="line">原始 NDJSON 行</param>
    /// <returns>解析结果</returns>
    private static SdkStreamInputLine InterpretControlRequest(string line) {
        SdkStreamControlRequest? control;
        try {
            control = System.Text.Json.JsonSerializer.Deserialize(
                line, CliOutputJsonContext.Default.SdkStreamControlRequest);
        } catch (System.Text.Json.JsonException ex) {
            return SdkStreamInputLine.Failed($"[INPUT] control_request 无法解析（{ex.Message}）；已跳过。");
        }

        if (control is null || string.IsNullOrWhiteSpace(control.RequestId))
            return SdkStreamInputLine.Failed("[INPUT] control_request 缺 request_id，已跳过；请补上 request_id 以便回 control_response。");

        if (string.Equals(control.Request?.Subtype, "interrupt", StringComparison.OrdinalIgnoreCase))
            return SdkStreamInputLine.Interrupt(control.RequestId);

        return SdkStreamInputLine.Failed(
            $"[INPUT] 暂不支持 control_request.request.subtype=\"{control.Request?.Subtype}\"，已跳过；当前仅支持 interrupt，请用 type=user 提交下一回合（若是 interrupt 请补 request.subtype）。");
    }

    /// <summary>拼接内容块数组中的 text 块（其他块类型忽略 — 图片/工具块暂无输入通道）。</summary>
    private static string JoinTextBlocks(System.Text.Json.JsonElement array) {
        var parts = new List<string>(array.GetArrayLength());
        foreach (var block in array.EnumerateArray()) {
            if (block.ValueKind != System.Text.Json.JsonValueKind.Object)
                continue;
            var text = block.TryGetProperty("text", out var textElement)
                       && textElement.ValueKind == System.Text.Json.JsonValueKind.String
                ? textElement.GetString()
                : null;
            if (!string.IsNullOrEmpty(text))
                parts.Add(text);
        }
        return string.Concat(parts);
    }

    /// <summary>空白文本视为无输入（避免用空提示词发起一次必然失败的回合）。</summary>
    private static SdkStreamInputLine FromText(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? SdkStreamInputLine.Failed("[INPUT] user 消息文本为空，已跳过。")
            : SdkStreamInputLine.Turn(text);
}
