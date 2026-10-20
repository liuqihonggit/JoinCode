# TASK053 — DSH 前端托管与宿主协议对齐

> ADR：[0143](../adr/architecture/0143-dsh-web-frontend-hosting.md)
> 目标：GUI 直接托管 DSH 前端产物，C# 侧实现 DSH 假定的「宿主半边」协议，随后把数据源逐方法替换为自己引擎。

## 里程碑

| 阶段 | 内容 | 状态 |
|------|------|------|
| M1 | 前端资产打包嵌入 + 回环 HTTP/WS 宿主 + 13 个启动 API 骨架 + 启动注入 | ✅ |
| M1.5 | `settings/describe` / `settings/mutate`（含 schema 信封与 revision）修「预览版说明」确认不关闭 | ✅ |
| M2-a | `directoryPicker/pick` + `workspace/*` 全量 + 工作区注册表持久化（选择工作区闭环） | ✅ |
| M2-b | `session/*`（create/list/follow/control）接自有引擎会话 | 进行中 |
| M2-b1 | `session/list` + `session/rename` + `session/cancel`：侧栏显示引擎已有会话（标题/时间/工作目录/空白标记） | ✅ |
| M2-b2 | `session/create` 落引擎 + `session/control` 基线投影 + `session/modelCatalog` 真实模型目录 + `session/projections` | ✅ |
| M2-b3 | 打开会话：`session/follow` snapshot（含 `projections` + `assistantStream` 基线） | ✅ |
| M2-c1 | 历史渲染：引擎转录 → DSH 事件（`turn/start`→`step/start`→`user/message`→`assistant/message`→`step/end`→`turn/end`，消息带 `surfaceOp:'append'`） | ✅ |
| M2-c2 | 聊天主链路：`session/prompt` 发送 + `session/follow` 增量事件流（`turn/start`/`step/*`/`user|assistant/message`）+ `session/cancel` 停止生成 | ✅ |
| M2-c3 | 工具事件（`tool/call`/`tool/result`）+ 历史向上翻页（`session/page`） | ✅ |
| M2-c3' | 流式增量（`assistant-stream` 帧）+ `queue`/`steer` 模式 + 循环干预旁白过滤 | ⬜ |
| M2-d | 权限：`approval/*` 事件 + 判定回传；`sandbox/mode`、`permission/preset` | ⬜ |
| M3 | 自研 WebUI 与旧桥接归档，`--webui-smoke` 删除 | ⬜ |

