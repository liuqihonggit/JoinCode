# TASK144 — SDK 输入侧控制面骨架 + 接线（Phase 0 → Phase 1）

> 承接 [TASK143](TASK143-SDK控制协议权限模式子集.md) §6「本轮不做」。
> 基准：`w2` = `origin/main` = `2f23c2b6b`（2026-11-28 核对；Phase 0 后被 rebase 到 PR #765 之上，
> Phase 0 提交 hash 由 `402ac803d` 变为 `a5e7c8ac0`，全部门禁在新基线上重跑过 —— 见 §7）。
> **用户指令演进**：先「接入先搭个骨架，别直接改」→ Phase 0 零修改完成后追加
> 「TASK 文档要添加验收方式，并且要一直施工直到验收成功」→ Phase 1 接线到真机通过（§7）。

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

## 3. 施工范围（两阶段）

### Phase 0 — 骨架（已完成，提交 `a5e7c8ac0`，零修改）

**只新增，不改任何现有文件。**

| 文件 | 动作 |
|------|------|
| `modules/apps/cli/core/output/SdkControlCommand.cs` | 新增：`SdkControlCommand` + `SdkControlOutcome` |
| `modules/apps/cli/core/output/SdkControlHandler.cs` | 新增：`ISdkControlHandler` |
| `modules/apps/cli/core/output/SdkControlPlane.cs` | 新增：注册表 + 分发 + 兜底 |
| `modules/apps/cli/core/output/SdkControlHandlers.cs` | 新增：interrupt / set_permission_mode 参照实现 |
| `test/unit/host.tests/adapters/SdkControlPlaneTests.cs` | 新增：锁协议不变量 |
| `SdkStreamInputParser.cs` / `SdkStreamControlMessage.cs` / `NonInteractiveModeRunner.cs` | **一行不改** |

Phase 0 无真机证据（未接线）；正确性由 12 条控制面单测证明。

### Phase 1 — 接线（本轮施工，行为等价）

| 文件 | 动作 |
|------|------|
| `SdkStreamControlMessage.cs` | `SdkStreamControlRequest.Request` 改为 `JsonElement?`（原名保留），删除 `SdkStreamControlRequestBody`（`mode` 等字段归 handler 自读） |
| `SdkStreamInputParser.cs` | `SdkStreamInputLine` 的控制字段统一为一个 `Control: SdkControlCommand?`；删除 `ControlRequestId` / `IsInterrupt` / `RequestedPermissionMode` / `ControlRejection` 与三个工厂；`InterpretControlRequest` 只做降级（校验 request_id → 读 subtype → 保留 payload），不再分派 subtype |
| `NonInteractiveModeRunner.cs` | 三段 if 换成一次 `controlPlane.Dispatch(...)`；按 `CancelActiveTurn` / `Diagnostic` / `Response` 三个字段落副作用；删除 `ApplyPermissionMode` |
| `SdkStreamInputParserTests.cs` | 控制断言改看 `Control`（解析器不判实现） |
| `SdkControlIntegrationTests.cs`（新） | 全链：原始 NDJSON → 解析 → 控制面 → 应答 |

**行为等价性**：分派顺序仍是「取消 → stderr → 回包」；拒绝文案与 TASK143 逐字一致
（`Unsupported control request subtype: …；当前支持 interrupt 与 set_permission_mode。`）。

## 4. Phase 1 实现记录（已完成）

1. 解析器：控制行 → `SdkControlCommand`，`request` 缺失/非对象时 payload 为 `Undefined`、subtype 为空，
   交由控制面回 error（仍带 request_id）。
2. 缺 `request_id`：解析阶段降级为 stderr 诊断，**不产出命令** —— 没有应答对象，
   让 plane 抛异常会把整个读循环带崩。
3. 读循环副作用顺序：`CancelActiveTurn` → `Diagnostic`（stderr）→ `WriteControlResponse`，
   与 TASK142-B/143 的现网顺序一致（interrupt 先取消再回包；拒绝先 stderr 再回包）。
4. 文案：`SupportedSubtypes` 仅两项，`" 与 "` 连接与 TASK143 原文一致，本轮不改分隔符；
   等接入第三项时再用 `"、"`（届时同步 §6 门禁 C 的期望文案）。
5. 旧字段 `IsInterrupt` / `ControlRequestId` / `RequestedPermissionMode` / `ControlRejection`
   全仓零残留（`grep` 确认）。

## 5. 本轮不做

1. **`set_model`**：换模型要走 Rehydrate，链路与权限模式完全不同，需先查清运行时是否已具备
   「不重建会话换模型」的能力，否则就是自造协议。骨架只是把它的接入成本降到「注册一个 handler」。
2. **`initialize` 能力协商**：形状（tools/commands 清单）与消费方都要先查清。
3. **多模态输入**：`content` 里的 `image` 块当前被忽略（`JoinTextBlocks`），需要先有图片输入通道。
4. **`mcp_*` / `get_context_usage` / `rewind_files`**：本仓无对应运行时能力。

