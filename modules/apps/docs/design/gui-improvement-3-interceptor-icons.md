# 设计：拦截器图标+分类色

## 需求

7 个 toggle 拦截器开关按类别（文件/Shell/网络/Agent）用不同颜色+图标，比纯文字开关更直观。

## 现状分析

当前 `TopBarView.axaml` 78-110 行：7 个纯文字 CheckBox，无图标无分类色。

## 设计方案

### 分类

| 类别 | 颜色 | 图标 | 拦截器 |
|------|------|------|--------|
| Git | 橙色 (#E89A3C) | 📊 | git_commit、git_push |
| GitHub | 紫色 (#9B59B6) | 🐙 | gh_pr_merge、gh_pr_create、gh_release_delete |
| 文件 | 红色 (#E74C3C) | 📄 | file_delete |
| Shell | 蓝色 (#3498DB) | ⚡ | bash/powershell |

### 改动清单

#### 1. XAML 改造 — 按类别分组 + 图标 + 颜色

把 7 个 CheckBox 按类别分成 4 组，每组：
- 组标题（带类别图标+颜色）
- 组内 CheckBox（带类别色边框）

```xml
<StackPanel Spacing="8">
  <!-- Git 类（橙色） -->
  <StackPanel Spacing="3">
    <TextBlock Text="📊 Git" Foreground="#E89A3C" FontSize="11" FontWeight="SemiBold" />
    <CheckBox Content="commit" IsChecked="{Binding BlockGitCommit}" ... />
    <CheckBox Content="push" IsChecked="{Binding BlockGitPush}" ... />
  </StackPanel>
  <!-- GitHub 类（紫色） -->
  ...
</StackPanel>
```

#### 2. 可选：加类别色 Resource

在 `app/gui/theming/` 加 4 个类别色 DynamicResource，支持暗色/亮色主题切换。

## 涉及文件

- `app/gui/views/TopBarView.axaml` — 拦截器面板分组+图标+颜色
- 可选：`app/gui/theming/` — 类别色资源
