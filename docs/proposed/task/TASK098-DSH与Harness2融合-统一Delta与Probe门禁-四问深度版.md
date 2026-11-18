# TASK098 DSH 与 Harness² 融合 — 统一 Delta 与 Probe 门禁（四问深度版）

> 状态: accepted（用户 2026-11-19 裁决「完整融合，含 Probe 门禁」）
> 创建: 2026-11-19
> 上游: [TASK097 节点继承](../proposed/task/TASK097-节点继承缺口清单与补全计划-四问深度版.md)（G1–G7 已闭合，本任务是它的收敛与扩展）
> 关联: [DSG048 万物皆节点](../design/DSG048-node-plugin-inheritance-architecture.md) §2.1/§2.2、[TASK072 四问深度模板](../implemented/task/TASK072-基准对比调研代理提示词模板-四问深度版.md)
> 用户诉求: DSH「万物皆插件」与 Harness²「可继承」都在解决**复用 vs 污染**，只是方向不同；设计融合方案让 Agent 框架更轻。

---

## 0. 一句话结论

TASK097 把两套机制各自补齐了，但**留存了两处结构性分叉**：结构覆盖走 `NodeProfileResolver.Merge`（Permanent），经验覆盖走 `HarnessComposer.ApplyPatch`（Round），两者产出同一个 `ResolvedProfile` 却没有统一入口；H₀ 重置靠"进程结束"隐式保证，类型层面没表达；G1 的 `RegistrationHandle` 与 G6 的 `Compose` 是两条断开的线。本任务用**一个 Delta 类型 + 一个合并器 + 一个可回滚句柄 + 一个行为门禁**把这四点收敛。

---

## 1. 四问深度法前置

| 问 | 本项回答 |
|----|----------|
| **1 专家隐性实践** | 融合不是"把两套代码拼一起"，而是先找到共同的不变量：**继承 = 声明式差异（delta）+ 后写者胜**。DSH 的 patch/override 与 Harness² 的 cue→adopt patch 都是"对骨架的键级覆写"，区别只在生命周期——前者随 profile 长期存在，后者只活一轮。 |
| **2 原始素材清单** | ① DSH `cordis.patch.yml`（DSG048 §2.1.4，后写者胜）；② DSH `ctx.effect()` 可逆注册（§2.1.3）；③ Harness² `write_tree` 省略即继承 + Tree dict（§2.2.2）；④ Harness² Library 三字段 + H₀ 重置（§2.2.4 + 论文）；⑤ 本仓 `NodeProfileResolver.Merge`/`HarnessComposer.ApplyPatch`/`RegistrationHandle` 一手实现。 |
| **3 具体方法与工具** | ① 先读代码实证再设计（已完成只读调研，见 §3）；② 只编改动 csproj：`dotnet build modules/apps/cli/JoinCode.csproj -c Debug --no-restore ...`；③ 真机 `jcc probe` / `jcc experience` / `--dump-profile`；④ Probe 复用生产 `FixedWindowRateLimiter.TryAcquire()` 发真实请求，禁止只打印配置当行为对比。 |
| **4 独立验证来源** | ① 真机 exe 行为（唯一验收标准）；② 论文 benchmark：成品继承导致 Code 73.4→63.2 回退（H₀ 红线证据）；③ 跨生命周期组合矩阵（Permanent 单独 / Round 叠加 / Dispose 回滚）；④ 635 工具与 TASK097 G1–G7 零回归。 |

---

## 2. 融合的第一性原理

| 维度 | DSH 结构 delta | Harness² 经验 delta | 融合后 |
|------|----------------|--------------------|--------|
| 本质 | 对骨架的键级覆写 | 对骨架的键级覆写 | **同一个 Delta 类型** |
| 生命周期 | 随 profile 长期存在 | 只活一轮 | **Lifetime 显式化**：Permanent / Round |
| 合并语义 | 后写者胜 | 后写者胜 | **同一个合并器**，同一套覆盖语义 |
| 来源 | YAML inherits/overrides | cue 命中后显式 adopt | 两条来源，一个入口 |
| 验证 | 靠人读配置 | 靠 Probe 行为对比 | **统一 Probe 门禁**（可选启用） |
| 回滚 | ctx.effect disposer | 每轮从 H₀ 重启 | **一个 CompositionHandle**：Dispose 即 Round 层回滚 |

