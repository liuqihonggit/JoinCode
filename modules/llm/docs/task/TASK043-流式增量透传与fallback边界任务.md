# TASK043 — 流式增量透传与 fallback 边界（缺陷驱动）

> 来源：TASK053 F1-x（GUI 输入闭环真机 E2E 红复现）。用户决策：先提交现状、另立本任务后修。
> 状态：待修复（E2E 复现已入库；修复需先写 ADR，见「实现前置」）。

## 1. 现象（真机可复现）

GUI WebUI（`--webui` + 真实引擎 + `OpenAI.MockServer` 确定性上游）中，助手回复**不逐字出现**：

- 上游按 20ms/分片下发 700 个分片（约 14s 流），观测窗口内 `.msg-column` 文本长度**恒定 111**（10s 采样，零新节点、零增长）；
- 点击「停止」的瞬间，此前累积的正文**批量补发**（实测 2423 字），随后 `chat/done(stopped)` 收尾。

复现脚本：`non_deliverables_tools/dsh_parity/dumps/f1-webui-flow.mjs`（CDP 9222 驱动真实 exe）+ 夹具 `dumps/f1-mockserver.json`（第 2 轮 = 700 分片长文本）。
判据：`STREAM t=...ms col=` 采样恒等 → `FAIL 流式进行中（已收到增量）`。

## 2. 根因

`modules/llm/core/Adapters/LLM/Fallback/StreamingFallbackDecorator.cs:61`

```csharp
public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(...) {
    var result = await CollectWithFallbackAsync(...);   // ← 全量收集完才返回
    foreach (var evt in result.Events) { yield return evt; }
}
```

注释自述原因：C# 不允许 `try-catch` 内 `yield return`（CS1626/CS1631），因此先用 `CollectWithFallbackAsync` 把整条流收进 `List<StreamEvent>`，成功后再统一 yield。结果：**消费方（GUI/TUI）在流结束前收不到任何事件**，逐字流式完全失效（长度 1 的短视频为「瞬间出现」，肉眼不可辨，故长期未暴露）。

放大点：`modules/llm/core/DependencyInjection/ServiceRegistration.cs:136-142` 默认 `StreamingFallbackConfig.Enabled = true` 包装**全部**流式调用 → GUI 与 TUI 同受影响。

## 3. 影响面

| 面 | 影响 |
|----|------|
| GUI（WebUI/TUI） | 无逐字流式；长回答期间界面「假死」直到整轮结束 |
| 停止生成 | 停止时刻恰为批量补发瞬间，「中断在途流」变成「批量闪现」（用户感知异常） |
| 首字延迟 | 等于整轮生成时间（长回答可达数十秒） |
| fallback 语义 | 现设计：任意流式异常 → 非流式重试（不透明）；增量透传后需重新界定「已产出后失败」的语义 |

## 4. 实现前置（架构决策）

新增 ADR（`docs/proposed/adr/`，状态 proposed → 落地后 accepted）：**流式增量化与 fallback 边界**

建议方案（待 ADR 论证）：

1. **首事件前可 fallback**：在收到第一个内容事件之前仍完整走 watchdog + fallback（保持「不完整流/看门狗超时/529 等」的降级能力）；
2. **一旦 yield 内容即不再 fallback**：已向消费方输出后无法回退，中途失败改为**上抛**（GUI 由 F1 错误条 + 已产出正文保留承接；CLI 打印错误）；
3. 实现手法（消除 CS1626 限制）：拆成「预热阶段」`TryPrimeAsync`（返回首事件或 fallback 结果）+「透传阶段」`foreach ... yield`（不再包 try-catch；看门狗在透传阶段用 `Reset()` 维持）；
4. 保留 `LastRequestFellBack` / `OnStreamingFallback` 回调语义不变。

替代方案（ADR 需记录并说明弃用理由）：① 消费方侧加缓冲窗口（治标，不解决首字延迟）；② 取消 fallback 装饰器（丢失降级能力）。

## 5. 验收标准（TDD）

| 层 | 红 | 绿 |
|----|----|----|
| 单元（E2E 红先） | `StreamingFallbackDecoratorTests`：上游分片产出但消费方在流结束前收不到事件（断言「首事件到达时间 < 上游结束时间」失败） | 首事件在首个上游分片后即到达；首事件前失败仍 fallback；yield 后失败上抛且不触发非流式 |
| 单元（回归） | — | 现有 fallback 触发用例（看门狗超时/不完整流/529/IOException）全绿 |
| 真机 E2E | `dumps/f1-webui-flow.mjs` 的 `流式进行中（已收到增量）` FAIL（当前 F1-x 红） | 同一条 14/15 → **15/15 PASS**（`STREAM` 采样随分片递增，停止时正文长度增长中且未收全） |

完成后：TASK053 的 `F1-x` 勾选为已修，并把该断言从「已知缺陷白名单」恢复为强制断言。

## 6. 关联

- 缺陷复现证据：TASK053「F 系列 — 功能对接」→ `F1-x`（含真机 log 与采样数据）
- ADR 0080 手动 exe 验收方式；TASK053 F7（一键功能 E2E 入 CI）依赖本任务转绿
- 观测（非本任务范围）：引擎就绪耗时 24~25s（`EngineSessionFactory.CreateGuiSessionAsync`）
