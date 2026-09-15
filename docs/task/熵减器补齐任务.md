# 熵减器补齐任务

> 📍 **导航**: [docs/](../README.md) › [task/](README.md) | **前置**: [plan/](../plan/README.md)
> 🔗 **上游索引**: [task/README.md](README.md) — 修改本文档后须同步更新此索引

## 背景
熵减检测器核心链路已完整(四路检测器+三级漏斗+日志记录+管道注册),但有5项完全缺失的组件需按顺序补齐。

## 任务清单

### 第1项: RingBuffer&lt;T&gt; 改造 [进行中]
- 在 Structura 层创建 `RingBuffer<T>` 定长环形队列(O(1)覆盖)
- 创建 Structura 测试项目(TDD)
- 替换三个检测器的 `List<T>`+`RemoveRange`:
  - `ShannonEntropyDetector._entropyHistory` (List<double> → RingBuffer<double>)
  - `LogicFingerprintDetector._fingerprints` (List<int> → RingBuffer<int>)
  - `ToolCallSequenceDetector._nameSequence/_fingerprintSequence` (List<string> → RingBuffer<string>)
- 决策: RingBuffer 放 `Structura.Collections` namespace,文件 `foundation/Structura/Collections/RingBuffer.cs`

### 第2项: ~~RemoveCoT 工具实现~~ (已移除,设计无效)

### 第3项: 流式token序列检测器
- 独立 `StreamTokenDetector`,环形队列+后台异步采集
- 方案: 环形队列塞满后自动覆盖旧信息,后台异步间隔采集窗口
- 串行多分析器,纵深防御方式,漏斗式触发

### 第4项: ~~推理轮次记录类 ReasoningRound~~ (已实现,见 `kit/brain/context/services/loop/ReasoningRound.cs`)

### 第5项: 串行纵深防御改造
- `InformationEntropyGuardian` 从并行+OR仲裁改为串行漏斗式
- 先廉价检测后昂贵检测,降低平均检测成本

## 清理
- 替换 List<T> 后,如有孤儿节点(未使用的 RemoveRange 裁剪逻辑等),移动到 `.xxx/` 目录

## 进度记录
<!-- 2026-08-05 开始第1项 RingBuffer 改造 -->