**关键洞察**：H₀ 重置可以降格为"合并时过滤掉 Round delta"——H₀ 不再是一个特殊状态，而是"只喂 Permanent delta 的合并结果"。这既保住论文红线，又消除了两套继承并存。

---

## 3. 缺口实证（代码级，非文档标签）

| # | 缺口 | 证据 | 影响 |
|---|------|------|------|
| F1 | 两套合并器入口分叉 | `NodeProfileResolver.Merge`（`modules/composition/composition/node_registry/profile/NodeProfileResolver.cs:63-131`）vs `HarnessComposer.ApplyPatch`（`modules/composition/composition/improvement/HarnessComposer.cs:63-89`） | 同一语义两份实现；新增 delta 来源要改两处 |
| F2 | 生命周期隐式 | H₀ 靠"进程结束/Compose 不接收上轮成品"保证；无 `Lifetime` 类型 | 类型层面无法区分"该留"与"该弃"，易被后续改动破坏 |
| F3 | G1 与 G6 断线 | `RegistrationHandle` 只回滚节点工厂注册；`HarnessComposer.Compose` 只产出不可变 `ResolvedProfile`，不动注册表 | "每轮组合"没有统一的回滚句柄 |
| F4 | 无行为门禁 | `ImprovementStore.LoadAdoptedPatches` 只按 cue 收集 patch 直接进 `ComposeSafe`；无采纳前行为对比 | 与 Harness² Probe→Compose 的真实循环有差距；坏经验只靠 fail-closed 兜底 |
| F5 | round 硬编码 base | `ExperienceCommand.cs:146/161` 写死 `"base"`；生产链用实际 profileName | 预览与生产不一致，H₀ 语义易误解 |

---

## 4. 设计方案

### 4.1 统一 Delta 模型（`CompositionDelta`，composition/improvement/）

```csharp
public enum DeltaLifetime { Permanent, Round }

public sealed record CompositionDelta(
    string Consumer, string Node, string ConfigKey, string ConfigValue,
    DeltaLifetime Lifetime, string? Cue = null, string? Evidence = null);
```

- 结构 override/includes → `Permanent`（长期随 profile）；经验 patch → `Round`（单轮）。
- `Cue`/`Evidence` 只在 Round 有意义，保留 Harness² 三字段可解释性。
- AOT 安全：`sealed record` + 源生成 JSON context（仿 `ImprovementJsonContext`），无反射。

### 4.2 单一合并器（`DeltaComposer`）

```
Compose(骨架 ResolvedProfile, IReadOnlyList<CompositionDelta> deltas) → CompositionHandle
```
- 内部只有一个 `ApplyConfigDelta(ResolvedProfile, CompositionDelta)` 原语（consumer/node 存在性校验 + 键级覆写），Permanent/Round 都走它。
- `H₀ = Compose(骨架, Permanent-only deltas)`；Round delta 只在显式采纳时叠加。
- 保留 `NodeProfileResolver.Merge` 处理 YAML 继承链（excludes/includes/overrides 的专门语义不重写），在其产出的 `ResolvedProfile` 之上统一叠加键级 delta——**不推倒重来，收敛入口而非重写已验收逻辑**。

### 4.3 可回滚句柄（`CompositionHandle : IDisposable`）

- 持有 `H₀`（Permanent-only 结果）+ `Current`（叠加 Round 后）+ 本轮 `RegistrationHandle` 列表。
- `Dispose()`：先 LIFO 回滚所有 `RegistrationHandle`（G1），再把 `Current` 恢复为 `H₀`（G6）——**一个句柄同时兑现两种回滚**。
- `Replace`/`Adopt` 幂等；已 Dispose 后操作抛 `ObjectDisposedException`。

