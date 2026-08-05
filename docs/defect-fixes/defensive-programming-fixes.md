# 防御性编程缺陷修复任务清单

> 创建时间: 2026-08-06
> 完成时间: 2026-08-06
> 范围: 10 个高严重度 + 10 个中严重度缺陷，严格 TDD（先复现再修复）

## 高严重度缺陷（10/10 全部完成）

| # | 文件:行 | 缺陷类型 | 问题 | 修复策略 | 状态 | commit |
|---|---------|---------|------|---------|------|--------|
| 1 | BridgeSessionTracker.cs:9-18 | 纵深防御 | 11个集合无并发保护 | ConcurrentDictionary + ConcurrentDictionary<string,byte> | ✅ | 8a2776e94 |
| 2 | SystemActuatorRegistry.cs:291 | 多级报错 | GetAwaiter().GetResult() 同步阻塞 | 全异步化 + try-catch 部分成功 | ✅ | ba21c7882 |
| 3 | QueryLoopMiddleware.cs:293 | 多级报错 | 迭代器中同步阻塞异步 | 拆 BuildPureTextResponse 返回事件列表 | ✅ | 3b98bbb27 |
| 4 | McpServer.cs:5-7 | 纵深防御 | 3个字典无并发保护 | ConcurrentDictionary | ✅ | c1728521f |
| 5 | LspClient.cs:227 | 纵深防御 | Task.Run 传 CancellationToken.None | 传 readToken + try-catch 观察异常 | ✅ | 378db27b0 |
| 6 | WorkflowTask.cs:172-191 | 任务断裂 | 顺序执行无 checkpoint | checkpoint 日志 | ✅ | 9e3da3e13 |
| 7 | WorkflowTask.cs:200-213 | 任务断裂 | 并行执行无部分成功 | ExecuteStepWithFailureHandlingAsync 异常隔离 | ✅ | 9e3da3e13 |
| 8 | SystemActuatorCommandContext.cs:323 | 多级报错 | KillProcessTree 兜底失败 | static改实例 + TryKillSafely + 分级日志 | ✅ | 6a1028d1a |
| 9 | McpStdioClient.cs:92 | 纵深防御 | 进程 Start 异常未 Dispose | try-catch+Dispose + HasExited 安全检查 | ✅ | 5972a5396 |
| 10 | PluginManager.cs:171 | 纵深防御 | 进程 Start 异常未 Dispose | try-catch+Dispose | ✅ | 5972a5396 |

## 中严重度缺陷（10/10 全部完成）

| # | 文件:行 | 缺陷类型 | 问题 | 修复策略 | 状态 | commit |
|---|---------|---------|------|---------|------|--------|
| 11 | EnvironmentSection.cs:101 | 多级报错 | GetAwaiter().GetResult() 同步阻塞 | Task.Run 包裹 | ✅ | 8e1398fa2 |
| 12 | IdeIntegrationService.cs:286 | 多级报错 | GetAwaiter().GetResult() 同步阻塞 | Task.Run 包裹 | ✅ | 8e1398fa2 |
| 13 | ChromeIntegrationService.cs:32,58 | 多级报错 | GetAwaiter().GetResult() 同步阻塞 | Task.Run 包裹 | ✅ | 8e1398fa2 |
| 14 | DesktopHandoffService.cs:24 | 多级报错 | GetAwaiter().GetResult() 同步阻塞 | Task.Run 包裹 | ✅ | 8e1398fa2 |
| 15 | SystemActuatorCommandContext.cs:523 | 任务断裂 | 大输出持久化失败无 fallback | LogDebug→LogWarning + 重试一次 + LogError | ✅ | 73ccffba8 |
| 16 | RemoteClientManager | 纵深防御 | fire-and-forget 异常吞掉 | 已有 try-catch log，无需修改 | ✅ | - |
| 17 | BridgeServer.cs:168 | 纵深防御 | fire-and-forget 请求处理 | Task.Run + try-catch 记录异常 | ✅ | 6d1d02d77 |
| 18 | CronScheduler.cs:154 | 任务断裂 | 定时任务无 checkpoint | MarkTasksFiredAsync 移到 finally + try-catch | ✅ | 48a69064a |
| 19 | InProcessTeammateTask.cs:460 | 任务断裂 | 流式无续传 | 每轮 checkpoint 日志（真正续传需持久化存储，架构改动暂缓） | ✅ | 9290fc96e |
| 20 | BuildQueueService.cs:370 | 任务断裂 | 队列无 checkpoint | build 开始/完成 checkpoint 日志（真正持久化属架构改动暂缓） | ✅ | c7eae76b7 |

## TDD 流程（每个缺陷）

1. 🔴 写失败测试复现缺陷
2. 🟢 修复代码使测试通过
3. 编译验证
4. git 提交

## 备注

- 缺陷19、20 的真正续传/持久化 checkpoint 需要架构改动（引入持久化存储层），当前做渐进式日志 checkpoint，便于崩溃诊断
- 缺陷16 经检查已有 try-catch log 保护，无需修改

## 进度记录

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 严格按 TDD 铁律2 逐个修复，每个缺陷独立提交 -->
<!-- 原因: 渐进式开发，每步可验证可回滚 -->
<!-- 替代方案: 批量修复后统一测试（风险高，放弃）-->
<!-- 验证: 全部20个缺陷编译通过 + 单元测试通过 ✅ -->
