namespace JoinCode.Cli.Output;

/// <summary>
/// SDK 控制面（TASK144 骨架）— 按 subtype 注册 <see cref="ISdkControlHandler"/>，把一条
/// <see cref="SdkControlCommand"/> 分发出去，并统一兜住三种「必须回 error」的情形：
/// 未注册 subtype、subtype 为空、handler 抛异常。
/// <para>**为什么兜底要放在这里而不是调用方**：现网（TASK142-B/143）这三类判断散在
/// <c>NonInteractiveModeRunner</c> 的 if 链里，每接一个 subtype 就多一次「记得回包」的机会。
/// 骨架把不变量收成一处，接入方只剩「注册一个 handler」。</para>
/// <para>线程安全：注册表构造后只读，无锁；分发可并发调用（handler 自身若有状态需自行保证）。</para>
/// </summary>
public sealed class SdkControlPlane {
    private readonly Dictionary<string, ISdkControlHandler> _handlers;
    private readonly string[] _subtypes;

    /// <summary>
    /// 用一组 handler 构造控制面。同一 subtype 重复注册视为装配错误 —— fail-fast，
    /// 避免「哪个 handler 生效」取决于构造顺序这种不可调试的行为。
    /// </summary>
    /// <param name="handlers">各 subtype 的 handler（顺序即 <see cref="SupportedSubtypes"/> 顺序）</param>
    /// <exception cref="ArgumentException">handler 为 null、subtype 为空或重复</exception>
    public SdkControlPlane(IEnumerable<ISdkControlHandler> handlers) {
        ArgumentNullException.ThrowIfNull(handlers);

        _handlers = new Dictionary<string, ISdkControlHandler>(0, StringComparer.OrdinalIgnoreCase);
        var subtypes = new List<string>();
        foreach (var handler in handlers) {
            if (handler is null)
                throw new ArgumentException("控制面注册了 null handler（装配错误）。", nameof(handlers));
            if (string.IsNullOrWhiteSpace(handler.Subtype))
                throw new ArgumentException(
                    $"handler {handler.GetType().Name} 的 Subtype 为空：注册键必须非空（装配错误）。", nameof(handlers));
            if (!_handlers.TryAdd(handler.Subtype, handler))
                throw new ArgumentException(
                    $"控制请求子类型 '{handler.Subtype}' 重复注册：一个 subtype 只能有一个 handler（装配错误）。", nameof(handlers));
            subtypes.Add(handler.Subtype);
        }
        _subtypes = [.. subtypes];
    }

    /// <summary>
    /// 已注册子类型（注册顺序）— 拒绝文案与诊断清单都从这里生成，**清单与能力同源**，
    /// 不再把「当前支持 interrupt 与 set_permission_mode」硬编码进字符串。
    /// </summary>
    public IReadOnlyList<string> SupportedSubtypes => _subtypes;

    /// <summary>
    /// 现网可接线的控制面：<c>interrupt</c>（TASK142-B）+ <c>set_permission_mode</c>（TASK143）。
    /// </summary>
    /// <param name="applyPermissionMode">
    /// 权限模式应用器（协议字符串 → 实际生效值，<c>null</c> = 未知值或引擎无权限系统）。
    /// 传 <c>null</c> 表示引擎没有权限系统：<c>set_permission_mode</c> 一律回 error 而非假装成功。
    /// </param>
    /// <returns>控制面</returns>
    public static SdkControlPlane CreateDefault(Func<string, string?>? applyPermissionMode) => new([
        new InterruptControlHandler(),
        new SetPermissionModeControlHandler(applyPermissionMode),
    ]);

    /// <summary>
    /// 分发一条控制请求。**返回值必定带应答**（<see cref="SdkControlOutcome.Response"/> 为 required）。
    /// </summary>
    /// <param name="command">控制请求</param>
    /// <returns>处理结果（应答 + 是否取消在途回合 + stderr 诊断）</returns>
    /// <exception cref="ArgumentException">缺 request_id —— 无从回包，应在解析阶段丢弃该行</exception>
    public SdkControlOutcome Dispatch(SdkControlCommand command) {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.RequestId))
            throw new ArgumentException(
                "控制请求缺 request_id：无从回 control_response，应在解析阶段丢弃。", nameof(command));

        if (string.IsNullOrWhiteSpace(command.Subtype))
            return SdkControlOutcome.Reject(
                command.RequestId, $"control_request 缺 request.subtype；当前支持 {DescribeSupported()}。");

        if (!_handlers.TryGetValue(command.Subtype, out var handler))
            return SdkControlOutcome.Reject(
                command.RequestId,
                $"Unsupported control request subtype: {command.Subtype}；当前支持 {DescribeSupported()}。");

        try {
            return handler.Handle(command)
                   ?? throw new InvalidOperationException($"handler '{handler.Subtype}' 返回了 null 结果");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // 最后一道网：handler 抛异常也必须回包，否则客户端等一条永远不会来的应答。
            return SdkControlOutcome.Reject(
                command.RequestId,
                $"控制请求 {command.Subtype} 处理失败：{ex.Message}；已回 error 以免客户端永久等待。");
        }
    }

    /// <summary>支持清单文案（拒绝原因用）。空注册表也要给可读值，不能回「当前支持 。」。</summary>
    private string DescribeSupported() =>
        _subtypes.Length == 0 ? "（无已注册子类型）" : string.Join(" 与 ", _subtypes);
}
