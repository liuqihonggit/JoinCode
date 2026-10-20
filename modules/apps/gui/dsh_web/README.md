# dsh_web — 第三方前端资产（DeepSeek Harness Web UI）

本目录存放**非本仓库编写**的第三方前端产物，由 JoinCode GUI 直接托管（ADR 0143）。

## 内容

| 文件 | 说明 |
|------|------|
| `dsh_web.zip` | 依 MIT 许可再分发的 `@deepseek-ai/dsh-web-frontend` 构建产物快照（278 文件 / 约 7 MB） |
| `LICENSE` | 上游 MIT 许可原文（`@deepseek-ai/dsh-web-frontend` 包内 LICENSE） |

## 来源与版本

- 上游包：`@deepseek-ai/dsh-web-frontend@0.2.0-rc.2`
- 快照内容：`dist/` 全部静态资产 + 运行期按需拉取的 `@deepseek-ai/dsh-client-ui-*` 插件 `lib/client.js`（combo URL 形态 `plugins/??a,b&rev=X`）
- 打包脚本：`non_deliverables_tools/dsh_parity/capture/package_dsh_web.py`
- 快照来源：`non_deliverables_tools/dsh_parity/dumps/dsh-web-snapshot/`（由 `capture/vendor-dsh-web.mjs` 抓取真实 DSH 服务端产物）

## 剔除项（按需 chunk，暂未分发）

`client.pdf.js`、`client.excel.js`、`client.terminal.js` 三个按需大 chunk 未打包；触发对应功能时宿主返回 404，界面会给出加载失败提示。

## 运行时行为

首次启动时 `DshWebAssets` 将本 zip 解包到 `{JccDirectory}/dsh_web/0.2.0-rc.2/`，以 `meta.json` 作为解包完成标记；后续启动直接复用。宿主（`DshWebHost`）在该目录上提供静态文件、combo 拼接与 `/api` 协议。

## 许可

上游为 MIT（见 `LICENSE`）。再分发需保留版权与许可声明；本目录中的 zip 为其未修改副本。
