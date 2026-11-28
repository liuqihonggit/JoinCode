# TASK144 — SDK 输入侧控制面骨架（Phase 0：只搭结构，不接线）

> 承接 [TASK143](TASK143-SDK控制协议权限模式子集.md) §6「本轮不做」。
> 基准：`w2` = `origin/main` = `9ba381c08`（2026-11-28 核对）。
> **本轮约束（用户指令）：SDK 输入侧的接入先搭骨架，不直接改现有实现。**

## 1. 为什么需要骨架

当前 SDK 输入侧每接入一个 `control_request` 子类型，要同时改三处：

| 位置 | 现状 | 每接一个 subtype 的代价 |
|------|------|------------------------|
| `SdkStreamControlRequestBody` | `mode` 字段直挂 DTO | 加字段（`model` / `mcpServers` / …）回到共享 DTO 上 |
| `SdkStreamInputParser.InterpretControlRequest` | subtype 的 if 分派 + 缺字段判定 | 加一段 if，判定与文案散在解析器里 |
| `NonInteractiveModeRunner` 读循环 | 拒绝 → interrupt → 权限 三段 if | 加一段 if，执行与回包写在宿主循环里 |

三处里最危险的是第三处：**「只要对方给了 request_id 就必须回包」这条协议不变量目前靠人肉维护**
（TASK143 就是因为漏了「缺 mode 只写 stderr」被真机打回一次）。if 链继续生长，漏一条就是
客户端永久挂住 —— 这是不可观测故障，客户端只能超时。

TASK143 台账记了基准侧 `controlSchemas.ts` 共 20 个 subtype，本仓有真实运行时能力的只有
`interrupt` / `set_permission_mode` /（将）`set_model` 三个，其余硬做即自造协议。也就是说：
**subtype 数量会长期停在个位数，但每加的边际成本不该是「改三处 + 人工记得回包」。**

## 2. 骨架形态

三段式，把「解析」「执行」「应答」的边界切开：

```
stdin 行 ──[解析]──> SdkControlCommand ──[SdkControlPlane.Dispatch]──> SdkControlOutcome
                        (RequestId,                │                        (Response 必有,
                         Subtype,                  │                         CancelActiveTurn,
                         Payload:JsonElement)      │                         Diagnostic)
                                                   │
                                     ISdkControlHandler（按 Subtype 注册）
```

| 类型 | 职责 | 关节点 |
|------|------|--------|
| `SdkControlCommand` | 一条控制请求的**执行视图**：request_id + subtype + 原始 `request` 对象 | payload 保留 `JsonElement`，**新 subtype 的字段不必回到共享 DTO 上**（比对 `SdkStreamControlRequestBody` 加字段） |
| `ISdkControlHandler` | 单个 subtype 的「执行 + 应答」 | `Subtype` 即注册键；handler 只关心自己的 payload |
| `SdkControlOutcome` | 处理结果 | `Response` 为 **required**，「必回包」由类型系统兜住 |
| `SdkControlPlane` | 注册表 + 分发 + 兜底 | 未注册 subtype、handler 抛异常、subtype 为空 —— 三种情况**统一回 error**，调用方不必再各写一遍 |
| `SdkControlPlane.SupportedSubtypes` | 支持清单 | 拒绝文案由注册表生成，**清单与能力同源**（避免 TASK143 那样把 `interrupt 与 set_permission_mode` 硬编码进字符串） |

内置两个参照实现（语义必须与 TASK142-B / TASK143 的现网行为逐条一致）：

- `InterruptControlHandler` → `success` 空载荷 + `CancelActiveTurn = true`
- `SetPermissionModeControlHandler` → 缺 `mode` / 未知模式回 error（复用 `PermissionModeApplierFactory`
  的 `ValidPermissionModes` 与 `DescribeFailure`），成功回**实际生效值**

## 3. 本轮范围

**只新增，不改任何现有文件。** 具体：

