# 0005. 文件驱动界面

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

GUI（Avalonia）中供应商下拉、模型下拉、工具补全、斜杠命令等列表数据，早期由 ViewModel 硬编码枚举遍历或固定列表填充。问题：改列表需改代码重新编译；无法热重载；与配置文件脱节。

## 决策

**任何界面下拉/列表/表格的数据源必须绑定配置文件或引擎数据源，禁止硬编码。**

数据流：
```
配置文件 (models.json / settings.json)
  ↓ ModelConfigLoader / IConfigChangeNotifier 加载
Abstractions 层门面 (IProviderDefinitionRegistry / ModelConfigLoader)
  ↓ IJccChatSession 接口暴露
GUI ViewModel 属性 (ConnectionOptions / ModelOptions)
  ↓ OnPropertyChanged 驱动
XAML ComboBox / ListBox 双向绑定
```

具体绑定：
- 供应商下拉 → `ModelConfigLoader.Config.Providers`
- 模型下拉 → `IJccChatSession.AvailableModels`
- 工具补全 → `IJccChatSession.GetAvailableToolsAsync()`
- 斜杠命令 → `IJccChatSession.GetAvailableSlashCommands()`（源码生成器 `[ChatCommand]` 提取）

## 替代方案

1. **ViewModel 硬编码枚举遍历**：放弃。`Enum.GetValues<ProviderKind>()` 填充 ComboBox，改枚举要重新编译。
2. **代码内固定列表**：放弃。`new[] { "openai", "deepseek" }`，改列表要改代码。
3. **编译时生成列表**：放弃。仍需重新编译，且无法热重载。

## 后果

- 正面：改配置文件即驱动界面更新，无需编译；配置热重载通过 `IConfigChangeNotifier` 触发 `OnPropertyChanged` 刷新
- 负面：测试需 mock session 实现 `AvailableProviders` 返回固定列表（如 `["fake"]`），不能依赖真实配置文件
- 中性：测试桩与生产路径分离，mock session 不依赖配置文件
