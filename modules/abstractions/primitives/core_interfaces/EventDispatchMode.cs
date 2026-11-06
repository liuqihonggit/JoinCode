namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 事件分发模式 — 对齐 DSH Cordis 五种 dispatch
/// <para>Emit：同步触发不等监听器（fire-and-forget）</para>
/// <para>Parallel：异步并行 await 全部</para>
/// <para>Serial：串行依次 await</para>
/// <para>Bail：首个 bail（返回 true）即停</para>
/// <para>Waterfall：监听器连成 next() 链，next() 返回值递下一个</para>
/// </summary>
public enum EventDispatchMode {
    /// <summary>同步触发不等监听器</summary>
    [EnumValue("emit")] Emit,

    /// <summary>异步并行 await 全部</summary>
    [EnumValue("parallel")] Parallel,

    /// <summary>串行依次 await</summary>
    [EnumValue("serial")] Serial,

    /// <summary>首个 bail 结果即停</summary>
    [EnumValue("bail")] Bail,

    /// <summary>next() 链，漏调 next() 短路</summary>
    [EnumValue("waterfall")] Waterfall,
}