| 文件 | 动作 |
|------|------|
| `modules/apps/cli/core/output/SdkControlCommand.cs` | 新增：`SdkControlCommand` + `SdkControlOutcome` |
| `modules/apps/cli/core/output/SdkControlHandler.cs` | 新增：`ISdkControlHandler` |
| `modules/apps/cli/core/output/SdkControlPlane.cs` | 新增：注册表 + 分发 + 兜底 |
| `modules/apps/cli/core/output/SdkControlHandlers.cs` | 新增：interrupt / set_permission_mode 参照实现 |
| `test/unit/host.tests/adapters/SdkControlPlaneTests.cs` | 新增：锁协议不变量 |
| `SdkStreamInputParser.cs` / `SdkStreamControlMessage.cs` / `NonInteractiveModeRunner.cs` | **一行不改** |

正因为不接线，本轮**没有真机证据**——骨架的正确性只由单测的协议不变量证明。真机验收留到
Phase 1，台账 §5 记录判据。

## 4. Phase 1 接线计划（下一轮，未开工）

1. `SdkStreamInputParser.InterpretControlRequest` 改为产出 `SdkControlCommand`（保留现有
   `SdkStreamInputLine` 的 user 文本路径不动），删除 subtype 的 if 分派与 `mode` 读取。
2. `SdkStreamControlRequestBody` 的 `mode` 字段删除（字段归 `Payload`），`CliOutputJsonContext`
   不变。
3. `NonInteractiveModeRunner` 读循环把「拒绝 → interrupt → 权限」三段 if 换成一次
   `plane.Dispatch(...)` + `WriteControlResponse(outcome.Response)` + `CancelActiveTurn` 判断；
   `ApplyPermissionMode` 私有方法删除（逻辑已在 handler）。
4. 文案变更点：`SupportedSubtypes` 超过两项时 `" 与 "` 连接读起来别扭，接线时统一改
   `"、"`；该变更同步现有 `SdkStreamInputParserTests` 的断言（现有断言只看「回包 ID + 拒绝原因」，
   预计不受影响）。
5. 验收判据（与 TASK143 §5 同口径）：五种控制行（合法 mode / 非法 mode / 缺 mode /
   未实现 subtype / interrupt）各得**一条且仅一条** `control_response`，stdout 无杂讯。

## 5. 本轮不做

1. **`set_model`**：换模型要走 Rehydrate，链路与权限模式完全不同，需先查清运行时是否已具备
   「不重建会话换模型」的能力，否则就是自造协议。骨架只是把它的接入成本降到「注册一个 handler」。
2. **`initialize` 能力协商**：形状（tools/commands 清单）与消费方都要先查清。
3. **多模态输入**：`content` 里的 `image` 块当前被忽略（`JoinTextBlocks`），需要先有图片输入通道。
4. **`mcp_*` / `get_context_usage` / `rewind_files`**：本仓无对应运行时能力。

## 6. 验证记录

编译与单测（2026-11-28）：

| 项 | 结果 |
|----|------|
| `dotnet build modules/apps/cli/JoinCode.csproj` | 0 警告 / 0 错误 |
| `dotnet build test/unit/host.tests/Host.Tests.csproj` | 0 警告 / 0 错误 |
| `SdkControlPlaneTests`（新增） | 12 通过 / 0 失败 |
| `SdkStreamInputParserTests` + `PermissionModeApplierFactoryTests`（回归） | 20 通过 / 0 失败 |
| `Verify-StyleGate.py` | 总计 1821，退出 0（与基线一致，新增代码未引入新项） |
| `git status` | 仅 6 个新增文件，**零修改** |

12 条单测覆盖的不变量：

1. interrupt → success 空载荷 + `CancelActiveTurn`
2. 未实现 subtype（`set_model`）→ error 且带回原 request_id、文案含当前支持清单
3. 缺 subtype → error（不静默跳过）
4. 缺 request_id → 抛 `ArgumentException`（解析阶段应丢弃，不产生脏应答）
5. handler 抛异常 → 仍回 error（兜底在控制面）
6. subtype 重复注册 → 构造期 fail-fast
7. `SupportedSubtypes` 按注册顺序（清单与能力同源）
8. `set_permission_mode` 成功回**实际生效值**而非请求值
9. 缺 mode → error 含合法值清单
10. 未知模式 → error 文案与 bridge_serve 共用 `DescribeFailure`
11. 引擎无权限系统 → error，不假装成功
12. 自定义 handler 可被分发（扩展点可用）

**本轮无真机证据**：骨架未接线，`NonInteractiveModeRunner` 一行未改，stdin 注入行为与 TASK143 合并后完全一致。
真机验收按 §4.5 判据在 Phase 1 做。