### 4.4 Probe 行为门禁（`BehaviorProbe`，可选启用）

- **真实行为**：对给定 profile 的 `chat-pipeline/stream-rate-limit-mw.max-requests` 构造生产同款 `FixedWindowRateLimiter(max, window)`，发固定 N 个真实 `TryAcquire()`，记录 allow/deny 数作为行为指纹。
- **对比**：H₀ 指纹 vs 叠加候选 delta 后指纹；差异必须与候选经验的 `evidence` 声明一致，否则拒绝采纳（fail-closed 回 H₀）。
- **可选**：默认关闭，由 `JCC_PROBE=1` 或 `jcc probe` 显式启用——不拖慢正常启动，不违背"轻"。
- **非 ground truth**：对齐 Harness² 行为对比精神，用确定性真实请求而非标准答案。

### 4.5 CLI `jcc probe`

- `CliSubCommand` 新增 `[EnumValue("probe")]`（生成器自动产出扩展，不手写）。
- `ProbeCommand`（internal）：`jcc probe --cue <cue> [--adopt <cue>]` 打印 H₀ 指纹、候选指纹、门禁判定与原因。
- 复用 `FlatSubCommandRouter` 路由，`GetPositional`/`GetOptionValue`/`DetectUnknownOptions` 保持既有约定。

### 4.6 生产链接入

- `DataDrivenPipelineComposition.cs:306-322` 的 `IProfileManager` 工厂改走 `DeltaComposer.Compose(...)`，返回 `CompositionHandle` 注册到 DI（生命周期随宿主）；`JCC_PROBE=1` 时先过门禁再采纳 Round delta。
- 保留 `ComposeSafe` 的 fail-closed 语义（不兼容 patch 不拖垮启动）。

---

## 5. 真机 exe 验收（唯一标准，非单测）

| # | 真机命令 | 期望 | 禁止 |
|---|----------|------|------|
| 1 | `jcc probe --cue rate-limit`（无 adopt） | H₀ 指纹 `allow=30 deny=5`（发 35 请求），候选==H₀，判定"无差异不采纳" | 只打印 config 值 |
| 2 | `jcc probe --cue rate-limit --adopt rate-limit`（library 有 patch=50） | 候选指纹 `allow=50 deny=0` 或按 N 调整，门禁 PASS | 单测断言 limiter |
| 3 | `jcc probe` 配坏经验（evidence 声明与行为不符） | 门禁 FAIL，fail-closed 回 H₀，exit=0，四段式引导 | 崩溃或静默采纳 |
| 4 | `jcc experience round --adopt rate-limit` / 下一轮 `--adopt` 空 | 30→50→30（TASK097 G6 红线零回归） | — |
| 5 | `JCC_EXPERIENCE_ADOPT=rate-limit jcc experience effective`（+新进程不带） | 生产消费端 50→30（零回归） | — |
| 6 | `jcc --dump-profile` / `--dump-nodes` | Permanent delta 生效；176 节点；metadata v=2.1.0 | — |
| 7 | `JCC_DEBUGLOG=1` 注册→替换→卸载 | G1 LIFO 零回归 | — |
| 8 | `jcc tool tree` | 635 工具零回归 | — |
| 9 | 1140+ 测试 | 仅回归网，不作验收依据 | 单测代替真机 |

---

## 6. 风险与缓解

| 风险 | 缓解 |
|------|------|
| 改生产链影响 635 工具 / AOT | 只收敛入口不重写 `NodeProfileResolver.Merge`；改后重编 CLI 并真机跑 `tool tree` |
| Probe 依赖 `FixedWindowRateLimiter` 引入新跨域引用 | composition 已引用 Infrastructure 且 `global using Core.Utils`（已实证），无新增 ProjectReference |
| Probe 默认开拖慢启动 | 默认关闭，`JCC_PROBE=1` 显式启用 |
| 行为指纹受时间窗口影响偶发 | 发请求数 N 固定且远小于 window 内容量；窗口 60s，探针瞬间完成 |
| AOT 反射 | Delta DTO 用源生成 JSON；无 `Assembly.Load`/`Activator` |
| 门禁过严挡住合法经验 | 门禁只拦"行为无变化但经验声称有变化"与"行为反向"两类；其余放行 |

