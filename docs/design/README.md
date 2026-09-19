# design/ — 技术设计(怎么实现)

> 📍 **导航**: [docs/](../README.md) › design/ | **前置**: [adr/](../adr/README.md)(决策依据)
> 🔗 **上游索引**: [docs/README.md](../README.md) — 新增/删除文档后须同步更新

技术设计文档、PRD、验收报告。回答"怎么实现"。

## 文档列表

### 架构索引

| 编号 | 文件 | 标题 |
|------|------|------|
| DSG001 | [DSG001-architecture-index.md](DSG001-architecture-index.md) | 项目架构索引(组件依赖图/详情表/中间件管道) |
| DSG002 | [DSG002-technical-details.md](DSG002-technical-details.md) | 技术要点(容错/前缀缓存/循环干预/并行加载) |
| DSG003 | [DSG003-small-model-strategy.md](DSG003-small-model-strategy.md) | 小模型设计组合拳 |
| DSG004 | [DSG004-flatten-restructure-plan.md](DSG004-flatten-restructure-plan.md) | 文件夹扁平化重组方案 |
| DSG005 | [DSG005-flatten-restructure-phase0-research.md](DSG005-flatten-restructure-phase0-research.md) | 阶段0 前置调研 |

### Computer Use / 桌面自动化

| 编号 | 文件 | 标题 |
|------|------|------|
| DSG006 | [DSG006-ComputerUse-PRD.md](DSG006-ComputerUse-PRD.md) | Computer Use 能力建设 PRD |
| DSG007 | [DSG007-ComputerUse-P0-DesktopInput-Design.md](DSG007-ComputerUse-P0-DesktopInput-Design.md) | P0 桌面输入模拟底座设计 |
| DSG008 | [DSG008-ComputerUse-P0-Acceptance.md](DSG008-ComputerUse-P0-Acceptance.md) | P0 验收报告 |
| DSG009 | [DSG009-ComputerUse-P1-P5-Acceptance.md](DSG009-ComputerUse-P1-P5-Acceptance.md) | P1-P5 验收报告 |
| DSG010 | [DSG010-desktop-scene-acceptance-criteria.md](DSG010-desktop-scene-acceptance-criteria.md) | 桌面情景模式验收标准 |
| DSG011 | [DSG011-desktop-ai-scene-disclosure-report.md](DSG011-desktop-ai-scene-disclosure-report.md) | 桌面自动化 AI 场景披露报告 |
| DSG012 | [DSG012-DesktopPulseOverlay-PRD.md](DSG012-DesktopPulseOverlay-PRD.md) | 桌面脉冲高亮覆盖层 PRD |

### 韧性与容错

| 编号 | 文件 | 标题 |
|------|------|------|
| DSG013 | [DSG013-UnifiedResilienceArchitecture.md](DSG013-UnifiedResilienceArchitecture.md) | 统一韧性架构设计 |
| DSG014 | [DSG014-TransportFallbackChain.md](DSG014-TransportFallbackChain.md) | 传输层 Fallback 链设计(L1-L10) |
| DSG015 | [DSG015-ContextCompaction-Design.md](DSG015-ContextCompaction-Design.md) | 上下文压缩机制实现设计 |
| DSG016 | [DSG016-LLM-OutputLoop-Detection-Design.md](DSG016-LLM-OutputLoop-Detection-Design.md) | LLM 输出循环检测与干预设计 |

### GUI / TUI

| 编号 | 文件 | 标题 |
|------|------|------|
| DSG017 | [DSG017-GUI重构-subAgent运行期显示设计.md](DSG017-GUI重构-subAgent运行期显示设计.md) | 多 subAgent 运行期显示设计 |

### 工具与命令

| 编号 | 文件 | 标题 |
|------|------|------|
| DSG018 | [DSG018-工具渐进式暴露设计.md](DSG018-工具渐进式暴露设计.md) | 工具渐进式暴露设计 |
| DSG019 | [DSG019-多模态隐喻显露工具-PRD.md](DSG019-多模态隐喻显露工具-PRD.md) | 多模态隐喻显露工具 PRD |
| DSG020 | [DSG020-命令拦截架构改造.md](DSG020-命令拦截架构改造.md) | 命令拦截架构改造 |
| DSG021 | [DSG021-rg-engine-acceleration-and-unification.md](DSG021-rg-engine-acceleration-and-unification.md) | RgEngine 加速与全局统一 |
| DSG022 | [DSG022-RangeDownloader-PRD.md](DSG022-RangeDownloader-PRD.md) | RangeDownloader 基建 PRD |

### 其他

| 编号 | 文件 | 标题 |
|------|------|------|
| DSG023 | [DSG023-worktree-merge-permission-system-questions.md](DSG023-worktree-merge-permission-system-questions.md) | Worktree 合并权限系统问题 |
| DSG024 | [DSG024-bridgemain-split-task.md](DSG024-bridgemain-split-task.md) | BridgeMain 拆分任务 |
| DSG025 | [DSG025-fix-config-loading-bugs.md](DSG025-fix-config-loading-bugs.md) | 配置加载 bug 修复 |
| DSG026 | [DSG026-window-shake-notification-design.md](DSG026-window-shake-notification-design.md) | 窗口震动通知设计 |
| DSG027 | [DSG027-cli_arg_io_unify_analysis.md](DSG027-cli_arg_io_unify_analysis.md) | CLI 参数 I/O 统一分析 |
| DSG028 | [DSG028-subagent-stall-defense.md](DSG028-subagent-stall-defense.md) | 子代理卡死防御 |
