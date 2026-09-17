namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// Bash 防御服务 — 薄编排层，注入各 node，适配 node 到 <see cref="BashDefenseStepAsync"/>。
/// <para>
/// 一切皆为 node/插件（ADR 0104）：每个 node 是独立公共对象，可被任意工具注入使用。
/// 本服务职责：① 注入 node ② 将 node 结果包装为 ToolResult + 诊断 ③ 提供 Begin() 入口。
/// </para>
/// <para>
/// 调用方组装示例（LINQ 链式 + 有名函数）：
/// <code>
/// var (ctx, rejection) = await _bashDefenseService
///     .Begin(command, workDir, shellKind, confirmMode, confirmedCmd, argvHash)
///     .Then(_bashDefenseService.CheckRetainedDevice)
///     .Then(_bashDefenseService.CheckRedirectWhitelist)
///     .Then(_bashDefenseService.RequireArgvHash)
///     .ExecuteAsync(ct);
/// </code>
/// </para>
/// </summary>
[Register(typeof(BashDefenseService), ServiceLifetime.Singleton)]
public sealed class BashDefenseService
{
    private readonly DangerousCommandNode _dangerousCommandNode;
    private readonly RetainedDeviceNode _retainedDeviceNode;
    private readonly ArgvHashNode _argvHashNode;
    private readonly RedirectWhitelistNode _redirectWhitelistNode;
    private readonly StrictParseNode _strictParseNode;

    /// <summary>
    /// 构造 Bash 防御服务
    /// </summary>
    /// <param name="dangerousCommandNode">危险命令检测 node</param>
    /// <param name="retainedDeviceNode">保留设备名检测 node</param>
    /// <param name="argvHashNode">argv hash 校验 node</param>
    /// <param name="redirectWhitelistNode">重定向白名单 node</param>
    /// <param name="strictParseNode">严格解析检测 node</param>
    public BashDefenseService(
        DangerousCommandNode dangerousCommandNode,
        RetainedDeviceNode retainedDeviceNode,
        ArgvHashNode argvHashNode,
        RedirectWhitelistNode redirectWhitelistNode,
        StrictParseNode strictParseNode)
    {
        _dangerousCommandNode = dangerousCommandNode ?? throw new ArgumentNullException(nameof(dangerousCommandNode));
        _retainedDeviceNode = retainedDeviceNode ?? throw new ArgumentNullException(nameof(retainedDeviceNode));
        _argvHashNode = argvHashNode ?? throw new ArgumentNullException(nameof(argvHashNode));
        _redirectWhitelistNode = redirectWhitelistNode ?? throw new ArgumentNullException(nameof(redirectWhitelistNode));
        _strictParseNode = strictParseNode ?? throw new ArgumentNullException(nameof(strictParseNode));
    }

    /// <summary>
    /// 开始构建 Bash 防御链。返回 <see cref="BashDefense"/> 构建器，链式追加防御步骤。
    /// </summary>
    /// <param name="command">原始命令字符串</param>
    /// <param name="workingDirectory">工作目录路径</param>
    /// <param name="shellKind">Shell 类型</param>
    /// <param name="confirmMode">确认模式（默认 None）</param>
    /// <param name="confirmedCommand">已确认命令（二次确认场景，默认 null）</param>
    /// <param name="argvHash">argv hash（防意图反推，默认 null）</param>
    /// <returns>Bash 防御链构建器</returns>
    public BashDefense Begin(
        string command,
        string workingDirectory,
        SystemActuatorKind shellKind,
        GuardConfirmMode confirmMode = GuardConfirmMode.None,
        string? confirmedCommand = null,
        string? argvHash = null)
        => BashDefense.Begin(command, workingDirectory, shellKind, confirmMode, confirmedCommand, argvHash);