---

## 7. 真实边界（不越界宣称）

- Probe 只针对**已存在节点的可配置键**（当前实证载体 = `max-requests`），不能凭空验证新节点。
- Probe 是确定性行为对比（真实 limiter 请求），不是语义检索、不调用 LLM。
- 融合收敛的是**键级 delta 覆盖入口**；`NodeProfileResolver.Merge` 的 excludes/includes/priority 专门语义保留为结构层前置处理，不强行塞进统一 Delta。
- H₀ 重置从"进程结束隐式"升级为"Lifetime 显式过滤"，但生产启动仍是每进程一次 Compose，不做进程内多轮自动切换（除显式 probe 命令）。
- JCC_PROBE 门禁默认 off——"完整融合"指能力齐全，不指默认全开变重。

---

## 9. 实现与真机验收（2026-11-19）

### 9.1 落地清单

| 部件 | 文件 | 说明 |
|------|------|------|
| 统一 Delta + 生命周期 | `composition/improvement/CompositionDelta.cs` | `CompositionDelta` record、`DeltaLifetime{Permanent,Round}`、`CompositionHandle`（Track + LIFO + H₀ 恢复） |
| 单一合并器 | `composition/improvement/DeltaComposer.cs` | `Compose`/`ComposeSafe`，唯一键级覆写原语；Permanent 构成 H₀、Round 叠加 Current |
| Probe 门禁 | `composition/improvement/BehaviorProbe.cs` | 复用生产 `FixedWindowRateLimiter.TryAcquire()` 发真实请求，allow/deny 指纹判定 |
| CLI 命令 | `apps/cli/.../ProbeCommand.cs` + `CliSubCommand.Probe` + 路由 | `jcc probe [--adopt cue]`，生成器自动产出枚举扩展 |
| 生产链接入 | `pipelines/.../DataDrivenPipelineComposition.cs` | `JCC_PROBE=1` 时先过门禁再采纳；FAIL → fail-closed 回 H₀ |
| 入口收敛 | `composition/improvement/HarnessComposer.cs` | `Compose`/`ComposeSafe` 委托统一 DeltaComposer；删除原 `ApplyPatch`（覆写逻辑物理上只剩一份） |
| 回归网 | `composition.tests/improvement/FusionDeltaTest.cs`（8 测试）+ `CliSubCommandEnumTests` | 防未来回归，非验收依据 |

### 9.2 真机 exe 验收（唯一标准）

| # | 命令 | 实测输出 | 判定 |
|---|------|----------|------|
| 1 | `jcc probe` | `allow=30 deny=30`（max=30，生产 limiter） | ✅ 基线来自真实请求非配置打印 |
| 2 | `jcc probe --adopt rate-limit`（patch=50） | 候选 `allow=50 deny=10`，`PASS +20` | ✅ 行为差异成立才采纳 |
| 3 | `jcc probe` 配 no-op（patch=30） | `FAIL 行为零差异`，回 H₀ | ✅ 死经验被拒 |
| 4 | `jcc probe` 配 reverse（patch=20） | `FAIL 行为反向`，回 H₀ | ✅ 改紧系统被拒 |
| 5 | `JCC_PROBE=1 ... JCC_EXPERIENCE_ADOPT=rate-limit jcc experience effective` + reverse 库 | `[PROBE-GATE] 拒绝` → `Current.max-requests=30` | ✅ **门禁真实进生产消费端**（非观测命令） |
| 6 | 同上 + 好经验库（50） | `Current.max-requests=50` | ✅ PASS 放行 |
| 7 | 默认 `JCC_PROBE` 关闭 + reverse 库 | `Current.max-requests=20` | ✅ TASK097 原行为零回归 |
| 8 | `JCC_DEBUGLOG=1 jcc --dump-nodes` | `[T098-FUSION] current-max=50 after-dispose-max=30 reg-before=reg-B reg-after=removed disposed=True` | ✅ 一个句柄同时兑现 G1 LIFO + G6 H₀ |
| 9 | `jcc experience round`（无 adopt / adopt / 下一轮无 adopt） | 30 → 50 → 30 | ✅ TASK097 H₀ 红线零回归 |
| 10 | `jcc tool tree` | 13 组 / 635 工具 | ✅ 零回归 |
| 11 | `jcc --dump-nodes` | 176 节点归纳 + meta v=2.1.0 | ✅ G4/G5 零回归 |

