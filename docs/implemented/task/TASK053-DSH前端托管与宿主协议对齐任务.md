# TASK053 — DSH 前端托管与宿主协议对齐

> ADR：[0143](../adr/architecture/0143-dsh-web-frontend-hosting.md)
> 目标：GUI 直接托管 DSH 前端产物，C# 侧实现 DSH 假定的「宿主半边」协议，随后把数据源逐方法替换为自己引擎。

## 里程碑

| 阶段 | 内容 | 状态 |
|------|------|------|
| M1 | 前端资产打包嵌入 + 回环 HTTP/WS 宿主 + 13 个启动 API 骨架 + 启动注入 | ✅ |
| M1.5 | `settings/describe` / `settings/mutate`（含 schema 信封与 revision）修「预览版说明」确认不关闭 | ✅ |
| M2 | 会话/消息/流式/权限接自有引擎（含 `directoryPicker/pick`、`workspace/*`、`open-in-app/apps`、`plugins/events`） | ⬜ |
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
| `directoryPicker/pick`（原生文件夹选择） | 「选择工作区」按钮 | ❌ | ❌ 未实现（fail-closed：返回 `unimplemented` + 引导文案），M2 补 |
| `workspace/initializeDefault` → `null`（宿主无默认工作区） | 启动无工作区分支 | ✅ | ✅ 重载抓包确认不再自动弹选择器、无错误横幅 |
| 会话/消息/流式/权限（`session/*` 实数据、`session/follow` 增量） | 聊天主链路 | ⬜ | ⬜ M2 |

## M1.5 缺陷记录（缺陷驱动）

1. 现象：首个「预览版说明」提示页点「继续/确定」不关闭；出现「无法保存确认状态」。
2. 红测（实现前，真实 exe）：`dumps/dsh-webui-ack.mjs` → `FAIL 点击后提示页关闭`、`FAIL 无「无法保存确认状态」报错`。
3. 根因链：
   - M1 的 `settings/describe` 返回 `namespaces:[]` → 前端 `configForms` 找不到该命名空间，写路径打到未实现的 `settings/mutate` → 写失败 → 报错。
   - 仅补 `mutate` 仍不够：视图里 `schema:null` → 前端 `decode()` 无法还原 schemastery 节点 → 命名空间停在 `loading` → 提示页干脆不渲染。
4. 修复：`DshSettingsStore` 落库 + `schema` 回传真实信封 + `autoGenerate=false` + revision/冲突；`workspace/initializeDefault` 回 `null` 消除启动错误路径。
5. 绿测：单测 7/7；E2E ALL_PASS；重载抓包显示 `value.welcomeNoticeVersion` 已由宿主返回（证明是宿主落库，不是前端本地关闭）。

## 已确认的协议事实（供 M2 使用）

- 启动期 13 个一元方法：`agentPresets/list`、`commands/list`、`credentials/describe`、`dynamicCordisRunner/inventory`、`dynamicCordisRunner/syncInspectManifest`、`llm/listConfigurableProviders`、`llm/listProviders`、`permissionPresets/catalog`、`session/create`、`session/list`、`session/modelCatalog`、`settings/describe`、`skills/list`。
- 运行期额外：`workspace/initializeDefault`（result `undefined | {workspace:{workspaceId,path,title,sessionIds,createdAt,updatedAt}}`）、`directoryPicker/pick`（result `null | string`）、`settings/mutate`（result = 命名空间视图）、静态路径 `open-in-app/apps`、`plugins/events`。
- `settings/mutate` 请求：`{"ns":"ui-settings-general","ops":[{"op":"set","path":["welcomeNoticeVersion"],"value":"2026-09-28.1"}]}`，可带 `expectedRevision`；`set` 操作必须携带 `value`。

## 复现与验证方式

```powershell
# 构建 + 启动（干净 WebView2 配置目录）
dotnet build modules/apps/gui/JoinCodeGui.csproj -c Debug
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9222'
$env:JCC_WEBVIEW_DATA_DIR="$env:TEMP\opencode\jcc-gui-e2e-ack2"
Start-Process "artifacts\bin\JoinCodeGui\Debug\net10.0-windows10.0.26100.0\JoinCode.Gui.exe" -ArgumentList '--webui' -WorkingDirectory "D:\project\w3"

# 断言（在 non_deliverables_tools/dsh_parity 下，依赖 playwright-core 与 9222 端口）
node dumps/dsh-webui-verify.mjs        # 启动契约
node dumps/dsh-webui-ack.mjs           # 提示页确认闭环
node dumps/dsh-webui-reload-trace.mjs  # 启动/重载全量请求响应
```
