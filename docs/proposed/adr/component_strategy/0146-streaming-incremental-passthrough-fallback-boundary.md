# ADR 0146 — 流式增量透传与 fallback 首事件边界

> 状态：accepted（2026-10-22 落地：单元红→绿 + llm.tests 605 全绿 + 实弹 E2E StreamingIncrementalE2ETests 绿，随 TASK043 实施）
> 关联：TASK043（modules/llm/docs/task/TASK043-流式增量透传与fallback边界任务.md）、ADR 0080（手动 exe 验收）

## 上下文

`StreamingFallbackDecorator.GetStreamEventContentsAsync` 因 C# 迭代器限制（CS1626/CS1631：try-catch 内禁止 `yield return`）采用「全量收集后整发」实现：`CollectWithFallbackAsync` 把整条流收进 `List<StreamEvent>` 再统一 yield。后果（TASK043 真机实证）：

- **逐字流式完全失效**：GUI/TUI 消费方在流结束前收不到任何事件（700 分片 14s 流，10s 采样文本长度恒定）
- **首字延迟 = 整轮生成时间**（长回答可达数十秒）
- **停止语义异常**：取消瞬间累积正文批量闪现（2423 字一次性补发）
- 放大面：`ServiceRegistration` 默认 `StreamingFallbackConfig.Enabled=true` 包装**全部**流式调用

## 决策

**两阶段结构（预热可 fallback / 透传不可 fallback）+ 手动 enumerator 消除 CS1626**：

1. **预热阶段（TryPrimeAsync，非迭代器可 try-catch）**：拉取首个上游事件。此阶段失败（529/超时/IOException/看门狗超时/空流 IncompleteStream）→ 仍走非流式 fallback——对齐真实场景：连接建立期错误拿不到任何事件，降级重试无损（消费方零感知）。
2. **透传阶段（迭代器，仅 try-finally 禁 try-catch）**：`yield return` 首事件后逐个 `MoveNextAsync` + `yield`。**已向消费方产出后不再 fallback**——中途失败/看门狗超时/用户取消 OCE 全部上抛（已 yield 内容无法回收，非流式重试会造成重复输出）。
3. **所有权转移**：预热成功时 watchdog + enumerator 从 TryPrimeAsync 转移给调用方，由迭代器 `finally`（finally 中 `await DisposeAsync` 合法）统一释放；fallback/取消路径由 TryPrimeAsync 自释放。
4. 保留 `LastRequestFellBack` / `OnStreamingFallback` 回调语义；`Enabled=false` 直通路径不变。

### 语义变化（有意为之，无后向兼容负担）

| 场景 | 旧（全量收集） | 新（两阶段） |
|------|--------------|-------------|
| 首事件前失败 | fallback 非流式 | fallback 非流式（不变） |
| 中途失败 | fallback（丢弃已收部分） | **上抛**（已产出保留） |
| 用户取消（透传中） | 吞 OCE 返回已收集事件 | **OCE 上抛**（标准流式语义；GUI 实时收增量后不再依赖批量补发） |
| 看门狗超时（透传中） | fallback | **上抛** |
| 空流 | fallback(IncompleteStream) | fallback(IncompleteStream)（不变） |

## 替代方案（弃用理由）

- **① 消费方侧加缓冲窗口**：治标——缓冲解除"整轮不出现"但首字延迟仍在；每消费方各自实现，重复逻辑。
- **② 取消 fallback 装饰器**：丢失降级能力（529 过载/网关不支持流式 404 等真实场景需要非流式重试）。
- **③ 全流 fallback + 中途失败也重试**：已 yield 内容与新流拼接会重复输出，消费方需去重——复杂度爆炸。

## 验证

- 单元红→绿：首事件在上游流完成前到达（门闩确定性时序，禁 Task.Delay）；中途失败上抛且已 yield 事件保留、LastRequestFellBack=false；首事件前失败仍 fallback（现有用例改"失败于首事件前"构造——对齐连接级错误真实形态）
- 回归：llm.tests 全量；现有 fallback 触发用例（529/IOException/超时/空流）语义保留
- 真机 E2E：f1-webui-flow.mjs 15/15（留用户/CI 验证）