    // ════════════════════════════════════════════════════════════════════
    //  防御步骤（委托各 node，适配 ToolResult）— 有名函数，非 lambda
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 危险命令检测 — 委托 <see cref="DangerousCommandNode"/>，分类命令危险等级，Dangerous 级直接拒绝。
    /// <para>
    /// MTP 扰动纵深防御约束第4条：Dangerous 级直接拒绝。
    /// 补 CommandInterceptionDispatcher 缺失：直接 git -c / git --exec-path 等命令在 Dispatcher 内被跳过。
    /// </para>
    /// </summary>
    /// <param name="ctx">Bash 防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> CheckDangerousCommand(BashDefenseContext ctx, CancellationToken ct)
    {
        var classification = _dangerousCommandNode.ClassifyDangerous(ctx.CurrentCommand);
        if (classification is null)
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildDangerousCommandRejectedDiagnostic(classification, ctx.CurrentCommand);
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// 严格解析检测 — 委托 <see cref="StrictParseNode"/>，检测未闭合引号。
    /// <para>
    /// MTP 扰动纵深防御约束第2条：结构化解析是承重墙，引号不配对直接拒绝。
    /// </para>
    /// </summary>
    /// <param name="ctx">Bash 防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> StrictParse(BashDefenseContext ctx, CancellationToken ct)
    {
        var unmatchedQuote = _strictParseNode.FindUnmatchedQuote(ctx.CurrentCommand.AsSpan());
        if (unmatchedQuote is null)
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildUnmatchedQuoteDiagnostic(unmatchedQuote.Value, ctx.CurrentCommand);
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// 保留设备名检测 — 委托 <see cref="RetainedDeviceNode"/>，检测到重定向到保留设备名构建拒绝诊断。
    /// <para>
    /// 拒绝消息封死替代路径（MTP 扰动纵深防御约束第5条）：
    /// <list type="bullet">
    /// <item>显式列出同类禁止写法（&gt;NUL / &gt;nul. / &gt;con）</item>
    /// <item>提示疑似 MTP 扰动，建议丢弃当前命令完整重新生成</item>
    /// <item>不在错误字符串上局部修补（约束第1条）</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="ctx">Bash 防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> CheckRetainedDevice(BashDefenseContext ctx, CancellationToken ct)
    {
        var deviceName = _retainedDeviceNode.FindRetainedDevice(ctx.CurrentCommand);
        if (deviceName is null)
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildRetainedDeviceRejectedDiagnostic(deviceName);
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// argv hash 二次确认 — 委托 <see cref="ArgvHashNode"/>，防意图反推（约束第9条）。
    /// <para>
    /// 三阶段逻辑：
    /// <list type="number">
    /// <item>未开启 AntiCharLossConfirm 模式 → 直接通过</item>
    /// <item>第一轮（ConfirmedCommand 为 null）→ 拒绝，要求再次输入并回显 hash</item>
    /// <item>第二轮 → 校验命令匹配 + hash 匹配，任一不匹配拒绝</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="ctx">Bash 防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> RequireArgvHash(BashDefenseContext ctx, CancellationToken ct)
    {
        if (ctx.ConfirmMode != GuardConfirmMode.AntiCharLossConfirm)
            return ValueTask.FromResult<ToolResult?>(null);

        if (ctx.ConfirmedCommand is null)
            return ValueTask.FromResult<ToolResult?>(BuildFirstRoundRejection(ctx.CurrentCommand));

        if (!string.Equals(ctx.ConfirmedCommand, ctx.CurrentCommand, StringComparison.Ordinal))
            return ValueTask.FromResult<ToolResult?>(BuildCommandMismatchRejection(ctx.ConfirmedCommand, ctx.CurrentCommand));

        if (!_argvHashNode.ValidateArgvHash(ctx.CurrentCommand, ctx.ArgvHash))
            return ValueTask.FromResult<ToolResult?>(BuildHashMismatchRejection(ctx.ArgvHash, _argvHashNode.ComputeArgvHash(ctx.CurrentCommand)));

        return ValueTask.FromResult<ToolResult?>(null);
    }

    /// <summary>
    /// 重定向白名单检测 — 委托 <see cref="RedirectWhitelistNode"/>，重定向目标必须在工作区内或 /dev/null。
    /// <para>
    /// MTP 扰动纵深防御约束第3条：重定向走白名单，不枚举危险名。
    /// 约束第8条：路径白名单在规范化之后判定。
    /// </para>
    /// </summary>
    /// <param name="ctx">Bash 防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> CheckRedirectWhitelist(BashDefenseContext ctx, CancellationToken ct)
    {
        var result = _redirectWhitelistNode.CheckWhitelist(ctx.CurrentCommand, ctx.WorkingDirectory);
        if (result.IsWhitelisted)
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildRedirectWhitelistRejectedDiagnostic(result.ViolatingTarget!, result.NormalizedTarget, ctx.WorkingDirectory);
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    // ════════════════════════════════════════════════════════════════════
    //  诊断模板构建（封死替代路径 + MTP 扰动提示）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 构建危险命令拒绝诊断 — Dangerous 级直接拒绝 + 封死替代路径 + 提示 MTP 扰动重生成。
    /// </summary>
    /// <param name="classification">危险分类结果</param>
    /// <param name="command">原始命令</param>
    /// <returns>拒绝诊断</returns>
    private static ToolDiagnostic BuildDangerousCommandRejectedDiagnostic(
        DangerClassificationResult classification, string command)
        => ToolDiagnostic.Create(
            "JCC9011",
            $"命令被分类为 Dangerous 级（直接拒绝）— {classification.Details ?? classification.RiskType.ToString()}" +
            $"\n\n原始命令：{command}" +
            $"\n\n✅ 推荐写法：移除危险参数，使用安全的等价命令" +
            $"\n❌ 禁止尝试：换用变体绕过 —— 全部会被拦截" +
            $"\n⚠️ 疑似 MTP 扰动：建议丢弃当前命令，完整重新生成，不要在错误字符串上局部修补",
            "风险类型", classification.RiskType.ToString(),
            "命令被分类为 Dangerous 级，直接拒绝。请移除危险参数或使用安全等价命令。");

    /// <summary>
    /// 构建保留设备名拒绝诊断 — 封死替代路径 + 提示 MTP 扰动重生成。
    /// </summary>
    /// <param name="deviceName">检测到的保留设备名</param>
    /// <returns>拒绝诊断</returns>
    private static ToolDiagnostic BuildRetainedDeviceRejectedDiagnostic(string deviceName)
        => ToolDiagnostic.Create(
            "JCC9005",
            $"检测到重定向到 Windows 保留设备名 '{deviceName}' — 在 git bash 中会创建名为 '{deviceName}' 的文件，" +
            $"常规命令难以删除。" +
            $"\n\n✅ 推荐写法：> /dev/null（丢弃输出）或 touch {deviceName}（确需创建文件）" +
            $"\n❌ 禁止尝试：>NUL / >nul. / >con / 先建后删 —— 全部会被拦截" +
            $"\n⚠️ 疑似 MTP 扰动：建议丢弃当前命令，完整重新生成，不要在错误字符串上局部修补",
            "设备名", deviceName,
            "请改用 > /dev/null 丢弃输出，或使用 touch 创建文件。" +
            "禁止尝试 >NUL / >nul. / >con 等变体，全部会被拦截。" +
            "疑似 MTP 扰动时建议完整重新生成命令。");

    /// <summary>
    /// 构建第一轮拒绝诊断 — 要求再次输入并回显 argv hash。
    /// </summary>
    private ToolResult BuildFirstRoundRejection(string command)
    {
        var expectedHash = _argvHashNode.ComputeArgvHash(command);
        var diag = ToolDiagnostic.Create(
            "JCC9006",
            $"防丢字符二次确认 — MTP 加速推理可能丢字符/乱入字符导致命令变形。" +
            $"\n\n解析结果：{command}" +
            $"\n确认码：#{expectedHash}" +
            $"\n\n请再次输入完全相同的命令，并附带确认码 #{expectedHash} 以确认执行。" +
            $"\n⚠️ 确认码由命令解析结果计算得出，无法从意图反推。",
            "命令", command,
            "请再次输入同样命令并附带确认码。确认码由解析结果计算，防意图反推。");
        return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
    }

    /// <summary>
    /// 构建命令不匹配拒绝诊断 — 第二轮命令与第一轮不一致。
    /// </summary>
    private static ToolResult BuildCommandMismatchRejection(string confirmedCommand, string currentCommand)
    {
        var diag = ToolDiagnostic.Create(
            "JCC9007",
            $"二次确认命令不匹配 — 疑似 MTP 扰动导致命令变形。" +
            $"\n\n第一轮命令：{confirmedCommand}" +
            $"\n第二轮命令：{currentCommand}" +
            $"\n\n⚠️ 建议丢弃当前命令，完整重新生成，不要在错误字符串上局部修补。",
            "第一轮", confirmedCommand,
            "两轮命令不一致，建议完整重新生成。");
        return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
    }

    /// <summary>
    /// 构建 hash 不匹配拒绝诊断 — 防意图反推（约束第9条）。
    /// </summary>
    private static ToolResult BuildHashMismatchRejection(string? providedHash, string expectedHash)
    {
        var diag = ToolDiagnostic.Create(
            "JCC9008",
            $"确认码不匹配 — 防意图反推校验失败。" +
            $"\n\n提供的确认码：{providedHash ?? "(未提供)"}" +
            $"\n期望的确认码：#{expectedHash}" +
            $"\n\n⚠️ 确认码必须由命令解析结果计算得出。如果不匹配，说明命令在生成过程中被扰动。" +
            $"\n建议丢弃当前命令，完整重新生成。",
            "提供", providedHash ?? "(null)",
            "确认码不匹配，建议完整重新生成命令。");
        return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
    }

    /// <summary>
    /// 构建重定向白名单拒绝诊断 — 封死替代路径 + 提示 MTP 扰动重生成。
    /// </summary>
    private static ToolDiagnostic BuildRedirectWhitelistRejectedDiagnostic(
        string violatingTarget, string? normalizedTarget, string workingDirectory)
        => ToolDiagnostic.Create(
            "JCC9009",
            $"重定向目标 '{violatingTarget}' 不在白名单内 — 只允许工作区路径或 /dev/null。" +
            (normalizedTarget is not null ? $"\n规范化后路径：{normalizedTarget}" : "") +
            $"\n工作目录：{workingDirectory}" +
            $"\n\n✅ 推荐写法：> /dev/null（丢弃输出）或 > ./output.txt（工作区内文件）" +
            $"\n❌ 禁止尝试：>../../etc/passwd / >$HOME/.bashrc / >nul —— 全部会被拦截" +
            $"\n⚠️ 疑似 MTP 扰动：建议丢弃当前命令，完整重新生成，不要在错误字符串上局部修补",
            "重定向目标", violatingTarget,
            "重定向目标必须在工作区内或 /dev/null。禁止重定向到工作区外路径或保留设备名。");

    /// <summary>
    /// 构建未闭合引号拒绝诊断 — 封死替代路径 + 提示 MTP 扰动重生成。
    /// </summary>
    /// <param name="quoteChar">未闭合的引号字符</param>
    /// <param name="command">原始命令</param>
    /// <returns>拒绝诊断</returns>
    private static ToolDiagnostic BuildUnmatchedQuoteDiagnostic(char quoteChar, string command)
        => ToolDiagnostic.Create(
            "JCC9010",
            $"检测到未闭合的引号 '{quoteChar}' — 命令解析失败，不进入下游执行。" +
            $"\n\n原始命令：{command}" +
            $"\n\n✅ 推荐写法：确保引号配对，如 echo \"hello\" 或 echo 'hello'" +
            $"\n❌ 禁止尝试：忽略引号不配对直接执行 —— 会导致参数解析错误" +
            $"\n⚠️ 疑似 MTP 扰动：建议丢弃当前命令，完整重新生成，不要在错误字符串上局部修补",
            "引号", quoteChar.ToString(),
            "请确保引号配对后重新执行。疑似 MTP 扰动时建议完整重新生成命令。");
}