测试网：Composition.Tests **1111/1111**（1103 原有 + 8 新增）；Host.Tests CLI 枚举/路由 **38/38**。仅回归网，非验收依据。

### 9.3 差评与修复记录

**P0：生产链门禁首版是"假绿灯"**。首轮真机 `JCC_PROBE=1` + reverse 库，`effective` 仍返回 20，没有门禁日志——正是本项目反复栽过的"打印≠生效"。定位过程：
1. 先怀疑 DLL 未更新 → 时间戳显示已更新，排除；
2. 加 `[T098-GATE-ENTRY]` 诊断打点 → **完全无命中**，说明工厂代码根本没进可执行链；
3. 用 Python 按 UTF-16 搜 DLL 字符串 → `T098-GATE-ENTRY`/`JCC_PROBE` 均不存在，确认改动未编译进去；
4. 根因：`DataDrivenPipelineComposition.cs` 属于**独立的 `modules/composition/pipelines/Pipelines.csproj`**，不属于 `Composition.csproj`。此前只编了后者，改动从未进产物。

修复：重编正确的 `Pipelines.csproj` → DLL 含新字符串 → 门禁真机命中，坏经验被拦回 30。诊断打点在验收后移除并重新编译验证。

**教训固化（写入项目约定）**：改 `pipelines/` 下的文件必须编 `Pipelines.csproj`，不能只编 `Composition.csproj`；"编译成功"不等于"改动进产物"，跨库改动要用字符串搜索或打点确认产物含新代码。

**P1：测试断言写法**。FluentAssertions `Equal("a","b","reason")` 的第三参是 `because` 不是集合元素，导致 LIFO 断言失败；改为 `Equal("second","first")` 修正。

**门禁拦截**：JCC9305（句柄未释放，4 处）、JCC9306（集合内 IDisposable 未遍历释放）、JCC11003（避免 null-forgiving，用 `?? throw`）各拦一次，全部按规范修正而非抑制。JCC9306 用 `foreach(_registrations.AsEnumerable().Reverse())` 同时满足 LIFO 语义与完备释放检测。

### 9.4 TASK098 判定

融合完成：DSH 结构 delta 与 Harness² 经验 delta 收敛为**一个 Delta 类型 + 一个合并器 + 一个可回滚句柄 + 一个行为门禁**。G1 可逆注册与 G6 H₀ 重置由 `CompositionHandle.Dispose` 统一兑现；Probe 从"观测命令"升级为"生产采纳门禁"，且默认关闭保持轻量。真实边界：键级 delta 只覆写已有 config 键、Probe 只验证 `max-requests` 这类已存在键的确定性行为、非语义检索、门禁默认 off。

## 10. 进度

| 阶段 | 任务 | 状态 |
|------|------|------|
| 1 | 提案文档 | ✅ |
| 2 | 统一 Delta 模型 + 合并器 + 可回滚句柄 | ✅ |
| 3 | Probe 行为门禁 + jcc probe 命令 | ✅ |
| 4 | 生产链接入 + 真机 exe 验收 | ✅ |
| 5 | 差评修复 + 文档固化 + 提交 | ✅（提交见下） |
