# Goal + 无人值守模式联动任务

## 背景

用户需求：
1. GUI 补充 goal 进行中按钮
2. goal 停止要求：输出停止超过 1 分钟自动激活询问下一步，再三确认才能停止
3. goal 需要无人值守模式，两个功能应联动
4. 停滞告警阈值 3600s 太长，改为 60s

## 发现的 Bug（TDD 修复）

### Bug1: GUI 开关断裂（P0）
- **问题**：`MainViewModel.IsUnattendedMode` 属性变更未传导到权限系统，缺少 `OnIsUnattendedModeChanged` partial 方法
- **影响**：GUI 切换无人值守开关只写 settings.json，不会实际激活 `PermissionMode.Unattended`
- **修复**：加 `OnIsUnattendedModeChanged` → 调用 `IJccChatSession.SetPermissionModeAsync`
- **需改接口**：`IJccChatSession` 加 `SetPermissionModeAsync(PermissionMode mode)` 方法
- **TDD**：红测试（切换开关后验证权限模式变更）→ 修复 → 绿测试

### Bug2: settings.json 死字段（P0）
- **问题**：`SettingsJson.IsUnattendedMode` 写入但从不读取，无热重载到 PermissionChecker
- **影响**：重启后无人值守模式不会从 settings.json 恢复
- **修复**：在 SettingsMapper 中读取 `IsUnattendedMode` 并应用到 PermissionChecker 初始化
- **TDD**：红测试（settings.json 有 isUnattendedMode=true 时 PermissionChecker 初始化为 Unattended）→ 修复 → 绿测试

### Bug3: goal 覆盖问题（P0）
- **问题**：`GoalEngine.SwitchToGoalPermissionModeAsync` 只跳过 Bypass，会覆盖 Unattended 为 Auto
- **影响**：即使通过环境变量设置 Unattended，进入 goal 后会被切回 Auto
- **修复**：跳过 Unattended（保留用户设置的无人值守模式）
- **TDD**：红测试（Unattended 模式下启动 goal 不切换到 Auto）→ 修复 → 绿测试

### Bug4: AgentToolRestrictions 未处理 Unattended（P1）
- **问题**：`AgentToolRestrictions` switch 未处理 Unattended，落入默认分支
- **影响**：Unattended 模式下工具限制层不主动放行/拒绝
- **修复**：加 Unattended 分支（行为对齐 Auto：允许所有非破坏性工具）
- **TDD**：红测试（Unattended 模式下 GetAllowedTools 返回正确列表）→ 修复 → 绿测试

### Bug5: 辅助层未对齐（P2）
- **问题**：PermissionToolHandlers 图标、SettingsEditValidator 合法值列表未包含 Unattended
- **修复**：加 Unattended 图标 + 合法值
- **TDD**：红测试（Unattended 模式有图标 + 合法值校验通过）→ 修复 → 绿测试

## 新功能

### Feature1: goal 启动自动启用无人值守（P0）
- **设计**：goal 启动时切换到 Unattended（而非 Auto），goal 结束恢复原模式
- **修改**：`GoalEngine.SwitchToGoalPermissionModeAsync` → `SetPermissionModeAsync(PermissionMode.Unattended)`
- **依赖**：Bug3 修复（跳过 Unattended 不覆盖）
- **TDD**：红测试（goal 启动后权限模式为 Unattended）→ 实现 → 绿测试

### Feature2: 停滞告警阈值 3600s→60s + 改为询问用户（P0）
- **修改**：`GoalEngine.StagnationElapsedThresholdSeconds` 从 3600 改为 60
- **行为变更**：60 秒无输出 → 不再注入停滞告警让 AI 自主行动，改为通过回调通知 GUI 弹窗询问用户
- **需改接口**：`IJccChatSession` 加 goal 停滞事件回调
- **TDD**：红测试（60 秒无输出触发回调）→ 实现 → 绿测试

### Feature3: GUI goal 进行中按钮 + 60 秒输出停滞检测弹窗（P0）
- **GUI 加 goal 状态指示器按钮**：goal 运行时显示，点击弹出停止确认
- **60 秒输出停滞检测**：GUI 监听流式输出（ChatStreamEvent），60 秒无新 token 自动弹窗
- **弹窗内容**：是否已检查 + 下一步选择（继续/停止）
- **依赖**：IJccChatSession 加 goal 状态查询方法
- **TDD**：红测试（goal 运行时按钮可见 + 60 秒无输出触发弹窗）→ 实现 → 绿测试

