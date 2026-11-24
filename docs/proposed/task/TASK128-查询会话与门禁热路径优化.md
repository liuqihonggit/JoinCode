# TASK128 查询、会话与门禁热路径优化

## 本轮切片

- [x] 查询：用现有异步消息原语替代 `QueryEngine.QueryAsync` 的空队列轮询；不改流事件 DTO、顺序和管道业务。
- [x] 会话：先按原顺序和修改时间选最近 N 项，再打开正文；JSONL 逐行统计、保留旧 JSON、取消以及损坏文件行为。
- [x] 门禁：注册顺序校验只读源码一遍，不改变漏登记、错目录或硬编码位次诊断。
- [x] 记录实测与未验项；后续独立切片：CI 制品缓存输入/输出闭合、SSE 快照、TUI 首帧及布局、GraphRAG、Pithos 范围扫描。

## 验收标准

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| 查询异步出队 | 主聊天 / 主代理 `QueryAsync` | ✅ | ✅ `QueryEngineOutputStreamTests` 3/3：空输出不投递空轮询、首块、顺序、末尾、异常和提前停止；进程级 exe 已跑（2026-11-24 MockServer harness 4/4，见未验项） |
| 按元数据选 Top N | CLI 恢复 / TUI 历史加载 | ✅ | ✅ `TranscriptServiceTests` 30/30：排序、格式、取消和预览契约保持；仅选中会话各打开一次正文 |
| 注册顺序单次扫描 | CI Registration order table | ✅ | ✅ `Verify-RegistrationOrder.py` 5 列表 / 17 项通过；`os.walk` 单次读取且诊断语义不变；新旧脚本未做逐字节 diff |

## 本轮实测

- `dotnet build modules/composition/composition/Composition.csproj`：0 警告 / 0 错误。
- `QueryEngineOutputStreamTests`：3/3 通过。
- `TranscriptServiceTests`：30/30 通过。
- `ServiceRegistrationTest`：35/35 通过（含 Cache 6 例）。
- 缓存工具定向测试：20/20 通过。
- `ArchitectureGate -Mode Validate`：PASS，项目 263 / 边 1206 / 生产跨域 477。
- `Verify-RegistrationOrder.py`、`Verify-FailFastConsumers.py`、`Verify-NodeBridgeCapability.py`、`Verify-NoLockPrimitives.py`、`Verify-ExplicitOrder.py`：全部 exit 0。
- `git diff --check`：通过。

## 未验项

- PR #728 / #730 CI 已全绿（38 pass + 1 条件 skip）；build job 已完成 packed NuGet 打包并由 test job 消费。
- Query 进程级 exe 已于 2026-11-24 补跑（MockServer harness，`JCC_ALLOW_DIRECT_FALLBACK=1`）：
  Streaming 超时验收（`NonInteractiveMode_ShouldCompleteWithoutTimeout`）、Vd02 异常路径、
  Ve06 多轮、Wq01 限流（含 bridge_serve 双回合流式事件 + 回合异常 → error 事件）4/4 通过，
  证据 `.xxx/logs/p1-1-e2e-evidence.txt`；单元测试覆盖的空等/顺序/异常/取消语义在真实 exe 上表现一致；
  注册顺序新旧脚本输出已逐字节一致（`.xxx/logs/registration-{old,new}.txt`）。
- `ExpiringValue` 的时间单位缺陷已修复：改用 `TimeProvider.GetElapsedTime` + `_hasValue` 消除时间戳单位混用与零哨兵问题；秒级时钟红测试和 `ExpiringValueTests` 4/4 通过，`CpuParallelismTest` 回归通过。
