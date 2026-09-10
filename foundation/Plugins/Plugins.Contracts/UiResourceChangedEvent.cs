namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 资源变更事件 — 插件卸载 UI 资源时广播,前端订阅后刷新界面
/// <para>通过 IAppEventBus 广播,GUI/CLI 各自订阅处理</para>
/// </summary>
public sealed record UiResourceChangedEvent(
    string PluginName,
    IReadOnlyList<UiResourceEntry> RemovedResources,
    DateTime Timestamp);
