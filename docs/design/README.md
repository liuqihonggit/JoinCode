# design/ — 技术设计(怎么实现)

> 📍 **导航**: [docs/](../README.md) › design/ | **前置**: [adr/](../adr/README.md)(决策依据)
> 🔗 **上游索引**: [docs/README.md](../README.md) — 新增/删除文档后须同步更新

技术设计文档、PRD、验收报告。回答"怎么实现"。

## 文档列表

### 架构索引

| 文件 | 标题 |
|------|------|
| [architecture-index.md](architecture-index.md) | 项目架构索引(组件依赖图/详情表/中间件管道) |
| [technical-details.md](technical-details.md) | 技术要点(容错/前缀缓存/循环干预/并行加载) |
| [small-model-strategy.md](small-model-strategy.md) | 小模型设计组合拳 |
| [flatten-restructure-plan.md](flatten-restructure-plan.md) | 文件夹扁平化重组方案 |
| [flatten-restructure-phase0-research.md](flatten-restructure-phase0-research.md) | 阶段0 前置调研 |

### Computer Use / 桌面自动化

| 文件 | 标题 |
|------|------|
| [ComputerUse-PRD.md](ComputerUse-PRD.md) | Computer Use 能力建设 PRD |
| [ComputerUse-P0-DesktopInput-Design.md](ComputerUse-P0-DesktopInput-Design.md) | P0 桌面输入模拟底座设计 |
| [ComputerUse-P0-Acceptance.md](ComputerUse-P0-Acceptance.md) | P0 验收报告 |
| [ComputerUse-P1-P5-Acceptance.md](ComputerUse-P1-P5-Acceptance.md) | P1-P5 验收报告 |
| [desktop-scene-acceptance-criteria.md](desktop-scene-acceptance-criteria.md) | 桌面情景模式验收标准 |
| [desktop-ai-scene-disclosure-report.md](desktop-ai-scene-disclosure-report.md) | 桌面自动化 AI 场景披露报告 |
| [DesktopPulseOverlay-PRD.md](DesktopPulseOverlay-PRD.md) | 桌面脉冲高亮覆盖层 PRD |

### 韧性与容错

| 文件 | 标题 |
|------|------|
| [UnifiedResilienceArchitecture.md](UnifiedResilienceArchitecture.md) | 统一韧性架构设计 |
| [TransportFallbackChain.md](TransportFallbackChain.md) | 传输层 Fallback 链设计(L1-L10) |
| [ContextCompaction-Design.md](ContextCompaction-Design.md) | 上下文压缩机制实现设计 |
| [LLM-OutputLoop-Detection-Design.md](LLM-OutputLoop-Detection-Design.md) | LLM 输出循环检测与干预设计 |

### GUI / TUI

| 文件 | 标题 |
|------|------|
| [GUI重构-subAgent运行期显示设计.md](GUI重构-subAgent运行期显示设计.md) | 多 subAgent 运行期显示设计 |

### 工具与命令

| 文件 | 标题 |
|------|------|
| [工具渐进式暴露设计.md](工具渐进式暴露设计.md) | 工具渐进式暴露设计 |
| [多模态隐喻显露工具-PRD.md](多模态隐喻显露工具-PRD.md) | 多模态隐喻显露工具 PRD |
| [命令拦截架构改造.md](命令拦截架构改造.md) | 命令拦截架构改造 |
| [rg-engine-acceleration-and-unification.md](rg-engine-acceleration-and-unification.md) | RgEngine 加速与全局统一 |
| [RangeDownloader-PRD.md](RangeDownloader-PRD.md) | RangeDownloader 基建 PRD |
