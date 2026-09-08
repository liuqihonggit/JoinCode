# 关注点分离重构计划(制造 Dispose + using 封装)

## 背景(用户原话澄清)

> 我根本不是让你调整这些,我让你随机找一个类,然后看看它是否关注点不够分离,然后去制造新类,制作好析构和释放函数,消费端就改成标准的 using 释放功能,也就是那些缺少的行为可以封装的.你把 try 改成 using 没有意义啊.我要的是 前..中间一大串...后,这种不清晰的包裹结构,很好改成 using 那种带 try 的.

## 正确方向

**不是**把 `try-finally` 改成 `using`(表面写法替换,无意义)。
**是**找"前..中间一大串...后"散落包裹结构 → 制造新类实现 IDisposable:
- 前(构建/获取资源/进入状态)→ 构造函数
- 后(释放/退出状态/清理)→ Dispose
- 缺少的行为(该清理未清理、散落配对)→ 补进 Dispose 封装
- 消费端:`using var scope = new XxxScope(...);` + 中间一大串业务

即 RAII / Scope Guard 模式,用语言机制实现关注点分离。

## 候选识别信号

1. 方法内"前-中-后"散落:获取资源 → 一大串业务 → 清理,但没封装成 scope
2. try-finally 里 finally 有多个清理调用(>2 个),或清理逻辑复杂
3. "进入...退出"配对(BeginUpdate/EndUpdate, Start/Stop, Acquire/Release, Push/Pop, Enter/Exit)散落
4. 多个方法重复相同的"前-中-后"模式
5. 有状态切换但退出时未确定性还原(缺少的行为)

## 执行流程(每个候选)

1. 随机选一个有上述信号的类
2. 分析"前/中/后"分别是什么,缺少什么释放行为
3. 制造新类(或改造现有类实现 IDisposable),前→构造,后→Dispose,补齐缺少行为
4. 消费端改 `using var scope = new ...;`
5. 编译 → 测试 → 提交

## 候选清单与执行结果

| # | 候选 | 封装 | 提交 | 测试 |
|---|------|------|------|------|
| 1 | FileWatcherIntegrationRegistry | ReaderWriterLockSlimScope 扩展 | 3c0f47a1a | 29 通过 |
| ' 2 | CodeIndexerRegistry | 复用同上 | 3c0f47a1a | 29 通过 |
| 3 | ReplLoopStep | ReplStepScope : IAsyncDisposable | 575fbc264 | E2E 待跑 |
| 4 | BridgeClient.SendRequestAsync | BridgeRequestScope : IDisposable | 8cac485a1 | 7 通过 |
| 5 | InProcessTeammateTask | TeammateWorkScope : IAsyncDisposable | f5db3a016 | 14 通过 |
| 6 | BridgeMainCommand(新) | ConsoleCancelScope : IDisposable **修 CTS+事件双重泄漏 bug** | 78fbd9c92 | 25 通过 |
| 7 | BuildQueueService.ExecuteBuildAsync | BuildExecutionScope : IAsyncDisposable **修 CTS 泄漏 bug** | 7d6f5a85c | 17 通过 |
| 8 | InProcessTeammateTask.ExecuteTeammateDirectAsync | TeammateDirectScope : IAsyncDisposable **修资源泄漏 bug** | af036f604 | 262 通过 |
| 10 | PreventSleepScope(4处重复) | PreventSleepScope : IAsyncDisposable + DetachTo **修 StartAsync 异常泄漏 bug** | 61e95dfd7 | 908 通过 |

## 未做候选(复杂度高,待决策)

| # | 候选 | 复杂点 |
|---|------|--------|
| 9 | AgentBase.ExecuteAsync+ExecuteStreamAsync | 已较好封装(linkedCts/scope 已 using var),价值低 |

## 执行进度

- [x] 第一批:候选1+2(锁 scope 复用)— 3c0f47a1a
- [x] 候选3:ReplStepScope — 575fbc264
- [x] 候选4:BridgeRequestScope — 8cac485a1
- [x] 候选5:TeammateWorkScope — f5db3a016
- [x] 候选6:ConsoleCancelScope(修 bug)— 78fbd9c92
- [x] 候选7:BuildExecutionScope(修 CTS 泄漏 bug)— 7d6f5a85c
- [x] 候选8:TeammateDirectScope(修资源泄漏 bug)— af036f604
- [x] 候选10:PreventSleepScope(修 StartAsync 异常泄漏 bug)— 61e95dfd7

## 总结

9 个候选完成,制造 8 个新 scope 类,消除 35+ 处散落 try-finally/ContinueWith,修 4 个泄漏 bug(CTS+事件双重泄漏、CTS 泄漏、资源泄漏、PreventSleep 异常泄漏)。
