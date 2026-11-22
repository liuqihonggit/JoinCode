namespace Tools.Handlers;


/// <summary>
/// 异步(后台)Agent 允许的工具集 — 委托 <see cref="BackgroundExecutionPolicy.AllowedTools"/> 唯一数据源。
/// <para>后台工具白名单统一由 <c>BackgroundExecutionPolicy</c> 维护，禁止在此重复定义。</para>
/// </summary>
public static class AsyncAgentAllowedTools {
    /// <summary>后台 Agent 允许的工具 FrozenSet — O(1) 查找, AOT 友好</summary>
    public static readonly FrozenSet<string> Tools = BackgroundExecutionPolicy.AllowedTools;
}