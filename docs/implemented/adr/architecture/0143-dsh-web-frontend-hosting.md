# 0143 — GUI 托管 DSH 前端并实现宿主 RPC/WS 协议

- 状态：accepted
- 日期：2026-10-20
- 取代：修订 [0138](0138-gui-webview2-webui-rendering.md)（自研 WebUI 渲染方案不再是主路径）

## 背景

GUI 目标是「UI 全量对齐 DSH（DeepSeek Harness）」，同时保留本项目自有 C# 引擎作为后端。
自研 WebUI（`modules/apps/gui/webui/**` + `webview/WebChatFlow` + `PermissionPromptBroker`）每补一个界面都要重写一遍 DSH 的交互细节，
且与 DSH 前端的 DOM/行为持续漂移，对齐成本随功能数线性增长。

DSH 前端产物 `@deepseek-ai/dsh-web-frontend@0.2.0-rc.2` 及其 `dsh-client-ui-*` 插件为 MIT 许可，产物是完整的静态包：
页面通过宿主注入的 `window.__ModuleLoader__` / `window.__DSH_BOOT__` 启动，之后只经由
`POST /api/<method>`（一元）与 `GET /api/remote.mux`（WebSocket 流）与「宿主」通信。
换言之：**前端可以原样托管，只要实现它假定的宿主半边协议**。

## 决策

1. **前端口径**：整体移植 DSH 前端构建产物，不再逐页重写 UI；`dsh_web/dsh_web.zip` 以 `EmbeddedResource` 嵌入 GUI
   （`LogicalName=JoinCode.Gui.dsh_web.zip`），运行时解包到 `{JccDirectory}/dsh_web/0.2.0-rc.2/`（越界防护 + `meta.json` 完成标记）。
   MIT 许可与来源记录在 `modules/apps/gui/dsh_web/{LICENSE,README.md}`。
2. **同源托管**：`DshWebHost` 用 `HttpListener` 监听回环临时端口，同一个 origin 同时提供静态资源、一元 RPC 与 WS。
   同源即可免除 token/cookie 交换；页面 `<base href="./">`，`plugins/??a/client.js&b/client.js` 的 combo 路由由宿主按序拼接（`;` 分隔）。
3. **宿主半边协议优先**：先把 DSH 启动/运行期实际调用到的方法逐个实现（fail-closed + 引导信息），再把数据来源逐步替换为自有引擎。
   未知方法返回 `code=unimplemented` + 「在哪注册处理器」的引导文案，便于 AI/人定位。
4. **不保留后向兼容**：自研 WebUI 不作为主路径维护；`--webui-smoke` 仅留作对照入口，待 M2 后归档。
   `--webui` 启动 `DshWebUiWindow`。
5. **WebView2 配置目录可注入**：`JCC_WEBVIEW_DATA_DIR` 覆盖默认 `~/.jcc/webview2`，
   E2E 用一次性干净目录，避免上次运行残留的 localStorage/IndexedDB 影响断言。

## 协议契约（抓包确证，`non_deliverables_tools/dsh_parity/dumps/dsh-web-snapshot/`）

- 一元：`POST /api/<controller>/<method>`，体 `{type:"client-request", rpcId, method, payload:{args}}`；
  成功 `{type:"server-response", rpcId, result:{ok:true, value}}`；
  失败 `result:{ok:false, error:{code, message, details:{...}}}`（`details` 必须是对象）。
- WS：`GET /api/remote.mux`；client→ `{type:"open", streamId, endpoint, payload:{args}}`，server→ `{type:"item", streamId, value}`。
  启动期端点：`$events`（`{type:"ready", clientId, host:{home}}`）、`workspace/follow`（baseline `items/archived/pinned`）、
  `session/control`（`{type:"baseline", value:{projections:{}}}`）、`session/follow`。
- 启动注入：`window.__ModuleLoader__`、`window.__DSH_BOOT__`（`{rev, entries[], batches[]}`），
  可选 `__DSH_TRANSPORT__`、`__DSH_CONTACT_CONFIG__`。
- 设置命名空间视图（`settings/describe` / `settings/mutate` 返回项）：
  `{autoGenerate, ns, schema, value, base, user, applies:"live", secrets:[{path,set}], revision}`。