## 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| `web_host/DshWebHost.cs`：回环 `HttpListener`，静态资源 + combo 路由（`plugins/??a&b`）+ 一元 RPC + `remote.mux` WS | `DshWebUiWindow` 的 WebView2 导航 | ✅ | ✅ 手动 exe `--webui`：`dumps/dsh-webui-verify.mjs` ALL_PASS（`__DSH_BOOT__` rev=98b55766dc4f，entries=65，batches=3，`#root` 渲染 38056 字符，无 pageerror）+ 截图 `dsh-webui-m1.png` |
| `web_host/DshWebAssets.cs`：zip 解包到 `{JccDirectory}/dsh_web/0.2.0-rc.2/`（越界防护 + `meta.json` 完成标记） | `DshWebHost` 静态根目录 | ✅ | ✅ 同上运行（解包目录 `C:\Users\54076\.jcc\dsh_web\0.2.0-rc.2\`） |
| `web_host/DshRpcDispatcher.cs`：13 个启动方法 + `workspace/list`、`workspace/initializeDefault` | DSH 页面启动 | ✅ | ✅ 重载抓包 `dumps/dsh-webui-reload-trace.mjs`：启动 API 全部 `ok:true`，无 4xx/5xx |
| `web_host/DshRpcProtocol.cs`：请求/响应/流信封 + `DshJsonContext`（AOT 源生成）+ `DshRpcResults` 统一构造 | 分发器与 WS 宿主 | ✅ | ✅ 真实前端解析通过（`__DSH_BOOT__`/settings 视图均被前端接受） |
| `web_host/DshSettingsStore.cs`：`ui-settings-general` 命名空间、路径 set/unset、revision、`settings/conflict`、schemastery schema 信封 | 前端 `configForms` + 预览版说明确认（`WELCOME_NOTICE_VERSION=2026-09-28.1`） | ✅ | ✅ 单测 `DshSettingsStoreTests` 7/7 + 手动 exe E2E `dumps/dsh-webui-ack.mjs` ALL_PASS（提示页出现→点「继续」→提示页关闭→无「无法保存确认状态」）+ 重载后读回 `value.welcomeNoticeVersion`，提示页不再出现 |
| WebView2 配置目录注入 `JCC_WEBVIEW_DATA_DIR`（默认 `~/.jcc/webview2`） | `App.axaml.cs` 两条 WebUI 启动分支 | ✅ | ✅ E2E 用一次性目录 `%TEMP%\opencode\jcc-gui-e2e-ack2`，不污染真实配置 |
| `directoryPicker/pick`（原生文件夹选择，`DshDirectoryPicker`） | 「选择工作区」按钮 | ✅ | ✅ 手动 exe E2E：点击后返回注入路径（`JCC_DIRECTORY_PICK`）并驱动 `workspace/create`，无「无法打开文件夹」 |
| `workspace/*` 全量路由（`DshRpcDispatcher` 实例化异步分发） | 侧栏工作区/会话归属/顺序/归档/置顶 | ✅ | ✅ 单测 `DshWorkspaceStoreTests` 9/9（含分发器参数校验与引导错误）；E2E 见下 |
| `DshWorkspaceStore`：`~/.jcc/dsh-workspaces.json` 注册表（create 去重/rename/delete/insertBefore/insertSessionBefore/attachSession/archive/pin） | `workspace/follow` 基线 + 各 workspace 一元方法 | ✅ | ✅ E2E：重启后无任何选择动作，侧栏工作区行由 `workspace/follow` 基线恢复 |
| `workspace/initializeDefault` → `null`（宿主无默认工作区） | 启动无工作区分支 | ✅ | ✅ 重载抓包确认不再自动弹选择器、无错误横幅 |
| `DshRequestEnvelope<T>`：DSH 生成远程契约的具名参数信封（生成器 `wire="request"`） | 全部 `workspace/*` 请求解析 | ✅ | ✅ 单测按真实信封构造 args；E2E 真实前端请求通过 |
| `web_host/DshSessionStore.cs`：引擎 `ITranscriptService` → DSH 会话条目投影（`sessionId/updatedAt/blank/agentAvailable/running/cwd/projections{title,agentPreset,modelSelection,sessionListMetadata}`） | `session/list`、`session/rename`、后续 `session/follow` 基线 | ✅ | ✅ 单测 `DshSessionStoreTests` 9/9（真实 `TranscriptService` + 内存文件系统）；E2E 见下 |
| 启动即读的轻量落盘会话实例（`TranscriptStore`，`~/.jcc/sessions` 只读投影）→ 引擎就绪后热切到引擎实例 | `DshWebUiWindow` 构造期挂载；`AttachEngineAsync` 切换 | ✅ | ✅ 手动 exe E2E：窗口打开 1s 内侧栏已列出引擎会话，无需等引擎组装 |
| `session/list`（异步分发，上限 200）、`session/rename`（`session/rejected` / `session/not-found` 引导错误）、`session/cancel`（`{accepted:true}`） | 侧栏会话行、会话内重命名快捷键、停止生成 | ✅ | ✅ 单测（含参数校验三态 + 真实落库标题）；E2E 侧栏可见引擎会话标题 |
| `ITranscriptService.EnsureSessionAsync`（`TranscriptService` + `TranscriptFileWriter.WriteEmptyTranscriptAsync`）：幂等建 `{id}/meta.json` + 空 `transcript.json` | `session/create`（DSH「实体先于首条消息诞生」）、`--continue` 可续上空白会话 | ✅ | ✅ 单测 `TranscriptServiceTests.EnsureSessionAsync_*`（建目录/meta/空转录/幂等不覆盖） |
| `session/create`（`DshSessionStore.CreateAsync` + 工作区归属）：参数 `workspaceId`/`cwd`/`sessionId`/`agentPreset`，落引擎 meta（供应商/模型取模型目录默认），`workspaceId` 时 `AttachSessionAsync` | 侧栏「新会话」/ 工作区行新建会话 | ✅ | ✅ 单测 4 例（落盘/幂等保留已有转录/workspaceId 归属/未知 workspaceId 引导）；E2E `dumps/dsh-webui-create-session.mjs` ALL_PASS |
| `session/modelCatalog`（`DshModelCatalogSource`）：启动读 `~/.jcc/settings.json`（`vendor` 预设 + `current.profile`，`JCC_VENDOR` 覆盖），引擎 attach 后热切换 `IJccChatSession.{CurrentVendor,CurrentModelId,VendorModelMap}`；输出 `default/routableProviders/groups/failures` | 模型选择器（消除「正在加载模型…」） | ✅ | ✅ 单测 `DshModelCatalogSourceTests` 4/4；E2E 实调返回 `default={deepseek,deepseek-v4-pro}` + `groups` + `failures:[]` |
| `session/projections`（`DshSessionStore.ProjectionsAsync`，`null` 或 `{asOfSeq,values}`）+ `session/control` 基线真实投影映射（`ProjectionsMapAsync`） | 会话投影懒加载 / `session/control` 基线种子 | ✅ | ✅ 单测 3 例（未接引擎 null/未知会话 null/空白会话 blank=true）；E2E 实调返回空白会话投影 |
| `session/follow` 打开快照（`DshWebHost.OpenSessionAsync` + `DshSessionStore.SnapshotAsync` + `FollowSessionId`）：`header{version,id,createdAt,cwd,isSeeded,agentPreset}` + `cursor=-1` + `records:[]` + `hasMore:false` + `projections` + `assistantStream{revision:0}`；未知会话/子代理地址/存储未就绪走引导式 `error` 帧 | 点侧栏会话打开会话（`manager.retain → SessionEventStream.follow`） | ✅ | ✅ 单测 6 例（未接引擎 null/未知会话 null/空白会话 cursor=-1/多条消息 seq+标题/普通地址解析/子代理地址拒绝）；E2E `dumps/dsh-webui-open-session.mjs` ALL_PASS（19 项，开流与快照头均为被点击会话） |
| 会话历史事件页（`records` 从引擎转录映射 `user/message`/`assistant/message`） | 打开会话看到历史消息（`DshTranscriptProjector` ← `SnapshotAsync`） | ✅ | ✅ M2-c1（`dsh-webui-history.mjs`） |
| 发送主链路：`session/prompt` → 用户消息事件（`source.rpcId` 回执）+ 后台跑引擎 → 助手消息事件 + `step/end`/`turn/end` | 输入框回车/发送按钮，界面出现回复 | ✅ M2-c2（`DshTurnRunner` + `DshSessionStreamHub`） | ✅ `dsh-webui-prompt.mjs` ALL_PASS（19 项，本地 OpenAI 桩） |
| 会话增量事件流：`session/follow` 打开后推送 `{type:'event'}`（seq 全局单调，与本会话历史 seq 接续） | 打开会话期间实时收消息 | ✅ M2-c2 | ✅ 同上（抓到 `turn/start,step/start,user/message,assistant/message,step/end,turn/end`） |
| 停止生成：`session/cancel` 真正取消引擎流，轮次以 `turn/end{kind:'aborted',reason:{kind:'user'}}` 收尾 | 输入框旁「停止」按钮 | ✅ M2-c2（`DshSessionStreamHub.Cancel` + `DshTurnRunner` 取消分支） | ✅ `dsh-webui-cancel.mjs` ALL_PASS（11 项，慢速桩 + 中途取消） |
| 会话列表元数据补 `lastPromptAt`（取最后一条 user 条目时间） | 侧栏排序/相对时间 | ✅ M2-c2（`DshSessionStore.ProjectValues`） | ✅ 单测 2 例 + E2E 断言 `blank:false` + `lastPromptAt` 为数字 |
| 历史页工具事件（`tool/call|result`，含 `callId` 配对与 `surfaceOp='append'`）与向上翻页（`session/page`） | 工具卡片渲染 / 长会话滚动加载 | ✅ M2-c3（`DshTurnRunner.PublishToolCallAsync` + `DshSessionStore.PageAsync`） | ✅ `dsh-webui-tool.mjs` ALL_PASS（13 项）；`dsh-webui-prompt.mjs` 回归 ALL_PASS（历史超 50 条后不再报「宿主尚未实现 session/page」） |
| 流式增量（`assistant-stream` 帧、逐字输出）与 `queue`/`steer` 模式 | 打字机效果 / 中途插话 | ⬜ | ⬜ M2-c3' |
| 会话/消息/流式/权限（`approval/*`、`sandbox/mode`、`permission/preset`） | 工具确认 | ⬜ | ⬜ M2-d |

## M1.5 缺陷记录（缺陷驱动）

1. 现象：首个「预览版说明」提示页点「继续/确定」不关闭；出现「无法保存确认状态」。
2. 红测（实现前，真实 exe）：`dumps/dsh-webui-ack.mjs` → `FAIL 点击后提示页关闭`、`FAIL 无「无法保存确认状态」报错`。
3. 根因链：
   - M1 的 `settings/describe` 返回 `namespaces:[]` → 前端 `configForms` 找不到该命名空间，写路径打到未实现的 `settings/mutate` → 写失败 → 报错。
   - 仅补 `mutate` 仍不够：视图里 `schema:null` → 前端 `decode()` 无法还原 schemastery 节点 → 命名空间停在 `loading` → 提示页干脆不渲染。
4. 修复：`DshSettingsStore` 落库 + `schema` 回传真实信封 + `autoGenerate=false` + revision/冲突；`workspace/initializeDefault` 回 `null` 消除启动错误路径。
5. 绿测：单测 7/7；E2E ALL_PASS；重载抓包显示 `value.welcomeNoticeVersion` 已由宿主返回（证明是宿主落库，不是前端本地关闭）。

## M2-a 缺陷记录（缺陷驱动）

1. 现象：点「选择工作区」后弹窗内报 `workspace create failed: workspace/rejected: workspace/create 缺少 path 参数`。
2. 红测（实现前，真实 exe）：`dumps/dsh-webui-workspace.mjs` → `FAIL 工作区出现在界面` + `FAIL 无「无法打开文件夹」报错`。
3. 根因：DSH 生成的远程契约把具名参数包在 `request` 下（`typert.remote-client.js` 里 `parameters: [{ name: 'request', wire: 'request' }]`），实测请求体为 `{"request":{"path":"…"}}`；宿主按裸 `{"path":…}` 解析 → 取不到路径。
4. 修复：新增 `DshRequestEnvelope<T>`（AOT 源生成注册 6 个闭包类型），所有 `workspace/*` 按信封类型化反序列化；拒绝提示回显收到的 args（`Describe`）便于指认。
5. 绿测：单测 16/16（`DshWorkspaceStoreTests` 9 + `DshSettingsStoreTests` 7）；E2E `dumps/dsh-webui-workspace.mjs` ALL_PASS。

## M2-a 验收记录（ADR 0080 手动 exe）

| 步骤 | 命令/动作 | 结果 |
|------|-----------|------|
| 选择工作区闭环 | `JCC_DIRECTORY_PICK=<临时目录>` + 点击「选择工作区」 | ✅ 侧栏出现 `jcc-e2e-ws`（标题取目录名） |
| 落库 | 读 `{JCC_APP_DATA_FOLDER}/dsh-workspaces.json` | ✅ 含该工作区行（`workspaceId/path/title/createdAt`） |
| 重启恢复 | 关 exe → 重启（无 `JCC_DIRECTORY_PICK`）→ 只读不点 | ✅ 侧栏工作区行由 `workspace/follow` 基线恢复 |
| 无错误横幅 | 读页面文本 | ✅ 无「无法打开文件夹」/「尚未实现」 |

## M2-b1 缺陷/决策记录（缺陷驱动）

1. 现象预判的缺陷：引擎组装 28~32s，而前端**只在 `$events` 流连接时**（`manager.handleConnected()`）调一次 `session/list`；若宿主此时还没接引擎 → 列表返回空 → 侧栏永远空白，直到用户手动重载页面。
2. 红测：`dumps/dsh-webui-sessions.mjs`（隔离 home 预置 `sessions/e2e-session-1/{transcript.json,meta.json}`，含 `custom-title` 条目）在「宿主启动即返回真实列表」前失败。
3. 根因：会话列表是**纯落盘读取**，不需要引擎组装；把「列表」与「引擎」绑在同一生命周期上是错误耦合。
4. 修复：`DshWebUiWindow` 构造期先挂一个轻量 `TranscriptStore`（`State.TranscriptService` + `PhysicalFileSystem`，读同一份 `~/.jcc/sessions`），引擎就绪后 `AttachTranscripts` 热切换到引擎实例（写路径改走引擎 actor，避免两个实例并发读改写同一文件）。顺带 `session/rename` 落库走引擎 `SaveCustomTitleAsync`。
5. 隔离环境缺陷（E2E 基建，非产品）：`JCC_APP_DATA_FOLDER` 隔离后无 `settings.json` → 引擎组装抛「未知的 Provider 'deepseek'」（默认 vendor 在 provider 注册表里不存在）；E2E 需在隔离 home 写一份最小 `settings.json`（`vendor.deepseek` + `current.profile=deepseek`，不含密钥）。补上后日志 `engine attached: transcripts ready`，热切换后列表仍正常。
6. 绿测：单测 25/25（`DshWorkspaceStoreTests` 9 + `DshSettingsStoreTests` 7 + `DshSessionStoreTests` 9）；E2E `dumps/dsh-webui-sessions.mjs` ALL_PASS。

## M2-b2 缺陷/决策记录（缺陷驱动）

1. 现象：侧栏能新建会话，但会话不落盘 —— 关窗即丢，重命名/列表/重启都看不到；模型选择器永远停在「正在加载模型…」。
2. 红测：`dumps/dsh-webui-create-session.mjs`（新 home + 新 workspace，点「新会话」后断言 `session/create` ok、`modelCatalog` 有分组、`projections` 有值）→ 3 FAIL。
3. 根因 A（会话实体）：引擎原来只有 `AppendEntryAsync` 才建目录 → 没有「空会话」概念，`session/create` 只能回显 GUID。修复：`ITranscriptService.EnsureSessionAsync`（meta + 空 `transcript.json`，幂等，已有转录不覆盖），语义对齐 DSH「实体先于首条消息诞生」。副作用：`--continue`（`SessionResumeStep` 取 LastModifiedAt 最新）可能续上一个刚建的空白会话 —— 与 DSH 行为一致，记录备查。
4. 根因 B（模型目录）：`session/modelCatalog` 返回空对象 → 前端 `default` 无供应商/模型 + `modelSelection` 投影缺失。修复：`DshModelCatalogSource` 双来源（settings.json 启动期 → 引擎 `IJccChatSession` 热切换），`failures` 字段补齐（协议必需）。
5. 根因 C（打开会话）：`session/create` 成功后前端只插一条**本地 placeholder**（`blank:true`、无投影），点击该行要靠 `session/follow` snapshot 填投影 —— 属 M2-b3 范围。故 M2-b2 的 E2E 用「刷新页面」验证：`session/list` 从磁盘读回该会话、投影种子到位后模型选择器显示 `deepseek-v4-pro`。
6. E2E 抓包坑：早期监听用 `/api/sessions/session/...`，真实一元 URL 是 `/api/session/<method>`（会话方法不在 `/api/sessions` 前缀下），导致「未捕获到调用」的假失败。
7. 绿测：`TranscriptServiceTests` 22/22（含 2 新增）、`DshSessionStoreTests` 18、`DshModelCatalogSourceTests` 4；GUI 全量 681 通过 / 3 跳过；E2E `dsh-webui-create-session.mjs` ALL_PASS（16 项）、`dsh-webui-sessions.mjs` 回归 ALL_PASS。

## M2-b2 验收记录（ADR 0080 手动 exe）

| 步骤 | 命令/动作 | 结果 |
|------|-----------|------|
| 新建会话落引擎 | 隔离 home（最小 `settings.json`）+ 新 workspace → 点「新会话」 | ✅ `session/create` ok:true + 32 位引擎风格 ID（`e5e08bd3…`） |
| 落盘实体 | 读 `{home}/sessions/{id}/{transcript.json,meta.json}` | ✅ 空转录 `[]` + meta 含 `projectPath/vendor/modelId/createdAt` |
| 模型目录 | 真实 HTTP `POST /api/session/modelCatalog` | ✅ `default={provider:deepseek,model:deepseek-v4-pro}`、`groups=[{id:deepseek,…}]`、`failures:[]` |
| 会话投影 | 真实 HTTP `POST /api/session/projections {"request":{"sessionId":…}}` | ✅ `{asOfSeq:0,values:{title:null,agentPreset:standard,modelSelection:{lastUsed:{deepseek,deepseek-v4-pro}},sessionListMetadata:{blank:true}}}` |
| 重启可见 | 刷新页面（`$events` → `session/list`） | ✅ 会话挂在工作区下（非「未分组」），从磁盘读回 |
| 模型选择器 | 刷新后读页面文本 | ✅ 显示 `deepseek-v4-pro`，无「正在加载模型…」 |
| 无错误横幅 | 读页面文本 | ✅ 无「尚未实现」/「创建会话失败」 |


## M2-b3 缺陷/决策记录（缺陷驱动）

1. 现象：侧栏点会话行，主区切到该会话（标题/「标准模式」/输入框都在），但**没有历史、模型选择器与投影全靠刷新前种下的种子**；关掉重开若引擎未 attach 则整片空白。抓包显示前端确实发了 `open:session/follow`，宿主 `default: return []` 什么都不回。
2. 红测：`dumps/dsh-webui-open-session.mjs`（刷新捕获 WS → 真实 RPC 造两条新会话并重命名 → 再刷新 → 点其中一条 → 断言上行 `session/follow` 的 sessionId 与下行 snapshot 头的 id 都是被点的那条）→ 5 FAIL（无 snapshot、无投影、无 assistantStream）。
3. 根因：`session/follow` 端点未实现。前端 `SessionEventStream.follow` 要求首帧必须是 `type:'snapshot'`，且因为 `assistantStream:true`，**snapshot 必须带 `assistantStream`**（缺失抛 `gateway/internal: session assistant stream omitted its opted-in opening baseline`）。修复：`DshWebHost.OpenSessionAsync` 回打开快照，数据源仍是引擎落盘会话（`DshSessionStore.SnapshotAsync`，与 `session/projections` 共用 `ReadAsync` 一次读盘）。
4. 决策：空日志页尾 `cursor` 必须是 **-1**（`RemoteJournalStream.emptyCursor = -1`，与 `assertPageThrough` 一致：空页的页尾光标必须等于请求光标，否则 `protocolViolation`）；`hasMore:false` 让前端不再发 `session/page`。历史事件映射（`records` 非空）留到 M2-c，避免与消息事件契约（seq/turn/step）搅在一起。
5. 决策：打开失败给**引导式 error 帧**（`{type:'error',error:{code,message,details}}`）而不是静默空响应：`session/rejected`（缺地址/子代理地址 → 提示用 `{address:{kind:'session',sessionId}}`）、`session/not-ready`（存储未就绪）、`session/unknown`（磁盘无该会话 → 提示 `session/create` 或从侧栏选）。
6. E2E 坑（非产品缺陷）：① 前端对**已经打开过**的会话再点不会重新开流（`manager` 复用实例）→ 用例必须点本轮新建、从未打开过的会话；② 页面刷新会还原上次选中的会话并自动开流 → 抓到的第一条 `session/follow` 可能是还原流，断言必须按 `sessionId` 过滤而不是取第一条；③ `dsh-webui-create-session.mjs` 的「点新建 → 行数增加」启发式在同工作区已有空白会话时不成立（前端走 `reuseOrCreateBlank` 复用），改为断言 `session/create` 的响应本身，并等待响应到达（原先立即取 `calls` 会漏）。
7. 绿测：`DshSessionStoreTests` 24 例（新增 4：快照 null 两态 + 空白会话 + 有消息 seq/标题；另有 3 例 `FollowSessionId` 解析）；GUI 全量 688 通过 / 3 跳过；E2E `dsh-webui-open-session.mjs` ALL_PASS（19 项）+ `dsh-webui-create-session.mjs` 回归 ALL_PASS。

## M2-b3 验收记录（ADR 0080 手动 exe）

| 步骤 | 命令/动作 | 结果 |
|------|-----------|------|
| 点会话开流 | 隔离 home + 真实 RPC 建两条会话并重命名 → 刷新 → 点其中一条 | ✅ 上行 `{"type":"open","streamId":…,"endpoint":"session/follow","payload":{"args":{"request":{"address":{"kind":"session","sessionId":"<被点 id>"},"assistantStream":true,…}}}}` |
| 宿主回快照 | 同上读 WS 下行 | ✅ `{"type":"item","value":{"type":"snapshot","header":{"version":1,"id":"<被点 id>","createdAt":…,"cwd":"…","isSeeded":true,"agentPreset":"standard"},"cursor":-1,"records":[],"hasMore":false,"projections":{…},"assistantStream":{"revision":0}}}` |
| 视图切换 | 点另一条会话行后读页面文本 | ✅ 主区标题变 `E2E点击会话` + 「标准模式」+ 「发消息或创建任务」输入框 |
| 点击目标正确 | 抓包按 sessionId 过滤 | ✅ 开流与快照头 id 都等于被点会话，不是刷新还原的那条 |
| 无错误帧 | 读 WS 下行全集 | ✅ 无 `{"type":"error"}`、无 `unimplemented`、页面无「尚未实现」 |

## M2-c1 缺陷/决策记录（缺陷驱动）

1. 现象：会话能打开（投影/标题/输入框都在），但主区**没有任何历史消息**——`records` 恒为空，前端把会话画成空白新会话。
2. 红测：`dumps/dsh-webui-history.mjs`（写一份带两条消息的落盘会话 → 真实 RPC 收养进工作区 → 刷新 → 点该行 → 断言快照 `records` 是消息事件且界面上出现两段文本）。
3. 根因：转录条目（`TranscriptEntry{role,content,timestamp}`）与 DSH 会话事件是两套形状；前端 `Session` 只把带 `surfaceOp` 的消息事件收进会话表面，且靠 `turn/step` 分组渲染。修复：新增 `DshTranscriptProjector` 把条目投影成完整脚手架事件（`turn/start→step/start→user/message→assistant/message→step/end→turn/end`），消息事件带 `surfaceOp:'append'`。
4. 决策：seq 从 0 单调递增、消息 id 用 `{sessionId}:{seq}`（稳定可重放），时间取条目时间戳；元数据条目（`custom-title`/`agent-name`）与侧链（`IsSidechain`）不参与投影。空内容条目跳过（避免渲染空气泡）。
5. 决策：一轮 = 一条用户消息 + 其后的助手消息（step 恒为 1）；会话以助手消息开头时补 `turn/start`+`step/start`。多轮工具循环（step 递增、`tool/call`）留 M2-c2，因为它需要真实流式事件而不是历史速写。
6. 决策：`ReadAsync` 一次读盘同时返回投影/元信息/条目，`session/projections` 与 `session/follow` 不各读一遍；页尾 `cursor` 取最后一条事件的 seq（无事件时仍为 `-1`），`hasMore:false`。
7. E2E 坑（非产品缺陷）：System.Text.Json 默认把中文转义成 `\uXXXX`，对 WS 帧做 `includes('中文')` 必然失败 → 断言必须先 `JSON.parse` 帧再查字段（解析后仍失败才是真缺陷）。
8. 绿测：`DshTranscriptProjectorTests` 5 例（空转录 / 脚手架形状 / 元数据与侧链剔除 / 每轮递增 / 助手开头补轮次）+ `DshSessionStoreTests.Snapshot_WithMessages_ReportsSeqAndTitle` 增补 records/cursor 断言；GUI 全量 693 通过 / 3 跳过；E2E `dsh-webui-history.mjs` ALL_PASS（15 项），`dsh-webui-open-session.mjs`（19 项）与 `dsh-webui-create-session.mjs` 回归 ALL_PASS。

## M2-c1 验收记录（ADR 0080 手动 exe）

| 步骤 | 命令/动作 | 结果 |
|------|-----------|------|
| 种子会话 | E2E 写 `sessions/history-e2e/{transcript.json,meta.json}`（2 条消息 + `custom-title`）+ `session/create` 附到工作区 | ✅ `session/create` 回 `{sessionId:"history-e2e"}` |
| 侧栏行 | 刷新后读页面文本 | ✅ 出现 `历史渲染会话` |
| 快照历史页 | 点该行读 WS 下行 | ✅ `records=6`（`turn/start,step/start,user/message,assistant/message,step/end,turn/end`），`cursor:5`，消息事件 `surfaceOp:"append"` |
| 界面渲染 | 等页面出现两段历史文本 | ✅ 主区显示「历史问题：请介绍仓库结构」+「历史回答：三层结构依次是能力层、模块层、应用层」，右上「1 轮 1 步」、「已完成」 |
| 无错误帧 | 读 WS 下行全集 | ✅ 无 `{"type":"error"}`、无 `unimplemented`、页面无「尚未实现」 |

## M2-c2 缺陷/决策记录（缺陷驱动）

1. 现象：会话能打开、历史能渲染，但输入框回车后只有本地气泡，**引擎不产生任何模型请求**（桩服务零请求），也没有 `turn/end`——一轮永远悬着。
2. 红测：`dumps/dsh-webui-prompt.mjs`（本地 OpenAI 兼容桩 + 真实前端输入框回车 → 断言 `session/prompt` 上行、`user/message`/`assistant/message` 下行、`turn/end`、界面出现回复、转录落盘）→ 首跑 7 FAIL。
3. 根因（产品）：`session/prompt` 完全没实现，前端拿不到 `accepted` 回执，也无人跑轮次；`session/follow` 打开后只回快照、不推增量 → 就算引擎跑了也看不见。
4. 修复：新增 `DshTurnRunner`（校验 → 立刻发 `turn/start`+`step/start`+`user/message` → 后台跑引擎 → `assistant/message`+`step/end`+`turn/end`，异常也以 `turn/end{kind:'error'}` 收尾）与 `DshSessionStreamHub`（streamId 订阅、全局 seq 分配、单会话单轮占用）。`DshWebHost` 改为「打开项 + 租约」：开流先发打开帧，再挂订阅者，后续 `PublishAsync` 逐订阅者推 `{type:'item',value:{type:'event',…}}`。
5. 决策：**先落用户消息再跑引擎**。前端的待发气泡靠「`user/message.source.rpcId == 请求里的 requestId`」来撤除（`observedInputs`），所以用户事件必须在 HTTP 回执前/后尽早到达，且 `rpcId` 必须原样回填。
6. 决策：seq 必须**接续**该会话历史（打开时 `Observe(sessionId, snapshot.Cursor, maxTurn)`），否则前端会把新事件当旧事件去重丢掉；`turn` 号从历史最大轮次 +1 起算，避免同一会话两段 `turn` 号撞车。
7. 决策：v1 不实现 `assistant-stream` 帧（`start/chunk/end`）——引擎的 `Content` 事件在内存里攒齐后整段落 `assistant/message`（DSH 允许无流式帧的消息）。收益是不用处理帧 revision/attempt 契约，代价是没有逐字效果；令牌级流式留 M2-c3。
8. 决策：单会话同时只允许一轮（`TryBeginTurn` 占位，占用中报 `session/busy`），与引擎「一个 chat 实例一条流」的约束一致；跨会话并发由引擎自己排队。
9. 已知缺陷（M2-c3 处理）：引擎会把循环干预提示（`💡 [低频诱导] loop-intervention-health…`）作为 `Content` 混进回复，DSH 界面上表现为助手多一段旁白；需要按事件类型过滤或映射成 `system/message`。
10. E2E 坑（非产品缺陷）：① `session/prompt` 是**一元 HTTP**（`POST /api/session/prompt`），不在 WS 帧里 → 断言要监听 `page.on('request'/'response')`；② 前端刷新会还原上次会话并自动开流，输入框发给了「当前打开的会话」而不是本轮 `session/create` 的那条 → 断言用请求体里的 `sessionId` 反查；③ `lastPromptAt` 在 `projections.values.sessionListMetadata` 里，不在投影顶层。
11. 绿测：`DshTurnRunnerTests` 6 例（正常一轮 / 并发 busy / 未知会话 / 非文本拒绝 / 无引擎 not-ready / 引擎抛错收尾）+ `DshSessionStoreTests` 新增 `List_WithUserMessage_ReportsLastPromptAt`；GUI 全量 700 通过 / 3 跳过；E2E `dsh-webui-prompt.mjs` ALL_PASS（19 项），`dsh-webui-history.mjs`（15 项）、`dsh-webui-open-session.mjs`（19 项）、`dsh-webui-create-session.mjs` 回归 ALL_PASS。
12. 停止生成（同一里程碑补齐）：`session/cancel` 原来只回 `{accepted:true}` 不做事，点停止后轮次照跑。修复：`TryBeginTurn` 同时分配 `CancellationTokenSource`，`Cancel(sessionId)` 取消它，`EndTurn` 释放；`RunTurnAsync` 用该令牌跑 `HistoryAsync`/`LoadHistoryAsync`/`StreamAsync`，捕 `OperationCanceledException` 落 `turn/end{kind:'aborted',reason:{kind:'user'}}`（对齐 DSH `TurnEndReasonMap.aborted + AgentCancelCause.user`）。`session/cancel` 缺参数走 `session/rejected` 引导（args 不是对象时先判 `ValueKind`，否则 `JsonSerializer.Deserialize(default(JsonElement))` 直接抛）。

## M2-c2 验收记录（ADR 0080 手动 exe）

| 步骤 | 命令/动作 | 结果 |
|------|-----------|------|
| 引擎可达（隔离环境） | 隔离 home 写最小 `settings.json`（`vendor.stub` 指向 `http://127.0.0.1:8899`）+ 桩模型 | ✅ 模型选择器显示 `stub-model`，无「正在加载模型…」 |
| 输入框发送 | 真实前端输入框填 `E2E 发送的一条消息` + 回车 | ✅ 上行 `POST /api/session/prompt`，args.request 含 `requestId/sessionId/mode:"queue"/content:[{type:"text",text:…}]`，回执 `{"ok":true,"value":{"accepted":true}}` |
| 用户事件 | 读 WS 下行 | ✅ `turn/start` → `step/start` → `user/message`（`surfaceOp:"append"`，`source.rpcId` = 请求 requestId） |
| 助手回复 | 等界面出现回复文本 | ✅ `assistant/message`（`surfaceOp:"append"`）→ `step/end` → `turn/end{kind:"completed"}`；界面显示「已完成，用时 1秒」+ 回复全文 |
| 转录落盘 | 读 `{home}/sessions/{id}/transcript.json` | ✅ 含用户消息与助手回复（561~1118 字节） |
| 列表元数据 | 真实 HTTP `POST /api/session/list` | ✅ 该会话 `blank:false`、`sessionListMetadata.lastPromptAt` 为毫秒时间戳 |
| 停止生成 | 慢速桩（400ms/块）+ 发送消息 → 中途 `POST /api/session/cancel {request:{sessionId}}` | ✅ 回 `{accepted:true}`；轮次收尾 `{"kind":"aborted","reason":{"kind":"user"}}`；无 `assistant/message`；占用已释放（可继续发下一条） |
| 无错误 | 读页面文本 + WS 全集 | ✅ 无「尚未实现」、无 `{"type":"error"}`、无 `unimplemented` |


## M2-c3 缺陷/决策记录（缺陷驱动）

1. 现象：会话历史累计超过约 50 条事件后，打开会话顶部出现「历史加载失败：宿主尚未实现方法 session/page」。
2. 根因：前端 `session/follow` 请求固定带 `turnWindow:{minMessages:50,minTurns:2}`；宿主忽略该窗口、一次返回全部记录且不实现 `session/page`，前端补齐窗口时调用翻页接口落 `unimplemented`。
3. 修复：实现 `session/page`（`request.address{kind:'session',sessionId}` + `throughSeq` + 可选 `beforeSeq/maxMessages`），返回 `beforeSeq` 之前最多 `maxMessages`（默认 100）条、按 seq 升序，`hasMore` 按是否仍有更早记录置位；地址非法走引导式 `session/rejected`，会话不存在走 `session/not-found`。
4. 现象：引擎工具轮次（`ToolCallStart`/`ToolCallEnd`）在界面上完全不可见，只有最终文本。
5. 修复：流中把 `ToolCallStart`/`ToolCallEnd` 映射为 DSH `tool/call{turn,step,callId,name,arguments}`（不带 `surfaceOp`，属过程事件）与 `tool/result{turn,step,message}`（`message.role='tool'`、`source.kind='tool'+callId`、`isError`、`surfaceOp:'append'`），`callId` 缺失时按 `${sessionId}:${turn}:tool:${n}` 补齐保证配对。
6. 决策：v1 全程 `step=1`（引擎一轮内的多次 LLM 调用不拆 step）。DSH 允许同 step 内多次工具调用；拆分需要引擎暴露 round 边界，留到引擎事件补齐后再做。
7. 决策：`assistant/message` 仍只落「本轮全部文本」，工具事件与文本事件共用同一轮次；不把工具结果拼进助手气泡（`tool/result` 自带消息体）。

## M2-c3 验收记录（ADR 0080 手动 exe）

| 步骤 | 命令/动作 | 结果 |
|------|-----------|------|
| 工具调用下发 | 桩模型返回 `tool_calls`（`StubTool`，`{"marker":"stub-arg"}`）→ 读 WS 下行 | ✅ `tool/call`（`callId:"call_stub_1"`、`name:"StubTool"`、`arguments` 原样、无 `surfaceOp`） |
| 工具结果下发 | 同上继续读 WS | ✅ `tool/result`（`message.role:"tool"`、`toolCallId` 与 `source.callId` 均为 `call_stub_1`、`isError:true`、`surfaceOp:"append"`） |
| 界面渲染 | 读页面文本 | ✅ 出现「工具调用 Error: Tool 'StubTool' not found.」卡片，轮次正常收尾 |
| 历史翻页 | 会话事件超 50 条后重新打开 | ✅ 前端调 `session/page` 得到历史页，页面无「历史加载失败」 |
| 回归 | `dsh-webui-prompt.mjs` / `dsh-webui-cancel.mjs` / `dsh-webui-tool.mjs` | ✅ 三者 ALL_PASS（19 / 11 / 13 项） |


## M2-b1 验收记录（ADR 0080 手动 exe）

| 步骤 | 命令/动作 | 结果 |
|------|-----------|------|
| 侧栏会话 | 隔离 home 预置会话（`custom-title=引擎会话标题`）→ 启动 `--webui` → 读页面文本 | ✅ 侧栏「未分组」下出现 `E2E引擎会话`（字段 `projections.values.title`）+ 相对时间 |
| 启动即读 | 进程启动 ~1s 内取页面文本 | ✅ 会话已在侧栏（不等引擎） |
| 引擎热切换 | 读 `{JCC_APP_DATA_FOLDER}/dumps/startup_timing.log` | ✅ `[DSH-WEB] engine attached: transcripts ready`，切换后列表不丢 |
| 无错误横幅 | 读页面文本 | ✅ 无「尚未实现」/「无法读取」 |
| 重命名落库（非 UI） | 单测 `DshSessionStoreTests.Rename_WritesCustomTitle_VisibleInList` | ✅ 落 `custom-title` 条目并回读（UI 快捷键 `Ctrl+Alt+G` 需先能打开会话 → M2-b3 验收） |


- 启动期 13 个一元方法：`agentPresets/list`、`commands/list`、`credentials/describe`、`dynamicCordisRunner/inventory`、`dynamicCordisRunner/syncInspectManifest`、`llm/listConfigurableProviders`、`llm/listProviders`、`permissionPresets/catalog`、`session/create`、`session/list`、`session/modelCatalog`、`settings/describe`、`skills/list`。
- 运行期额外：`workspace/initializeDefault`（result `undefined | {workspace:{workspaceId,path,title,sessionIds,createdAt,updatedAt}}`）、`directoryPicker/pick`（result `null | string`）、`settings/mutate`（result = 命名空间视图）、静态路径 `open-in-app/apps`、`plugins/events`。
- `settings/mutate` 请求：`{"ns":"ui-settings-general","ops":[{"op":"set","path":["welcomeNoticeVersion"],"value":"2026-09-28.1"}]}`，可带 `expectedRevision`；`set` 操作必须携带 `value`。
- **具名参数信封**：生成的远程契约里每个具名参数都有 `wire` 名；`workspace/*` 全部为 `request`（`{"request":{"path":…}}`），`directoryPicker/createDirectory` 为位置参数 `{"path":…,"name":…}`，`directoryPicker/pick` 无参数，`settings/*` 为 `request`。宿主按此解析，勿假设平铺参数。
- 覆盖范围提示：`directoryPicker/list`、`directoryPicker/createDirectory` 仅浏览器内建选择器用到；原生文件夹对话框（宿主侧 Avalonia）不需要，暂不实现。
- `session/list` 返回 `{items:[…]}`，条目字段：`sessionId/updatedAt(ms)/blank/agentAvailable/running/cwd/archived/pinned/parentSessionId?/projections`。`projections` 在列表里可省略，但**不能给 `null`**（严格 codec 解码失败）；宿主改为总是输出 `{kind:'sequenced', asOfSeq, values}`。
- 侧栏标题来自 `projections.values.title`（`projectList ← buildListSnapshot ← projectionStore.get('title')`）；`updatedAt = max(summary.updatedAt, values.sessionListMetadata.lastPromptAt ?? 0)`；`sessionListMetadata.blank` 决定是否显示「新建会话」占位。
- `projections.values.modelSelection` 缺失会让模型选择停在「正在加载模型…」→ M2-b2 已补（列表投影 + `session/projections` + `session/control` 基线）。
- `session/modelCatalog` 结果顶层字段全必需：`{default:{provider,model,reasoningEffort?}, routableProviders:[], groups:[{id,name,models:[{id,name,description?,reasoning?}]}], failures:[{id,name,message}]}`（少 `failures` 即解码失败）。
- `session/projections`（一元，`request.sessionId`）结果为 `null` 或 `{asOfSeq, values}`（**无** `kind` 字段，与列表投影不同）；`session/control` 基线 `value.projections` 是 `Record<sessionId, {asOfSeq, values}>`（同样无 `kind`）。
- `session/create` 成功后前端只在本地插 placeholder（`blank:true`、无投影），真正可打开/可渲染要等 `session/follow` snapshot → M2-b3；`reuseOrCreateBlank` 只在 `session/writer-held` 重试路径用。
- `session/follow` 的 snapshot **必须**带 `projections`（缺失即解码失败，和列表不同）；`kind:'sequenced'` 必须同时有 `asOfSeq` + `values`。
- `session/follow`（流式，参数 `request:{address,assistantStream?,maxMessages?,turnWindow?}`，地址 `{kind:'session',sessionId}` 或 `{kind:'subagent',parentSessionId,childSessionId,mode}`）：打开帧必须回 `{type:'snapshot',header,cursor,records,hasMore,projections,assistantStream}`。因为前端固定传 `assistantStream:true`，**`assistantStream` 字段必需**（`{revision:n,activeAttempt?}`，`revision:0` 表示无进行中尝试），缺失即 `gateway/internal`。空日志页 `cursor` 必须等于 `emptyCursor=-1`（页尾光标与请求光标不一致即 `protocolViolation`）；后续增量帧是 `{type:'event',event}` 或 `{type:'assistant-stream',frame}`。
- 会话内重命名入口是快捷键 `Ctrl+Alt+G`（`session.rename` 命令），需要已打开且非空白会话（`sessions.using(...)`）→ 依赖 M2-b3 的打开会话后才可做 UI 验收。
- 前端对**已经打开的会话**再点不重新开流（`manager` 复用已开的 `SessionEventStream`）；页面刷新会按上次选择还原并自动发一条 `session/follow` → E2E 断言要按 `sessionId` 过滤，不能取第一条。
- 会话事件契约（M2-c 用）：`user/message` data=`UserMessage{id,content:ContentBlock[],source}`；`assistant/message` data=`{turn,step,message,stream,usage?}`；`ContentBlock` 文本 `{type:'text',text}`、`tool-call{type,id,name,arguments}`；`SessionTitleEventData{title,messageSeqs,source}`。
- 消息事件只有带 `surfaceOp`（`'append'`）才被前端收进会话表面（`isSurfaceEvent`：类型属 `{system,developer,user,assistant}/message` + `tool/result`，且 `surfaceOp !== undefined`）；`turn/start`/`step/start`/`step/end`/`turn/end` 提供分组脚手架。
- WS 帧是 JSON（非 ASCII 被转义成 `\uXXXX`）：E2E 断言必须 `JSON.parse` 帧后查字段，禁止直接对帧做中文字符串 `includes`。
- `session/prompt` 是**一元 HTTP**（不是 WS 帧）：`POST /api/session/prompt`，args.request=`{requestId,sessionId,mode:'queue'|'steer',content:[{type:'text',text}|{type:'image',…}|{type:'file',receiptId}],clientTimeZone?}`，成功回 `{accepted:true}`。用户消息事件的 `source.rpcId` 必须等于 `requestId`，前端才撤掉本地待发气泡。
- `session/follow` 打开后的增量帧是 `{type:'item',streamId,value:{type:'event',event}}`（事件项的 value 形状与打开快照的 `records[]` 元素一致），seq 必须接续该会话历史（回放页尾光标）；`{type:'assistant-stream',frame}` 帧 M2-c2 未实现（无逐字输出）。
- `turn/end` 的 `reason` 形状：`{kind:'completed'}` / `{kind:'aborted',reason}` / `{kind:'error',error:{message,code}}`。
- `projections.values.sessionListMetadata.lastPromptAt` 取最后一条 user 条目时间（毫秒），侧栏 `updatedAt = max(summary.updatedAt, lastPromptAt ?? 0)`；`blank` 由转录条目数是否为 0 决定。

## 复现与验证方式

```powershell
# 构建 + 启动（干净 WebView2 配置目录 + 隔离 .jcc，避免污染真实用户配置）
dotnet build modules/apps/gui/JoinCodeGui.csproj -c Debug
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9222'
$env:JCC_APP_DATA_FOLDER="$env:TEMP\opencode\jcc-e2e-home"
$env:JCC_WEBVIEW_DATA_DIR="$env:JCC_APP_DATA_FOLDER\webview2"
$env:JCC_DIRECTORY_PICK="C:\Users\54076\AppData\Local\Temp\opencode\jcc-e2e-ws"   # E2E 注入缝，跳过原生对话框
# 会话 E2E 还需：隔离 home 下预置 sessions/{id}/{transcript.json,meta.json} + 最小 settings.json（vendor.deepseek，不含密钥），否则引擎组装报“未知的 Provider”
Start-Process "artifacts\bin\JoinCodeGui\Debug\net10.0-windows10.0.26100.0\JoinCode.Gui.exe" -ArgumentList '--webui' -WorkingDirectory "D:\project\w3"

# 断言（在 non_deliverables_tools/dsh_parity 下，依赖 playwright-core 与 9222 端口）
node dumps/dsh-webui-verify.mjs                        # 启动契约
node dumps/dsh-webui-ack.mjs                           # 提示页确认闭环
node dumps/dsh-webui-workspace.mjs jcc-e2e-ws          # 选择工作区闭环
node dumps/dsh-webui-workspace.mjs jcc-e2e-ws --no-pick # 重启后基线恢复（先重启 exe 且不带 JCC_DIRECTORY_PICK）
node dumps/dsh-webui-sessions.mjs "E2E引擎会话"          # 侧栏显示引擎已有会话（标题/时间）
node dumps/dsh-webui-create-session.mjs ws-m2b2         # 新建会话落引擎 + 模型目录/投影 + 刷新后读回
node dumps/dsh-webui-open-session.mjs ws-m2b3           # 点侧栏会话开流 + 打开快照（投影/assistantStream/空日志页）
node dumps/dsh-webui-history.mjs ws-m2b3 <工作区路径> <隔离 home>   # 历史渲染：转录→DSH 事件，界面出现历史问答
node dumps/dsh-webui-prompt.mjs ws-m2c2 <隔离 home>    # 发送主链路：输入框回车→用户/助手事件→turn/end→转录落盘（自带本地 OpenAI 桩 stub-openai-server.mjs）
node dumps/dsh-webui-cancel.mjs ws-m2c2 <隔离 home>    # 停止生成：慢速桩中途 session/cancel→turn/end aborted(user)
node dumps/dsh-webui-tool.mjs ws-m2c2 <隔离 home>      # 工具事件：桩返回 tool_calls→tool/call+tool/result→界面工具卡片
node dumps/dsh-webui-reload-trace.mjs                  # 启动/重载全量请求响应
# 单测
dotnet test test/unit/join_code_gui.tests/JoinCodeGui.Tests.csproj -c Debug --filter "FullyQualifiedName~WebHost"
```
