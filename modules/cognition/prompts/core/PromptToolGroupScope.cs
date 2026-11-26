namespace Core.Prompts;

/// <summary>
/// 工具组作用域 — 把「当前 profile 启用了哪些工具组」与「当前 agent 的工具域」传给 static PromptSection。
/// <para>
/// TASK100 S6：<see cref="PromptConfigSnapshot"/> 解决的是「static Section 读不到配置」这一整类问题，
/// 但它的语义是「会话初始化时的配置快照」，一个进程一份。工具组清单不同：它随 profile 变，
/// 且只影响 <c>tools</c> 一个段落 —— 塞进 <see cref="SystemPromptProviderOptions"/> 会让
/// 所有读 <c>Current</c> 的段落都换一套输入，那是另一件事的影响面。
/// </para>
/// <para>
/// 所以这里单独一个 AsyncLocal 槽：profile 组 ID + agent 工具域快照。
/// 作用域由 <c>SystemPromptMiddleware</c> 在构建提示词时开启 —— 也就是唯一同时知道
/// profile 与当前子代理上下文的地方。
/// </para>
/// </summary>
public static class PromptToolGroupScope {
    private sealed record State(IReadOnlyList<string>? Enabled, CliToolScopeSnapshot? Domain);

    private static readonly AsyncLocal<State?> _current = new();

    /// <summary>
    /// 当前作用域内启用的工具组 ID；<see langword="null"/> 表示未进入作用域（= 全开）。
    /// </summary>
    public static IReadOnlyList<string>? Enabled => _current.Value?.Enabled;

    /// <summary>
    /// 当前 agent 的工具域快照；<see langword="null"/> 表示未收紧（主代理或无子代理空间）。
    /// </summary>
    public static CliToolScopeSnapshot? Domain => _current.Value?.Domain;

    /// <summary>
    /// 进入工具组作用域 — <see cref="IDisposable.Dispose"/> 时恢复原值（支持嵌套）。
    /// </summary>
    /// <param name="enabled">启用的组 ID；<see langword="null"/> 表示全开。</param>
    /// <param name="domain">当前 agent 工具域；<see langword="null"/> 表示不裁剪。</param>
    /// <returns>作用域句柄。</returns>
    public static IDisposable Enter(IReadOnlyList<string>? enabled, CliToolScopeSnapshot? domain = null) =>
        AsyncLocalScope<State?>.Enter(_current, new State(enabled, domain));
}
