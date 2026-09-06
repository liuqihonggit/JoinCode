# MCP 工具测试计划 06: 任务与视觉 Task + Vision

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 06/12 |
| 涵盖分类 | Task (12) + Vision (13) |
| 工具总数 | 25 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译 |

## 工具清单

### Category: Task (12 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `task_create` | 创建任务 | ⬜ |
| 2 | `task_list` | 列出任务 | ⬜ |
| 3 | `task_update` | 更新任务 | ⬜ |
| 4 | `task_stop` | 停止任务 | ⬜ |
| 5 | `task_get` | 获取任务 | ⬜ |
| 6 | `task_set_dependency` | 设置依赖 | ⬜ |
| 7 | `task_remove_dependency` | 移除依赖 | ⬜ |
| 8 | `task_get_dependencies` | 获取依赖 | ⬜ |
| 9 | `task_can_execute` | 可执行检查 | ⬜ |
| 10 | `task_output` | 任务输出 | ⬜ |
| 11 | `task_stop_batch` | 批量停止 | ⬜ |
| 12 | `task_list_running` | 运行中任务 | ⬜ |

### Category: Vision (13 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `image_describe` | 图像描述 | ⬜ |
| 2 | `image_drill_down` | 图像下钻 | ⬜ |
| 3 | `measure_length` | 测量长度 | ⬜ |
| 4 | `measure_depth` | 测量深度 | ⬜ |
| 5 | `measure_ratio` | 测量比例 | ⬜ |
| 6 | `quadtree_build` | 构建四叉树 | ⬜ |
| 7 | `quadtree_zoom` | 四叉树缩放 | ⬜ |
| 8 | `quadtree_paint` | 四叉树绘制 | ⬜ |
| 9 | `quadtree_render` | 四叉树渲染 | ⬜ |
| 10 | `quadtree_neighbor` | 四叉树邻居 | ⬜ |
| 11 | `screen_indicate` | 屏幕指示 | ⬜ |
| 12 | `temporal_aggregate` | 时序聚合 | ⬜ |
| 13 | `temporal_stable_contour` | 时序稳定轮廓 | ⬜ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$taskTools = @(
    "task_create", "task_list", "task_update", "task_stop", "task_get",
    "task_set_dependency", "task_remove_dependency", "task_get_dependencies",
    "task_can_execute", "task_output", "task_stop_batch", "task_list_running"
)
$visionTools = @(
    "image_describe", "image_drill_down", "measure_length", "measure_depth",
    "measure_ratio", "quadtree_build", "quadtree_zoom", "quadtree_paint",
    "quadtree_render", "quadtree_neighbor", "screen_indicate",
    "temporal_aggregate", "temporal_stable_contour"
)
foreach ($t in $taskTools + $visionTools) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
& $jcc --trust --bypass mcp_call task_list
& $jcc --trust --bypass mcp_call task_list_running
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] `task_set_dependency` / `task_remove_dependency` 互为逆操作
- [ ] `task_stop_batch` 可处理空列表(不崩溃)
- [ ] Vision 工具无图像时给出明确提示

## 风险提示

- `task_stop` / `task_stop_batch` 终止运行中任务,注意副作用
- Vision 工具依赖图像输入,无图像时需优雅降级
- `quadtree_build` 可能消耗内存,注意大图像

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| | | | |

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
