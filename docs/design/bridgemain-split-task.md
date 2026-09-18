# BridgeMain 拆分任务

## 目标
将 `server/bridge/session/main/core/BridgeMain.cs`（1769行）拆分为单一职责独立类型，非 partial class。

## 方案
共享上下文对象 `BridgeMainContext` + 6 个职责类，构造注入 Context。

## 职责类设计

| 类名 | 方法 | 依赖字段 |
|------|------|---------|
| BridgeShutdownHandler | ShutdownAsync, ShutdownViaPipelineAsync, ShutdownDirectAsync | _isShuttingDown, _shutdownPipeline, _deps, _tracker, _pointerManager, _loopCts, _loopTask, _isResuming, _fatalExit, EnvironmentId, _logger |
| BridgeWorkHandler | HandleWorkAsync, HandleWorkViaPipelineAsync, HandleWorkDirectAsync | _handleWorkPipeline, _deps, _tracker, _workApi, _pointerManager, _tokenRefresh, EnvironmentId, _logger, _networkService |
| BridgeLoopRunner | RunBridgeLoopAsync, HandleNoWorkAsync, RunAtCapacityHeartbeatAsync | _deps, _logger, _clock, _backoff, _tracker, _loopStartTime, _fatalExit, EnvironmentId |
| BridgeSessionMonitor | MonitorSessionCompletionAsync, MonitorSessionTimeoutAsync, CleanupAllSessionsAsync | _deps, _logger, _tracker, _clock, _loopCts, _workApi, _tokenRefresh, _isResuming, EnvironmentId, _pendingCleanups, _cleanupLock |
| BridgeRunOrchestrator | RunBridgeFromContextAsync, RunDirectAsync, RunHeadlessAsync | _deps, _logger, _tracker, _pointerManager, _tokenRefresh, _loopCts, _loopTask, _isResuming, EnvironmentId |
| BridgeEnvironmentRegistrar | RegisterEnvironmentAsync, TryCreateInitialSessionAsync, HandleRegistrationError | _deps, EnvironmentId, EnvironmentSecret, _logger |

## 执行顺序（渐进式）
1. [ ] 创建 BridgeMainContext（共享状态容器）
2. [ ] BridgeShutdownHandler（最简单，3方法~135行）
3. [ ] BridgeEnvironmentRegistrar（3方法~90行）
4. [ ] BridgeLoopRunner（3方法~210行）
5. [ ] BridgeWorkHandler（3方法~390行）
6. [ ] BridgeSessionMonitor（3方法~220行）
7. [ ] BridgeRunOrchestrator（3方法~520行）
8. [ ] 清理 BridgeMain 主文件，委托给各职责类

每步：编译 → 测试 → commit

## 进度
- 2026-09-18: 任务创建，方案确认（共享上下文对象）
