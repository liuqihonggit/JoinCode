# dsh_parity — DSH 像素对齐参考物捕获工具

> 非交付品工具（non_deliverables_tools）。用途：把 DSH（DeepSeek Harness）Web UI 捕获为
> **可机读参考物**（截图 + DOM 盒树 + 设计 token），供本项目 UI 做像素级对齐校验。
>
> 设计文档: [DSG042](../../modules/apps/docs/design/DSG042-DSH像素级对齐对比校验体系.md)
> 任务清单: [TASK053](../../modules/apps/docs/task/TASK053-DSH像素对齐对比校验体系任务清单.md)

## 产出物（写入 `reference/`）

| 文件 | 内容 |
|------|------|
| `dsh-web-{W}x{H}.png` | 固定视口截图（DPR=1，深色主题） |
| `dsh-dom-{W}x{H}.json` | 可见元素盒树：box/背景色/字号/圆角/padding/gap/稳定路径 |
| `dsh-tokens.json` | 全部 `--dsw-*`/`--ds-*` CSS 变量，双主题解析值 |
| `dsh-meta.json` | 版本锚定（dsh 版本/视口/DPR/时间） |

## 运行

前置：Node ≥ 20、系统 Edge（`msedge.exe`，可用环境变量 `DSH_PARITY_EDGE` 覆盖路径）。

```powershell
# 首次会自动 npm install（锁定版本见 package.json）
powershell -NoProfile -ExecutionPolicy Bypass -File capture/capture-all.ps1

# 自定义视口
powershell -NoProfile -ExecutionPolicy Bypass -File capture/capture-all.ps1 -Viewport 1024x768
```

## 安全/隔离约定

- `DSH_HOME` 指向工具目录下 `.dsh-home/`（gitignored），不污染用户 `~/.dsh`
- `dsh web --no-open`，不会打开用户浏览器
- 结束按 PID 击杀 node 进程 + 兜底按命令行归属清扫，无残留
- token 不出日志（URL 中 token 以 REDACTED 写 meta）

## 踩坑记录

| 坑 | 根因 | 解法 |
|----|------|------|
| `msedge --headless --screenshot --virtual-time-budget` 永久挂起 | DSH UI 保持 WebSocket 长连接，虚拟时间永不推进 | 弃 CLI 截图，playwright-core CDP 主动 `page.screenshot()` |
| ESM 找不到 playwright-core | 脚本位置不在 node_modules 解析路径 | 脚本放工具目录内运行（node 从脚本位置向上解析） |
| 启动器被回收遗留 node 进程 | 沙箱子进程管理差异 | 单次调用内完成启动→捕获→击杀；CommandLine 归属兜底 |
