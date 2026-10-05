namespace JoinCode.Gui.Views.Docking;

/// <summary>
/// Dock 面板 View 映射 — 根据 dockable 类型创建对应 View。
/// DataContext 继承 Dock Tool/Document 的 Context（即 MainViewModel），保持现有绑定路径不变。
/// </summary>
public sealed class PanelViewLocator : IDataTemplate {
    /// <summary>根据 dockable 类型创建对应 View，DataContext 设为 Context</summary>
    public Control? Build(object? data) {
        switch (data) {
            case PanelTool tool:
                return BuildPanelView(tool);
            case MessageAreaDocument doc:
                return BuildWithContext(new MessageAreaView(), doc.Context);
            case InputBarTool inputBar:
                return BuildWithContext(new InputBarView(), inputBar.Context);
            case TerminalTool terminal:
                return BuildWithContext(new PanelView(), terminal.Context);
            default:
                return new TextBlock { Text = data?.ToString() ?? "" };
        }
    }

    private static Control BuildWithContext(Control view, object? context) {
        view.DataContext = context;
        return view;
    }

    private Control BuildPanelView(PanelTool tool) {
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