### 「schema 信封」是硬契约（M1.5 根因）

`ui-settings-general` 的确认状态由前端 `configForms` 管理：`decode(view)` 用
`new Schema(view.schema)` 还原 schemastery 节点并校验 `view.value`；**`schema` 缺失或非法时解码返回 undefined，
命名空间永远停在 `loading`，依赖它的界面（首个「预览版说明」提示页）根本不渲染**。
因此宿主必须回传与插件 `volatileForm(Config).toJSON()` 同构的信封：

```
ui-settings-general 的 Config = z.object({ welcomeNoticeVersion: z.string().volatile() })
信封 = {"uid":4,"refs":{"3":{"type":"string","meta":{}},
                        "4":{"type":"object","meta":{"default":{}},"dict":{"welcomeNoticeVersion":3}}}}
```

`autoGenerate=false` 对应插件侧的 `settings.configure({auto:false})`；`revision` 每次成功写入 +1，
`expectedRevision` 不符返回 `settings/conflict`。
「确认成功」的判定由前端**读回** `value.welcomeNoticeVersion === "2026-09-28.1"` 完成——宿主假装成功无效。

## 放弃的替代方案

| 方案 | 放弃原因 |
|------|----------|
| 继续补自研 WebUI 逐页对齐 | 每个功能都要复刻 DSH 行为与样式，漂移不可控，成本随功能数线性增长 |
| iframe 跨源嵌入 + token 交换 | 同源回环托管即可，跨源只会引入鉴权与 CORS 复杂度 |
| 无头浏览器 / CDP 自动化驱动 DSH | 需要外挂 Node 运行时与进程编排，产品形态错误（用户要的是桌面 GUI）|
| 把 DSH 前端当成黑盒只做协议 mock | 无法交付真实交互；M1 已用真实协议抓包驱动实现，逐方法替换更可控 |

## 后果