## 6. 验收方式（可执行；任一红即回到对应层修，不跳步）

### 门禁 A — 编译（结构正确）

```bash
dotnet build modules/apps/cli/JoinCode.csproj -c Debug --no-restore --nologo -v minimal \
  -p:BuildProjectReferences=false -p:EmitCompilerGeneratedFiles=false
dotnet build test/unit/host.tests/Host.Tests.csproj -c Debug --no-restore --nologo -v minimal
```

判据：两条均 **0 警告 0 错误**。分析器（JCC1017 手写 JSON / JCC10011 元组公开 API /
JCC4008 禁 lock / JCC9305 泄漏）是 error 门禁，不是建议。

### 门禁 B — 单测（语义等价 + 协议不变量）

```bash
dotnet test test/unit/host.tests/Host.Tests.csproj -c Debug --no-build --nologo \
  --filter "FullyQualifiedName~SdkControl|FullyQualifiedName~SdkStreamInputParser|FullyQualifiedName~PermissionModeApplierFactory"
```

判据（缺一不算过）：

| # | 测试 | 锁什么 |
|---|------|--------|
| 1 | `SdkControlPlaneTests`（12 条） | 控制面本身：必回包、兜底、注册清单同源 |
| 2 | `SdkStreamInputParserTests`（改造后） | 控制行产出 `SdkControlCommand`（不再在解析器里分派 subtype） |
| 3 | `SdkControlIntegrationTests`（新增） | 原始 NDJSON → 解析 → 控制面 → 应答 全链，5 类控制行 |
| 4 | `PermissionModeApplierFactoryTests`（回归） | 合法值清单与枚举不漂移 |

### 门禁 C — 真机（协议不变量，唯一不可用单测替代的一环）

```bash
JCC=./artifacts/bin/JoinCode/Debug/net10.0/jcc.exe
printf '%s\n' \
 '{"type":"control_request","request_id":"pm-ok","request":{"subtype":"set_permission_mode","mode":"plan"}}' \
 '{"type":"control_request","request_id":"pm-bad","request":{"subtype":"set_permission_mode","mode":"bogus"}}' \
 '{"type":"control_request","request_id":"pm-none","request":{"subtype":"set_permission_mode"}}' \
 '{"type":"control_request","request_id":"m-1","request":{"subtype":"set_model","model":"x"}}' \
 '{"type":"control_request","request_id":"i-1","request":{"subtype":"interrupt"}}' \
  | "$JCC" --input-format stream-json --output-format stream-json --model nonexistent-xyz
```

判据：

| 请求 | 期望应答（且有且只有一条） |
|------|--------------------------|
| `pm-ok` | `subtype=success`、`request_id=pm-ok`、`response.mode` = **实际生效值** |
| `pm-bad` | `subtype=error`、`request_id=pm-bad`、`error` 含 `bogus` 与合法值清单 |
| `pm-none` | `subtype=error`、`request_id=pm-none`、`error` 含 `mode` |
| `m-1` | `subtype=error`、`request_id=m-1`、`error` 含 `set_model` 与当前支持清单 |
| `i-1` | `subtype=success`、`request_id=i-1`、`response={}` |

附加判据：stdout **只有** NDJSON 协议行（诊断/人类可读文本必须只在 stderr）；
控制行应答总数 = 5；进程退出码 0（无回合失败）。

验收脚本（断言而非肉眼）：

```bash
... | "$JCC" ... > /tmp/task144.out 2> /tmp/task144.err
python - <<'PY'
import json, collections
rows = [json.loads(l) for l in open('/tmp/task144.out', encoding='utf-8') if l.strip()]
ctrl = [r for r in rows if r.get('type') == 'control_response']
ids = [r['response']['request_id'] for r in ctrl]
dup = [k for k, v in collections.Counter(ids).items() if v != 1]
assert len(ctrl) == 5, f'应答数 {len(ctrl)} != 5'
assert not dup, f'重复应答 {dup}'
assert sorted(ids) == sorted(['pm-ok','pm-bad','pm-none','m-1','i-1']), ids
print('OK 5/5 应答唯一')
PY
```

### 门禁 D — StyleGate 不升

```bash
python non_deliverables_tools/scripts/Verify-StyleGate.py
```

判据：退出 0，且 `总计` ≤ 1821（基线）。

### 失败判读

