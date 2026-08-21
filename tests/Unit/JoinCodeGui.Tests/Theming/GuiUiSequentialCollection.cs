namespace JoinCode.Gui.Tests.Theming;

/// <summary>
/// GUI UI 串行集合 — 操作 Avalonia Application 级共享状态（GuiAppResources 主题字典、
/// 窗口样式）的测试类必须加入此集合，xUnit 保证同集合内顺序执行，避免并行互踩导致的
/// 偶发失败（B8：LightThemeFrame/SessionSelectHighlight 等在全量跑时随机失败，单测通过）。
/// </summary>
[CollectionDefinition("GuiUiSequential")]
public sealed class GuiUiSequentialCollection;
