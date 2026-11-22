namespace Core.Context;

/// <summary>
/// 发送提醒注入器 — 用户发送消息时把系统提示词以 &lt;system-reminder&gt; 标签形态追加到消息末尾，
/// 冷却期内不重复注入（默认 10 分钟）。
/// <para>对齐 ClaudeCode system-reminder 注入语义：标签内容是系统自动添加的信息，
/// 提示 AI 洞悉用户诉求（服务指令背后的诉求而非字面指令）。斜杠命令与空白输入不注入、不消耗冷却。</para>
/// <para>冷却复用 <see cref="CooldownService"/> 组合（实例独立、静态 Instance 进程内共享）；
/// 接线点：SessionController.StreamResponseAsync（TUI/CLI 主代理路径）+
/// ChatService.StreamWithEventsAsync（GUI/TUI 回退路径）— 两路径互斥，静态 Instance 保证同进程共享冷却。</para>
/// </summary>
public sealed class SendReminderInjector {
    /// <summary>冷却去重键 — CooldownService 键空间内唯一标识本注入器。</summary>
    public const string CooldownKey = "send-intent-reminder";

    /// <summary>默认冷却期 — 10 分钟内不重复注入。</summary>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(10);

    /// <summary>注入文案 — 洞悉用户诉求（用户提供的系统提示词原文）。</summary>
    public const string ReminderText = "重要提示：洞悉用户诉求——不要服务于指令，服务指令背后的诉求，围绕用户诉求作为目标驱动开发。";

    /// <summary>
    /// 进程内共享单例 — SessionController（手动 new 组装）与 ChatService（DI 单例）共用同一冷却状态
    /// （绑定全局 <see cref="CooldownService.Instance"/>），对齐 SystemClockService.Instance 先例。
    /// </summary>
    public static readonly SendReminderInjector Instance = new(cooldownService: CooldownService.Instance);

    private readonly CooldownService _cooldown;
    private readonly TimeSpan _cooldownSpan;

    /// <summary>
    /// 初始化注入器 — 注入冷却服务与冷却时长（测试传独立实例 + 毫秒级短冷却验证恢复行为）。
    /// </summary>
    /// <param name="cooldown">冷却时长，默认 10 分钟。</param>
    /// <param name="cooldownService">冷却服务，默认独立实例（静态 Instance 绑定全局共享实例）。</param>
    public SendReminderInjector(TimeSpan? cooldown = null, CooldownService? cooldownService = null) {
        _cooldown = cooldownService ?? new CooldownService();
        _cooldownSpan = cooldown ?? DefaultCooldown;
    }

    /// <summary>
    /// 判定并追加 — 可注入（非空白非斜杠）且冷却允许时返回带 system-reminder 后缀的消息，否则原文返回。
    /// </summary>
    /// <param name="input">用户发送的原始消息。</param>
    /// <returns>可能追加了 system-reminder 后缀的消息。</returns>
    public string Append(string input) =>
        IsInjectable(input) && _cooldown.TryTrigger(CooldownKey, _cooldownSpan)
            ? BuildSuffixed(input)
            : input;

    /// <summary>重置静态 Instance 的冷却状态 — 仅供测试隔离（清全局 CooldownService.Instance 的记录）。</summary>
    public static void ResetForTest() => CooldownService.Reset();

    /// <summary>可注入判定 — 空白或斜杠命令（/model 等，不进 LLM）不注入且不消耗冷却。</summary>
    private static bool IsInjectable(string input) =>
        !string.IsNullOrWhiteSpace(input) && !input.TrimStart().StartsWith('/');

    /// <summary>构造带后缀消息 — 原文 + 空行 + system-reminder 标签包裹文案。</summary>
    private static string BuildSuffixed(string input) =>
        $"{input}\n\n<system-reminder>\n{ReminderText}\n</system-reminder>";
}
