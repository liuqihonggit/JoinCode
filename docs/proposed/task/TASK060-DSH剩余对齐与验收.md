# TASK060 — DSH 剩余对齐与验收收尾

> 延续 [TASK058](TASK058-DSH引擎深度对齐与DTO统一.md)（B1–B8 已落地并真机验收）与
> [ADR 0143](../adr/architecture/0143-dsh-web-frontend-hosting.md)。
> 目标：把「已实现但验证手段过期」与「尚未实现的成块能力」分清，先恢复自动化验收能力，再逐项推进。

## 一、现状结论（2026-10-21 检查）

- TASK058 B1–B8 已实现且单测绿（`native_gui/tests/join_code_gui.tests` 270 通过 / 0 失败），NativeGui.Host Debug 编译通过。
- 目录已迁移到 `native_gui/src/NativeGui.Core/web_host/*`（ADR 0145），`modules/apps/gui/*` 旧路径不再存在。
- **遗留不一致**：`non_deliverables_tools/dsh_parity/dumps/*.mjs` 仍按旧存储/旧路径断言：
  - `dsh-webui-prompt.mjs` 断言 `sessions/<id>/transcript.json`（现为 `transcript.jsonl`，ADR 0144）。
  - `dsh-webui-history.mjs` 以 JSON 数组写 `transcript.json` 播种历史（现引擎优先读 `.jsonl`）。
  - `make-jcc-home-messages.mjs`、`make-fixture-session.mjs` 同样按旧格式写种子数据。
- E2E 启动方式仍可用：`NativeGui.Host.exe` 默认进 DSH 前端（`--webui` 已并入默认路径，`--webui-smoke` 是遗留自研面）。

## 二、剩余能力盘点

| # | 能力 | 现状 | 处理 |
|---|------|------|------|
| 1 | E2E 脚本与新存储/路径同步 | 断言过期 | **本批 B9**（恢复回归能力） |
| 2 | 审批弹窗真机交互验收（TASK058 B1 未竟项） | `$events` 桥已挂，未实测点击 | **B10**（桩触发权限工具 + 截图验收） |
| 3 | steer（轮内注入） | `session/steer-unavailable` 诚实拒绝 | **B11**（先 ADR/spec，再改引擎 StreamAsync） |
| 4 | `assistant-stream` 逐字流式 | `revision=0` | 依赖引擎增量事件边界，随 B11 一并评估 |
| 5 | terminal（10 方法）/ goals（7 方法+事件）/ workspaceFiles/changes | 未实现 | ⬜ 另立项目（需引擎大扩展），本任务不展开 |

## 三、批次计划（每批 红测试 → 实现 → 编译 → 绿测试 → 文档 → commit）

| 批次 | 内容 | 状态 |
|------|------|------|
| B9 | E2E 脚本同步：`transcript.jsonl` 优先/`.json` 回退、播种改写 JSONL、exe 路径与文档对齐 | ✅ 已验收（见 §四） |
| B10 | 审批弹窗真机验收：桩返回需审批的工具调用 → 断言 `approval/request` waterfall → 前端面板 → `$events/result` 回帖 → 引擎按判定放行/拒绝 | ⬜ |
| B11 | steer 引擎扩展：ADR 记录轮内注入点（Actor 邮箱/邮箱队列）→ `StreamAsync` 支持下一 step 注入 → 宿主 `mode:"steer"` 路由 + 事件投影 | ⬜ |

## 四、B9 验收（四列）

真机 exe 环境（ADR 0080）：
- 隔离 home `%TEMP%\opencode\dsh-m2c2-e2e\home`（最小 `settings.json`：`vendor.stub → http://127.0.0.1:8899`，`STUB_API_KEY=stub-key`），
  工作区 `%TEMP%\opencode\dsh-m2c2-e2e\ws-m2c2`（`JCC_DIRECTORY_PICK` 注入），WebView2 目录 `...\webview2-b9`，
  `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`。
- 启动：`Start-Process artifacts\bin\NativeGui.Host\Debug\net10.0-windows10.0.26100.0\NativeGui.Host.exe`（默认即 DSH 前端）→ `node dumps/wait-webui.mjs`。

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| `TranscriptService.ResolveTranscriptFile`（`.jsonl` 优先、`.json` 回退，ADR 0144） | `dsh-webui-history.mjs` 播种 `sessions/history-e2e/transcript.jsonl` → 引擎读回 → 快照含 `user/message`+`assistant/message` → 界面渲染历史问答 | ✅ | ✅ ALL_PASS（16 项）：`快照历史页非空且光标为最后 seq`、`历史页含用户与助手消息事件 turn/start,step/start,user/message,assistant/message,step/end,turn/end`、`界面渲染出历史问答` 全过 |
| 引擎写侧 JSONL 落盘 | `dsh-webui-prompt.mjs` 读 `sessions/<id>/transcript.jsonl` 断言用户/助手文本 | ✅ | ✅ ALL_PASS（23 项）：`用户消息落盘到 transcript.jsonl`、`助手回复落盘到 transcript.jsonl bytes=974`、`sessions/prompt 受理回执 accepted`、`无流式错误帧` |
| exe 路径 / 启动方式对齐（`NativeGui.Host.exe`，默认 DSH 面） | `launch-webui.mjs`、`wait-webui.mjs` | ✅ | ✅ `GUI_READY`（超时由 15s 提到 45s：引擎组装 `CreateGuiSessionAsync` 实测 ~7.6s + WebView2 就绪，CDP 端口 ~17–20s 才开） |
| 回归基线（改协议层后必跑） | `tool/cancel/queue/reload` 四脚本 | ✅ | ✅ `dsh-webui-tool.mjs` ALL_PASS（工具卡片 `call_stub_1` state=error）、`dsh-webui-cancel.mjs` ALL_PASS（turn/end aborted）、`dsh-webui-queue.mjs` ALL_PASS（两条 queue 串行两轮）、`dsh-webui-reload.mjs` ALL_PASS（`session/page 页尾对齐 throughSeq=27`） |
| `make-jcc-home-messages.mjs` 双写 `gui.json`（旧存储回退）+ `transcript.jsonl`（引擎入口） | 自研面遗留夹具 / `dsh-jcc-home-messages` 目录 | ✅ | ⬜ 未跑（依赖已废弃的自研 WebUI 面，随 M3 删除再定去留） |

### B9 遗留观察（不影响验收，记录备查）

- **脚本无节制重载导致偶发 UI flake**：`dsh-webui-queue.mjs` 首次运行报 `element was detached from the DOM, retrying` 超时，重跑即 ALL_PASS。根因是点「发送」瞬间队列重排使节点被 React 重挂载，`page.click` 重试耗尽。属 E2E 脚本侧稳健性问题（工具目录 gitignored），后续可改为 `page.getByRole('textbox').fill + press('Enter')` 或 `locator.click({force:true})`。
- **窗口尺寸被外部改小会让断言假失败**：实测 GUI 窗口曾从 1440×900 缩到 533×458，侧栏折叠成 56px rail，`工作区 ws-m2c2 就绪` 断言随之超时（与代码无关）。E2E 前应确认窗口尺寸；必要时脚本显式断言 `window.innerWidth >= 1000`。


<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 先修 E2E 脚本再推进新能力 -->
<!-- 原因: 脚本断言过期会让后续每批实现失去可信回归手段；审批/steer 都需要 script 级验证 -->
<!-- 替代方案: 先做 steer 引擎扩展（风险高且无回归护栏）—— 暂不采用 -->
<!-- 验证: TASK060 建档，B9 起步 -->
