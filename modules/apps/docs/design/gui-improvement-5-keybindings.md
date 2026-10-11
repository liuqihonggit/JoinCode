# 设计：快捷键绑定

## 需求

拦截器/快速提示词/goal 按钮加快捷键（如 Ctrl+1~7 切换拦截器），键盘用户不用点鼠标。

## 现状分析

- Avalonia 用 `KeyBindings` 绑定快捷键
- 当前 TopBar/InputBar 按钮只有鼠标点击，无快捷键
- `MainViewModel` 有 `ToggleInterceptorPanelCommand`/`StopGoalCommand` 等命令

## 设计方案

### 快捷键映射

| 快捷键 | 功能 | 命令 |
|--------|------|------|
| Ctrl+I | 切换拦截器面板 | `ToggleInterceptorPanelCommand` |
| Ctrl+G | 切换 goal 面板 | `ToggleGoalPanelCommand` |
| Ctrl+Shift+S | 停止 goal | `StopGoalCommand` |
| Ctrl+1~6 | 快速提示词 1~6 | `QuickPromptCommand` 参数 1~6 |
| Ctrl+Shift+P | 暂停所有子代理 | `BackgroundPanel.PauseAllCommand` |
| Ctrl+Shift+R | 恢复所有子代理 | `BackgroundPanel.ResumeAllCommand` |
| Ctrl+Shift+T | 终止所有子代理 | `BackgroundPanel.StopAllCommand` |

### 改动清单

#### 1. MainWindow.axaml 加 KeyBindings

```xml
<Window.KeyBindings>
  <KeyBinding Gesture="Ctrl+I" Command="{Binding ToggleInterceptorPanelCommand}" />
  <KeyBinding Gesture="Ctrl+G" Command="{Binding ToggleGoalPanelCommand}" />
  <KeyBinding Gesture="Ctrl+Shift+S" Command="{Binding StopGoalCommand}" />
  <KeyBinding Gesture="Ctrl+1" Command="{Binding QuickPromptCommand}" CommandParameter="1" />
  <KeyBinding Gesture="Ctrl+2" Command="{Binding QuickPromptCommand}" CommandParameter="2" />
  ...
</Window.KeyBindings>
```

#### 2. 快速提示词命令加参数

当前 `QuickPromptCommand` 可能无参数，需改为接受 `string` 参数（提示词编号）。

#### 3. 子代理快捷键绑定到 BackgroundPanel 命令

```xml
<KeyBinding Gesture="Ctrl+Shift+P" Command="{Binding BackgroundPanel.PauseAllCommand}" />
<KeyBinding Gesture="Ctrl+Shift+R" Command="{Binding BackgroundPanel.ResumeAllCommand}" />
<KeyBinding Gesture="Ctrl+Shift+T" Command="{Binding BackgroundPanel.StopAllCommand}" />
```

## 涉及文件

- `app/gui/views/MainWindow.axaml` — 加 KeyBindings
- `app/gui/view_models/main_view_model/MainViewModel.Interceptors.cs` — QuickPromptCommand 加参数