- 前端升级 = 重新打包 `dsh_web.zip` + 回归宿主协议（`capture/vendor-dsh-web.mjs` + `package_dsh_web.py` 可重复）。
- 宿主协议成为稳定边界：自有引擎按 `DshRpcDispatcher.Routes` 的命名方法逐个接入，不侵入前端。
- 需要持续维护「方法实现清单」（见 `docs/implemented/task/TASK053-DSH前端托管与宿主协议对齐任务.md`）。
- 已实现方法之外的调用会得到 `unimplemented` + 引导文案；`directoryPicker/pick`、`workspace/*` 已于 M2-a 接入真实实现（工作区注册表落 `~/.jcc/dsh-workspaces.json`），会话/消息/权限在 M2-b/c/d 接入自有引擎。
- **请求参数形状以生成的远程契约为准**（`typert.remote-client.js` 里每个参数的 `wire` 名）：具名参数收在 `request` 下（如 `workspace/create` 的 `{"request":{"path":…}}`），位置参数平铺（如 `directoryPicker/createDirectory` 的 `{"path":…,"name":…}`）。宿主用 `DshRequestEnvelope<T>` 类型化解析，禁止假设平铺参数。
- **前端只在 `$events` 流连接那一次拉全量列表**（`manager.handleConnected() → refreshList() → session/list`）：凡是「引擎组装完成后才有数据」的列表，必须让启动期的数据源先可用，否则数据要等用户手动重载页面才出现。会话列表因此不绑引擎生命周期 —— 启动即挂轻量落盘会话实例（`State.TranscriptService` 读同一份 `~/.jcc/sessions`），引擎就绪后热切到引擎实例（M2-b1）。
- **新会话必须先是引擎里的实体**（M2-b2）：`session/create` 落 `{id}/meta.json` + 空 `transcript.json`（`ITranscriptService.EnsureSessionAsync`），不靠首条消息才建目录；模型目录同样双来源（启动读 `~/.jcc/settings.json`，引擎就绪后热切 `IJccChatSession.VendorModelMap`）。前端 `create` 成功只是本地 placeholder，会话真正可打开仍需 `session/follow` snapshot 提供投影（M2-b3）。
- **打开会话靠 `session/follow` 的打开快照，且形状是硬契约**（M2-b3）：首帧必须 `{type:'snapshot', header, cursor, records, hasMore, projections, assistantStream}`；`assistantStream` 因前端固定传 `assistantStream:true` 而必需（缺失即 `gateway/internal`）；空日志页 `cursor` 必须为 `-1`（等于前端 `emptyCursor`，页尾光标与请求光标不一致即 `protocolViolation`）。投影一律由引擎落盘会话现算（与 `session/projections` 共用一次读盘），宿主不缓存。打开失败回引导式 `error` 帧（`session/rejected` / `session/not-ready` / `session/unknown`），不静默空响应 —— 静默会让前端停在加载态且无任何可见原因。
- **历史消息是「投影」不是「回放」**（M2-c1）：引擎转录条目（role/content/timestamp）与 DSH 事件形状不同，必须由 `DshTranscriptProjector` 补出 `turn/start→step/start→…→turn/end` 脚手架，并给消息事件打 `surfaceOp:'append'`（否则前端不把消息收进会话表面）。seq 从 0 单调递增、消息 id 用 `{sessionId}:{seq}` 保证可重放；元数据条目（`custom-title`/`agent-name`）与侧链不参与。选择投影而非改造引擎存储，是因为 DSH 的事件词汇（turn/step/流式块）属于界面协议，不该污染引擎转录；代价是工具调用细节要等 M2-c2 用真实流式事件补齐，而不是从历史里反推。
- **发送链路是「宿主先落用户事件、再后台跑引擎」**（M2-c2）：`session/prompt` 是一元 HTTP，宿主立刻回 `{accepted:true}` 并先发布 `turn/start`+`step/start`+`user/message`（`source.rpcId == requestId`，前端据此撤掉待发气泡），随后在后台跑引擎、把结果落成 `assistant/message`+`step/end`+`turn/end`。实时事件由 `DshSessionStreamHub` 广播给已打开的 `session/follow` 流，seq 接续该会话历史光标、单会话同时只允许一轮。选「宿主编排轮次」而非「把轮次交给前端轮询」的原因：DSH 前端只认事件流（无轮询接口），且必须能显示「已完成/失败」这类终态；代价是宿主需要自管轮次占用与 seq 分配（已收敛在一个 hub 里）。
- **v1 不做令牌级流式**（M2-c2）：`assistant-stream` 帧（`start/chunk/end` + attempt revision）暂不实现，回复在引擎本轮流结束后整段落成一条 `assistant/message`。DSH 允许消息事件不伴随流式帧，界面仍能正确渲染，只是没有逐字效果；等引擎的 `Content` 事件直接映射到帧时再接，避免先做一遍「假流式」（攒一半再切块）再删掉。
- **停止生成靠取消令牌，不靠丢弃事件**（M2-c2）：`session/cancel` 拿到 `{sessionId}` 后取消该轮次的 `CancellationTokenSource`，引擎流从头到尾共用这一个令牌（历史灌入 + `StreamAsync`），取消后落 `turn/end{kind:'aborted',reason:{kind:'user'}}` 并释放单轮占用；已产生的部分文本不落 `assistant/message`（与 DSH「停止 = 不产出该轮助手消息」一致）。取消路径必须与 `session/prompt` 的占用在同处（hub）管理，否则会出现「取消后下一轮永远 busy」。
- **工具事件必须有独立消息体**（M2-c3）：引擎的 `ToolCallStart`/`ToolCallEnd` 映射成 `tool/call`（过程事件，不带 `surfaceOp`）与 `tool/result`（`message.role='tool'` + `source{kind:'tool',callId}` + `surfaceOp:'append'`），不把工具文本混进助手气泡；`callId` 由引擎提供，缺失时按会话/轮次补稳定 ID，保证前端能把结果挂回调用卡片。
- **历史翻页走 `session/page` 而不是一次全量**（M2-c3）：前端 `session/follow` 固定传 `turnWindow{minMessages:50,minTurns:2}`，宿主不能忽略——超过阈值时它会调 `session/page` 补齐，未实现就在界面顶部显示「历史加载失败」。宿主按 `beforeSeq`（不含）+ `maxMessages`（默认 100）切片并返回 `hasMore`，让前端无限向上滚动。
