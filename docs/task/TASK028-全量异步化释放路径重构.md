# TASK028 — 全量异步化释放路径重构

## 背景

PR #258 修复了 `using var host` 持有 IAsyncDisposable 的死锁(定义 IAsyncHost 接口)。现需将此模式推广到整个项目,消除所有同步释放路径,实现全量异步化。

## 根因

`using var` 调同步 `Dispose()`,持有异步资源(IAsyncDisposable)时阻塞线程池 → Consumer 死锁。项目现有 **329 处** `using var` 持有双接口类型(IDisposable+IAsyncDisposable),分析器 JCC9104/JCC9107 对双接口类型有盲区(豁免),未拦截。

## 范围

### 纳入(P0+P1)
- **P0**:增强分析器 JCC9104/JCC9107 覆盖双接口类型 + 329 处 `using var`→`await using var`(豁免 CTS/MemoryStream) + 3 处显式 `.Dispose()`→`.DisposeAsync()`
- **P1**:77 处同步阻塞(`.GetAwaiter().GetResult()` 58 + `.Wait()` 19)→`await`

### 豁免(白名单)
| 类型 | 数量 | 豁免原因 |
|------|------|---------|
| `CancellationTokenSource` | 141 | DisposeAsync 内部是同步完成,无内核句柄,改了低价值 |
| `MemoryStream` | 29 | 纯内存无内核句柄,同步 Dispose 无害 |

### 不纳入(后续)
- `.Result` 439 处(P2,生产代码约50-80处,由 JCC3006 驱动)
- 测试代码 `using var` 1142 处(P3,批量)
- `try-finally`+Dispose 4 处(P3,全在测试)

## 工作量估计

| 项 | 处数 | 文件数 |
|----|------|--------|
| 双接口 using var(扣除白名单) | 159 | ~120 |
| 显式 Dispose→DisposeAsync | 3 | 2 |
| .GetAwaiter().GetResult()→await | 58 | 35 |
| .Wait()→await | 19 | 10 |
| **合计** | **239** | **~150** |

## 推进顺序

| 步骤 | 内容 | 状态 |
|------|------|------|
| 0 | 增强分析器 JCC9104/JCC9107 覆盖双接口类型 + 白名单(CTS/MemoryStream) | 🔄 |
| 1 | `lib\infrastructure\io\` using var→await using var(IO层重灾区,31+37+35=103处) | ⏳ |
| 2 | `server\bridge\client\` using var→await using var(网络客户端,38处) | ⏳ |
| 3 | `llm\agents\` + `kit\slash\` + `kit\mcp\` using var→await using var(Actor/Transport) | ⏳ |
| 4 | `app\gui\` + `app\cli\` + `app\tui\` using var→await using var(入口层) | ⏳ |
| 5 | `kit\hands\` + `kit\brain\` using var→await using var(工具执行器) | ⏳ |
| 6 | `.GetAwaiter().GetResult()`/`.Wait()`→await(同步阻塞清理) | ⏳ |
| 7 | 测试代码 using var→await using var(批量,低优先) | ⏳ |

## 编译策略

增强分析器后全量编译会失败(约159处 Error),这是大型重构的预期中间状态。逐模块修复,每修完一个模块用 `dotnet build <csproj>` 验证该模块编译通过,最后全量编译通过。

## 验收标准

- [ ] 全量编译 0 警告 0 错误
- [ ] JCC9104/JCC9107 规则覆盖双接口类型(CTS/MemoryStream 白名单豁免)
- [ ] 生产代码 0 处 `using var` 持有双接口类型(扣除白名单)
- [ ] 生产代码 0 处 `.GetAwaiter().GetResult()`/`.Wait()`(主入口豁免)
- [ ] 分析器测试全通过

## 决策记录

<!-- 🤖 Auto Decision: 2026-09-19 -->
<!-- 决策: CTS/MemoryStream 白名单豁免 -->
<!-- 原因: CTS.DisposeAsync 内部是同步完成无内核句柄(141处改了低价值);MemoryStream 纯内存无句柄(29处) -->
<!-- 替代方案: 全量包含CTS(风格统一但170处低价值改动) -->
<!-- 验证: 待编译验证 -->
