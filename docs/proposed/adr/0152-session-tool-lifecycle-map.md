# ADR 0152: Session 级别工具生命周期 Map

> 📍 **导航**: [docs/](../../README.md) › [adr/](../README.md)
> 🔗 **上游索引**: [adrDadr/README.md](../README.md) — 修改本文档后须同步更新此索引

> 状态: proposed
> 创建: 2026-10-28
> 决策者: 用户主导（子代理崩溃时杀死其 bash 子进程 — session 级别绑定每个工具触发）
> 影响: modules/cognition/hands/system_actuator/abstractions/SessionToolMap.cs + BashSystemActuator + ADR 0106 卡死检测
> 关联: [ADR 0106](../implemented/adr/data_core_selection/0106-subagent-stall-defense-in-depth.md)（子代理卡死防护）、[ADR 0151](0151-command-timeout-auto-background.md)（命令超时转后台）

## 背景

子代理执行任务时启动 bash 子进程。子代理崩溃/无法唤醒时，其 bash 子进程成为孤儿进程，继续消耗资源。

用户需求：
1. 每个会话有个 map 持有各种工具
2. key: 工具唯一描述名
3. value: info（开始时间、超时时间、警报时间、真实杀死时间）
4. 警报时间到达时异步提示宿主代理
5. 真实杀死时间到达时强制杀死子进程
6. 子代理崩溃时杀死其所有工具子进程

当前状态：
- ADR 0151 CLI 层总开关（进程退出时取消所有后台任务）— 兜底
- ADR 0106 子代理卡死防护（四层纵深防御）— 检测卡死但未杀死子进程
- 无 session 级别工具生命周期管理

## 决策

1. **ToolInfo record** — 记录每个工具子进程的生命周期信息：
   - ToolName: 工具唯一描述名（如 "bash!bash:ls"）
   - ProcessId: 子进程 ID
   - StartTime: 开始时间（UTC）
   - TimeoutDuration: 超时时间（总上限）
   - WarningThreshold: 警报时间（到此时异步提示宿主代理）
   - KillThreshold: 真实杀死时间（到此时强制杀死）
   - AgentId: 启动此工具的子代理 ID

2. **SessionToolMap** — session 级别工具生命周期 map：
   - Register/Unregister — 工具执行时注册/注销
   - Get/GetAll/GetByAgent — 查询工具
   - KillByAgent(agentId) — 子代理崩溃时杀死其所有工具子进程
   - KillAll() — session 结束时兜底
   - CheckTimeouts(now) — 返回需要警报和需要杀死的列表

3. **集成点**：
   - BashSystemActuator 启动子进程后注册 ToolInfo
   - 后台定时器调 CheckTimeouts（警报 → 异步提示宿主，杀死 → Kill(entireProcessTree: true)）
   - ADR 0106 卡死检测确认子代理崩溃时调 KillByAgent

4. **默认时间阈值**：
   - WarningThreshold: 30s（工具执行超 30s 提示宿主代理）
   - KillThreshold: 300s（工具执行超 5min 强制杀死）
   - TimeoutDuration: 600s（总超时上限 10min）

## 替代方案

1. **CLI 层总开关（ADR 0151）** — 进程退出时杀死所有子进程。但子代理崩溃时进程不退出，子进程成为孤儿；作为兜底保留
2. **进程组管理** — 子进程在同一进程组，kill 进程组终止所有子进程。但跨平台兼容性差（Windows JobObject vs Linux setpgid）；弃
3. **操作系统 JobObject** — Windows JobObject 在父进程退出时杀死所有子进程。但仅 Windows；弃

## 后果

### 正面
- 子代理崩溃时杀死其所有 bash 子进程（防止孤儿进程）
- 工具执行超时时警报提示宿主代理$代理（异步通知）
- 真实杀死时间到达时强制杀死（防止无限挂起）
- session 级别管理（每个会话独立）

### 负面
- 每个工具执行需要注册/注销（轻微开销）
- 后台定时器检查（每秒一次 CheckTimeouts）
- Kill(entireProcessTree: true) 可能杀死孙进程（bash 启动的子命令）

### 实现顺序
1. ✅ ToolInfo + SessionToolMap 数据结构
2. ⏳ 单元测试
3. ⏳ BashSystemActuator 集成（注册 ToolInfo）
4. ⏳ 后台定时器（CheckTimeouts）
5. ⏳ ADR 0106 卡死检测集成（KillByAgent）
