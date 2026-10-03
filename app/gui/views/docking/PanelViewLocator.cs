namespace JoinCode.Gui.Views.Docking;

/// <summary>
/// Dock 面板 View 映射 — 根据 <see cref="ViewModels.Docking.PanelTool.Kind"/> 创建对应面板 View。
/// DataContext 继承 Dock Tool 的 Context（即 MainViewModel），保持现有绑定路径不变。
/// </summary>
public sealed class PanelViewLocator : IDataTemplate {
    /// <summary>根据 PanelTool.Kind 创建对应面板 View，DataContext 设为 Tool.Context</summary>
    public Control? Build(object? data) {
        if (data is not PanelTool tool) return new TextBlock { Text = data?.ToString() ?? "" };
        Control view = tool.Kind switch {
            JoinCode.Gui.ViewModels.SidePanelKind.Sessions => new SidebarView(),
            JoinCode.Gui.ViewModels.SidePanelKind.FileTree => new FileTreePanelView(),
            JoinCode.Gui.ViewModels.SidePanelKind.Settings => new SettingsPanelView(),
            JoinCode.Gui.ViewModels.SidePanelKind.Goal => new GoalView(),
            JoinCode.Gui.ViewModels.SidePanelKind.Interceptor => new InterceptorPopupContent(),
            JoinCode.Gui.ViewModels.SidePanelKind.ChatRoom => new ChatRoomView(),
            _ => new TextBlock { Text = tool.Title }
        };
        view.DataContext = tool.Context;
        return view;
    }

    /// <summary>匹配 IDockable 数据类型</summary>
    public bool Match(object? data) => data is IDockable;
}