| 现象 | 归因 | 回到哪层 |
|------|------|---------|
| 某 request_id 无应答 | 分发路径漏控制行（解析未产出 command / plane 未分发） | 先查门禁 B 的集成测试，再查 Runner 读循环 |
| 同一 request_id 两条应答 | 老 if 链未删净，与新路径各回一条 | Runner 读循环（应只有一处 `WriteControlResponse`） |
| `response.mode` 等于请求值而非实际值 | applier 传的是请求值而非读回值 | Runner 的 lambda 是否调用 `ToValue()` 前读回 |
| stdout 混入人类文本 | 诊断走了 stdout | `outcome.Diagnostic` 的写出目标 |
| 编译报 JCC1017 | 测试/实现手写 JSON | 改 DTO + `JsonSerializer` |

## 7. 验证记录

### Phase 0（骨架，提交 `a5e7c8ac0`）

| 项 | 结果 |
|----|------|
| `dotnet build modules/apps/cli/JoinCode.csproj` | 0 警告 / 0 错误 |
| `dotnet build test/unit/host.tests/Host.Tests.csproj` | 0 警告 / 0 错误 |
| `SdkControlPlaneTests`（新增） | 12 通过 / 0 失败 |
| `SdkStreamInputParserTests` + `PermissionModeApplierFactoryTests`（回归） | 20 通过 / 0 失败 |
| `Verify-StyleGate.py` | 总计 1821，退出 0（与基线一致） |
| `git status` | 仅 6 个新增文件，**零修改** |

骨架的 12 条不变量：interrupt 空载荷+取消意图、未实现 subtype 回 error 带原 ID 与清单、
缺 subtype 回 error、缺 request_id 抛 `ArgumentException`、handler 抛异常仍回包、重复注册 fail-fast、
支持清单按注册顺序、`set_permission_mode` 回实际生效值、缺 mode 回 error 带合法值、
未知模式与 bridge_serve 共用文案、无权限系统回 error、自定义 handler 可分发。

### Phase 1（接线，2026-11-28）

门禁 A — 编译：

| 命令 | 结果 |
|------|------|
| `dotnet build modules/apps/cli/JoinCode.csproj` | 0 警告 / 0 错误 |
| `dotnet build test/unit/host.tests/Host.Tests.csproj` | 0 警告 / 0 错误 |

门禁 B — 单测：

| 测试 | 结果 |
|------|------|
| `SdkControl` + `SdkStreamInputParser` + `PermissionModeApplierFactory` 过滤 | **39 通过 / 0 失败** |
| `Host.Tests` 全量回归 | **1460 通过 / 0 失败**（16 s） |

门禁 C — 真机（`--input-format stream-json` + 5 条控制行 + `--model nonexistent-xyz`）：

```
$ printf '…5 条 control_request…' | jcc --input-format stream-json --output-format stream-json --model nonexistent-xyz
{"type":"control_response","response":{"subtype":"success","request_id":"pm-ok","response":{"mode":"plan"}}}
{"type":"control_response","response":{"subtype":"error","request_id":"pm-bad","error":"权限模式 'bogus' 无法应用（未知值或引擎未提供权限系统）。合法值：plan, auto, ask, bypass, unattended。bypass 受 settings.json 的 disableBypassPermissionsMode 安全闸管控，被拦截时保持原模式。"}}
{"type":"control_response","response":{"subtype":"error","request_id":"pm-none","error":"set_permission_mode 缺 request.mode；合法值：plan, auto, ask, bypass, unattended。"}}
{"type":"control_response","response":{"subtype":"error","request_id":"m-1","error":"Unsupported control request subtype: set_model；当前支持 interrupt 与 set_permission_mode。"}}
{"type":"control_response","response":{"subtype":"success","request_id":"i-1","response":{}}}
exit=0
```

断言脚本输出：`OK 5/5 应答唯一；mode 实际生效值 = plan；stdout 行类型 = ['auth_status', 'control_response', 'system']`。

判读：
- 5 条控制行 → **5 条应答，无重复、无遗漏**，`request_id` 全部原样；
- 合法 mode 回 `response.mode=plan`（实际生效值）；非法 / 缺 mode / 未实现 subtype 均走 error 且原因可读；
- interrupt 空载荷（对齐基准）；
- stdout 只有 NDJSON 协议行（`system/init`、`auth_status`、`control_response`），
  三条拒绝诊断全部落在 stderr；
- 进程退出码 0。

**本轮真机边界**：无可用 provider（模型列表探测 401），因此没有在途回合，
`interrupt` 的「真打断」仍由 TASK142-B 的 CAS 单测 + 上轮真机覆盖，本轮只验证应答不变量。

### 新基线复跑（rebase 到 `2f23c2b6b` 后，全部重跑，不复用旧结论）

| 门禁 | 结果 |
|------|------|
| A 编译（CLI + Host.Tests） | 0 警告 / 0 错误 |
| B 全量 `Host.Tests` | 1460 通过 / 0 失败（26 s） |
| C 真机（同一 5 条控制行脚本） | `OK 5/5 应答唯一；mode 实际生效值 = plan；stdout 行类型 = ['auth_status','control_response','system']`，exit 0 |
| D StyleGate | 总计 1821，退出 0 |
