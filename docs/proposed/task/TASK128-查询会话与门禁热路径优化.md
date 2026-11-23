# TASK128 查询、会话与门禁热路径优化

## 本轮切片

- [x] 查询：用现有异步消息原语替代 `QueryEngine.QueryAsync` 的空队列轮询；不改流事件 DTO、顺序和管道业务。
- [x] 会话：先按原顺序和修改时间选最近 N 项，再打开正文；JSONL 逐行统计、保留旧 JSON、取消以及损坏文件行为。
- [x] 门禁：注册顺序校验只读源码一遍，不改变漏登记、错目录或硬编码位次诊断。
- [x] 记录实测与未验项；后续独立切片：CI 制品缓存输入/输出闭合、SSE 快照、TUI 首帧及布局、GraphRAG、Pithos 范围扫描。

## 验收标准

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| 查询异步出队 | 主聊天 / 主代理 `QueryAsync` | ✅ | ✅ `QueryEngineOutputStreamTests` 3/3：空输出不投递空轮询、首块、顺序、末尾、异常和提前停止；进程级 exe 未跑 |
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

- 全量构建、packed NuGet 消费和 PR CI 交由流水线验证。
- Query 进程级 exe 未跑；注册顺序脚本仅验证当前工作树，未对新旧脚本做逐字节 diff。
- `ExpiringValue` 的 `Stopwatch` 时间戳与 `TimeSpan.Ticks` 频率隐患为原有独立待办，本轮未改。
