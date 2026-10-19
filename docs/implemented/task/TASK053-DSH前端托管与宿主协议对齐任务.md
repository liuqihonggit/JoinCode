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
| M2-b2 | `session/create` 落引擎 + `session/control` 基线投影 + `session/modelCatalog` 真实模型目录（消除「正在加载模型…」） | ⬜ |
| M2-b3 | 打开会话：`session/follow` snapshot（含 `projections`）+ 会话投影载入 | ⬜ |
| M2-c | 聊天主链路：消息发送 + `session/follow` 增量事件流（`assistant/message`、`tool/call|result`、`step/*`…） | ⬜ |
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
| 会话/消息/流式/权限（`session/create|control|follow`、`session/follow` 增量、`approval/*`） | 聊天主链路 | ⬜ | ⬜ M2-b2/b3/c/d |

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
- `projections.values.modelSelection` 缺失会让模型选择停在「正在加载模型…」→ M2-b2 与 `session/modelCatalog` 一起补。
- `session/follow` 的 snapshot **必须**带 `projections`（缺失即解码失败，和列表不同）；`kind:'sequenced'` 必须同时有 `asOfSeq` + `values`。
- 会话内重命名入口是快捷键 `Ctrl+Alt+G`（`session.rename` 命令），需要已打开且非空白会话（`sessions.using(...)`）→ 依赖 M2-b3 的打开会话后才可做 UI 验收。
- 会话事件契约（M2-c 用）：`user/message` data=`UserMessage{id,content:ContentBlock[],source}`；`assistant/message` data=`{turn,step,message,stream,usage?}`；`ContentBlock` 文本 `{type:'text',text}`、`tool-call{type,id,name,arguments}`；`SessionTitleEventData{title,messageSeqs,source}`。

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
node dumps/dsh-webui-reload-trace.mjs                  # 启动/重载全量请求响应
# 单测
dotnet test test/unit/join_code_gui.tests/JoinCodeGui.Tests.csproj -c Debug --filter "FullyQualifiedName~WebHost"
```