### Feature4: 三再三确认停止 + 提示词设计（P0）
- **再三确认**：停止 goal 需要连续 3 次确认（防止误操作）
- **提示词设计**：
  - 第一次确认："goal 正在运行中，是否确实要停止？请确认你已经检查了当前进展。"
  - 第二次确认："停止 goal 将放弃未完成的工作。你确定吗？"
  - 第三次确认："这是最后一次确认。停止后无法恢复。确定停止？"
  - 60 秒停滞询问："AI 输出已停止超过 1 分钟。请检查：① 是否已完成目标？② 是否需要继续等待？③ 是否停止 goal？"

## 执行顺序（依赖关系）

```
Bug1（GUI 开关断裂）→ Bug2（settings.json 死字段）→ Bug3（goal 覆盖）
  → Feature1（goal 自动启用无人值守）→ Bug4（AgentToolRestrictions）
  → Bug5（辅助层）→ Feature2（阈值 60s + 询问用户）
  → Feature3（GUI 按钮 + 检测）→ Feature4（三再三确认 + 提示词）
```

## ✅ 完成状态（2026-09-30）

| 任务 | Commit | 状态 |
|------|--------|------|
| Bug1: GUI 开关断裂 | `d60eec2ca` | ✅ |
| Bug3: goal 覆盖 + Feature1: 自动启用 Unattended | `d5af86f8a` | ✅ |
| Feature2: 停滞告警阈值 3600s→60s | `ec9fac044` | ✅ |
| Feature3+4: GUI goal 按钮 + 再三确认提示词 a→b→c | `e0d268d10` | ✅ |
| Bug2: GuiPreferences IsUnattendedMode 死字段 | `70ce5cea0` | ✅ |
| Bug4: AgentToolRestrictions 显式 Unattended | `240f96f6d` | ✅ |
| Bug5: 辅助层对齐 Unattended | `2723902c7` | ✅ |

### Feature4 提示词最终设计（用户纠正后）

再三确认由**引擎层自动注入提示词给 AI**（不是 GUI 按钮点三次），逐级 a→b→c：
- **a (AuditMissingTasks)**："您是否有任务遗漏？派出子代理审核一下。"
- **b (ReviewSubAgentWork)**："子代理完成的工作，你需要核查一次：单一职责化、补充守卫、补充单元测试、补充压测、相同函数抽取工具类。"
- **c (FinalConfirmAndTests)**："你确认真的要结束任务了吗？是否补充一些单元测试？"

定义在 `ContinuationPromptBuilder.BuildStopConfirmationPrompt(StopConfirmationLevel)`，60 秒无输出后逐级注入 `_chatHistory.AddSystemMessage()`。

## TDD 流程（每个 Bug/Feature）

1. 🔴 写失败测试（复现 bug / 定义行为）
2. 🔴 确认测试失败
3. 🟢 写最小修复让测试通过
4. 🔵 重构（如有必要）
5. 🟢 确认测试通过
6. 编译验证
7. git 提交

## 涉及文件

### 接口层
- `app/gui/hosting/IJccChatSession.cs` — 加 SetPermissionModeAsync + goal 状态查询 + 停滞回调
- `app/gui/hosting/JccChatSession.cs` — 实现
- `app/gui/hosting/PlaceholderChatSession.cs` — 空实现

### GUI 层
- `app/gui/view_models/main_view_model/MainViewModel.cs` — OnIsUnattendedModeChanged
- `app/gui/view_models/main_view_model/MainViewModel.Interceptors.cs` — goal 按钮 + 60 秒检测
- `app/gui/views/TopBarView.axaml` — goal 进行中按钮
- `app/gui/views/MainWindow.axaml` — 确认弹窗

### 引擎层
- `lib/clock/goal/core/goal_engine/GoalEngine.cs` — 阈值 60s + Unattended 切换 + 询问回调
- `lib/clock/goal/infrastructure/ContinuationPromptBuilder.cs` — 询问提示词

### 权限层
- `lib/guard/permission/permission2/tool_handlers/core/PermissionChecker.cs` — settings.json 读取
- `lib/guard/permission/utils/AgentToolRestrictions.cs` — Unattended 分支
- `lib/guard/permission/permission2/tool_handlers/core/PermissionToolHandlers.cs` — 图标
- `lib/abstractions/abs_core/configuration/settings/SettingsEditValidator.cs` — 合法值

### 配置层
- `lib/guard/configuration/configuration2/core/mapping/SettingsMapper.cs` — 读取 IsUnattendedMode
