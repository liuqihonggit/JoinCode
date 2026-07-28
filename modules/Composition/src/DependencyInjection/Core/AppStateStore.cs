namespace Core.DependencyInjection;

/// <summary>
/// AppState 专用的 Store 子类 — 桥接 [Register] 自动注册与泛型 Store
/// <para>Store&lt;TState&gt; 是泛型类，[Register] 生成器无法直接处理泛型。</para>
/// <para>通过此非泛型子类，DI 容器可自动解析 IStore&lt;AppState&gt;。</para>
/// </summary>
[Register(typeof(IStore<AppState>))]
public sealed partial class AppStateStore : Store<AppState>
{
    /// <summary>
    /// DI 构造函数 — 使用 AppState.Default 作为初始状态
    /// </summary>
    public AppStateStore(
        IStorePersistence<AppState>? persistence = null,
        ILogger<Store<AppState>>? logger = null)
        : base(AppState.Default, persistence, logger)
    {
    }
}
