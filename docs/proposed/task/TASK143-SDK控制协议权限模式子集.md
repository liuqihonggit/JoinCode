# TASK143 — SDK 控制协议：权限模式子集（`set_permission_mode`）

> 承接 [TASK142](TASK142-后续四项收口台账.md) §5.B 的「仅实现 `interrupt` 子集」。
> 基准：`w2` = `origin/main` = `2be808a90`（2026-11-28 核对）。

## 1. 为什么先做这个子集

基准侧 `controlSchemas.ts` 列了 20 个 subtype，但本仓**有真实运行时能力**的只有三个：
`interrupt`（TASK142-B 已做）、`set_permission_mode`（本轮）、`set_model`（换模型需 Rehydrate，
链路完全不同，未做）。其余（`mcp_status` / `get_context_usage` / `rewind_files` / …）本仓无对应能力，
硬做就是自造协议。

`set_permission_mode` 的另一个理由：bridge_serve 协议面早已实现同一语义
（`BridgeRequest.SetPermissionMode` → `BridgeServeLoop.ApplyPermissionMode` → `permission_mode_changed`），
只是 SDK 输入面没接。**能力是现成的，缺的只是接线。**

## 2. 协议形状（取自基准，不自造）

| 方向 | 形状 | 来源 |
|------|------|------|
| 请求 | `{"type":"control_request","request_id":"r","request":{"subtype":"set_permission_mode","mode":"ask"}}` | `SDKControlSetPermissionModeRequestSchema` |
| 成功 | `{"type":"control_response","response":{"subtype":"success","request_id":"r","response":{"mode":"ask"}}}` | `sendControlResponseSuccess` |
| 失败 | `{"type":"control_response","response":{"subtype":"error","request_id":"r","error":"…"}}` | `ControlErrorResponseSchema` |

**`response` 带 `mode` 键**：基准 schema 是 `z.record(z.string(), z.unknown())`（开放载荷），
带键合法。理由是本仓的 bypass 受 settings.json `disableBypassPermissionsMode` 安全闸管控，
被拦时**实际生效值 ≠ 请求值**——不回实际值，客户端就无从知道真实状态（bridge_serve 的
`permission_mode_changed` 同此语义，两边一致）。

## 3. 开工前发现的协议缺陷（被动发现，不是原计划）

真机第一轮暴露：**缺 `mode` 的 `set_permission_mode` 只写 stderr，没回 `control_response`**。
SDK 客户端会拿着 request_id 永久等待一条不会来的应答。

查基准 `print.ts` 确认这不是"可接受的降级"：

```ts
} else {
  // Unknown control request subtype — send an error response so
  // the caller doesn't hang waiting for a reply that never comes.
  sendControlResponseError(message, `Unsupported control request subtype: ${…}`)
}
```

同一文件 `handleSetPermissionMode` 对 bypass 被禁、auto 门禁不可用等**每种不满足**都回 error。
结论：**只要对方给了 request_id，就必须回包**。于是本轮把范围从「缺 mode」扩到「所有带
request_id 但无法满足的控制行」。

### 结构改动

`SdkStreamInputLine` 原来用 `InterruptRequestId` + `PermissionModeRequestId` 两个 ID 字段，
加上「拒绝」后会出现三个语义词重叠的字段。统一为：

| 字段 | 含义 |
|------|------|
| `ControlRequestId` | 需要回包的 request_id（interrupt / set_permission_mode / 未实现 subtype） |
| `IsInterrupt` | true = 取消在途回合 |
| `RequestedPermissionMode` | 非空 = 改权限模式 |
| `ControlRejection` | 非空 = 回 error，不尝试应用 |

运行器分派顺序：**拒绝优先** → interrupt → set_permission_mode。

## 4. 落地清单

| 文件 | 改动 |
|------|------|
| `modules/apps/cli/core/services/PermissionModeApplierFactory.cs`（新） | 唯一一份「解析 → 应用 → 读回实际值」+ `ValidPermissionModes` 清单 + `DescribeFailure` 文案；`ResolveAsync` 从 DI 取 `PermissionChecker` 并等 `ModeInitialization` |
| `modules/apps/cli/core/output/SdkStreamControlMessage.cs` | `request.mode` 字段；`Success(id)` / `Success(id, mode)` / `Failure(id, error)` 三个工厂；`Response` 与 `Error` 均 `WhenWritingNull` |
| `modules/apps/cli/core/output/SdkStreamInputParser.cs` | 控制字段统一（见 §3）；`set_permission_mode` 分派；缺 `mode` 与未实现 subtype 都返回 `ControlFailure(requestId, reason)` |
| `modules/apps/cli/entry/runners/NonInteractiveModeRunner.cs` | 启动时解析 applier；控制分支按「拒绝 → interrupt → 权限」分派；`ApplyPermissionMode` 成功回实际值、失败回 error |
| `modules/apps/cli/core/commands/core/BridgeServeCommand.cs` | 删除私有 `ResolvePermissionCheckerAsync` / `BuildPermissionModeApplier`，改用工厂（消除重复） |
| `modules/apps/cli/core/commands/core/BridgeServeLoop.cs` | 删除私有 `ValidPermissionModes` 常量，error 文案改用 `DescribeFailure` |
| `test/unit/host.tests/adapters/PermissionModeApplierFactoryTests.cs`（新） | 3 条：无 checker 返回 null、失败文案含清单与安全闸、**清单 ≡ 枚举** |
| `test/unit/host.tests/adapters/SdkStreamInputParserTests.cs` | +4 条：set_permission_mode 识别 / 缺 mode 带 request_id / error 分支形状 / success 带实际值；未实现 subtype 用例改为断言"回包 ID + 拒绝原因" |

**为什么工厂单测不构造真实 `PermissionChecker`**：其构造会读 `JCC_PERMISSION_MODE` 环境变量与
settings.json（`InitializeModeAsync`），与其它测试并行是共享进程态——`guard_security_tests`
为此专门用 `[Collection("permission-mode-env")]` 串行化，跨程序集 collection 不共享。
所以真实应用路径走真机，单测只锁纯逻辑（清单与枚举不漂移、无权限系统不崩）。

## 5. 真机证据

```
$ printf '…5 条 control_request…' | jcc --input-format stream-json --output-format stream-json \
    --model nonexistent-xyz

CTRL success | pm-1 | {'mode': 'plan'} |
CTRL error   | pm-2 | None | 权限模式 'bogus' 无法应用（未知值或引擎未提供权限系统）。合法值：plan, auto, ask, b…
CTRL error   | pm-3 | None | set_permission_mode 缺 request.mode；合法值：plan, auto, ask,…
CTRL error   | pm-5 | None | Unsupported control request subtype: set_model；当前支持 int…
CTRL success | int-1 | {} |
```

判读：**每条带 request_id 的控制行都有且只有一条应答**；interrupt 仍是空载荷（对齐基准）；
非法值 / 缺字段 / 未实现 subtype 都走 error 分支并带可读原因。进程退出码 0（无回合失败）。

## 6. 本轮不做

1. **`set_model`**：换模型要走 Rehydrate（记忆中的既定约束），与权限模式是两条链路，单独立项。
2. **`can_use_tool` / `mcp_*` / `get_context_usage`**：本仓无对应运行时能力，做了就是自造协议。
3. **`initialize`**：本仓已有 `system/init` 输出，SDK 的 `initialize` 是能力协商（tools/commands 清单），
   形状与消费方都要先查清，不塞进本轮。
4. **`update_environment_variables`**：仍静默忽略（基准仅在有该字段时写入 `process.env`，
   本仓流式会话没有环境热更新需求）。
