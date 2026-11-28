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

    /// <summary>
    /// 需要回 <c>control_response</c> 的请求 ID（TASK142-B/143）— interrupt、set_permission_mode
    /// 以及「带 request_id 但未实现/不合法」的控制行都填这里。
    /// <para>协议要求：只要对方给了 request_id，就必须回包 —— 基准侧对未知 subtype 也回 error
    /// （print.ts：<c>sendControlResponseError</c>，注释写明「否则调用方会一直等一条不会来的应答」）。</para>
    /// </summary>
    public string? ControlRequestId { get; init; }

    /// <summary>true = 取消当前在途回合（control_request.interrupt）。</summary>
    public bool IsInterrupt { get; init; }

    /// <summary>set_permission_mode 的目标权限模式（非空表示要改权限模式）。</summary>
    public string? RequestedPermissionMode { get; init; }

    /// <summary>
    /// 控制指令的拒绝原因 — 非空时调用方**必须**用 <see cref="ControlRequestId"/> 回
    /// <c>control_response.error</c>，不能只写 stderr（否则客户端永久等待）。
    /// </summary>
    public string? ControlRejection { get; init; }

    /// <summary>构造一条 interrupt 控制行。</summary>
    /// <param name="requestId">控制请求 ID（用于回 control_response）。</param>
    /// <returns>解析结果。</returns>
    public static SdkStreamInputLine Interrupt(string requestId) =>
        new(null, null) { ControlRequestId = requestId, IsInterrupt = true };

    /// <summary>构造一条 set_permission_mode 控制行。</summary>
    /// <param name="requestId">控制请求 ID（用于回 control_response）。</param>
    /// <param name="mode">目标权限模式（协议字符串，由应用器解析）。</param>
    /// <returns>解析结果。</returns>
    public static SdkStreamInputLine SetPermissionMode(string requestId, string mode) =>
        new(null, null) { ControlRequestId = requestId, RequestedPermissionMode = mode };

    /// <summary>构造一条「带 request_id 但无法满足」的控制行（回 error，不尝试应用）。</summary>
    /// <param name="requestId">控制请求 ID（必须带回，否则客户端无法关联）。</param>
    /// <param name="rejection">拒绝原因。</param>
    /// <returns>解析结果。</returns>
    public static SdkStreamInputLine ControlFailure(string requestId, string rejection) =>
        new(null, null) { ControlRequestId = requestId, ControlRejection = rejection };
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
    /// 已实现 interrupt（TASK142-B）与 set_permission_mode（TASK143），
    /// 其余子类型给诱导式诊断（说明当前支持什么、下一步怎么做）。
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

        if (control.Request is { } controlRequest
            && string.Equals(controlRequest.Subtype, "set_permission_mode", StringComparison.OrdinalIgnoreCase)) {
            // 缺 mode 无法应用，但**必须**带 request_id 回 error —— 基准侧 handleSetPermissionMode
            // 对每种不满足都回 control_response.error，只写 stderr 会让客户端一直等。
            if (string.IsNullOrWhiteSpace(controlRequest.Mode))
                return SdkStreamInputLine.ControlFailure(
                    control.RequestId,
                    $"set_permission_mode 缺 request.mode；合法值：{PermissionModeApplierFactory.ValidPermissionModes}。");

            return SdkStreamInputLine.SetPermissionMode(control.RequestId, controlRequest.Mode);
        }

        // 未实现的子类型也回 error（基准侧 print.ts 末尾同此：否则调用方等一条永远不来的应答）。
        return SdkStreamInputLine.ControlFailure(
            control.RequestId,
            $"Unsupported control request subtype: {control.Request?.Subtype ?? "(missing)"}；当前支持 interrupt 与 set_permission_mode。");
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
