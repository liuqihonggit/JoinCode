namespace Core.Prompts.Sections;

/// <summary>
/// 死锁与并发审计规则的提示词部分；关键词触发注入。
/// </summary>
[PromptSection(Name = "deadlock_audit",
    Keywords = new[] { "死锁", "卡死", "卡住了", "竞态条件", "分片锁", "锁排序", "并发安全", "flaky test", "线程安全", "互斥锁", "阻塞", "deadlock", "thread safety", "race condition", "concurrent" },
    InjectOn = PromptSectionInject.Keyword)]
public static class DeadlockAuditSection {
    /// <summary>
    /// 获取 deadlock_audit 部分内容；死锁与并发审计规则文本。
    /// </summary>
    public static string GetContent() {
        return """
# 死锁与并发审计规则

当用户提到"死锁"、"卡死"、"卡住了"、"竞态条件"、"分片锁"等关键词时，你必须执行以下审计：

## 无锁并发检查清单

本项目并发红线：禁止新增 lock/Monitor/SemaphoreSlim/Semaphore/Mutex/ReaderWriterLockSlim/SpinLock/事件等待句柄，以及 AsyncLock/LockRef/AsyncLockedDictionary/LockRegistry/TimeoutLock/OrderedLockManager（JCC4008）。并发通过业务状态所有权规约，而不是靠锁：

1. 先找共享可变状态，明确唯一写者；跨线程写状态必须收口到 Actor 单写者邮箱（Channel&lt;T&gt; + 单消费者）
2. 读多写少用不可变快照 + Interlocked.CompareExchange 发布，读方只读快照
3. 生产者/消费者与背压用 Channel&lt;T&gt;，禁止用信号量模拟队列
4. 单飞初始化用 TaskCompletionSource + CAS，禁止用锁保护 check-then-act
5. 需要串行化的是"消息处理顺序"，不是"代码临界区"；把操作改写成消息投递给所有者
6. 跨进程互斥用 FileMode.CreateNew 原子文件租约 + TTL 心跳，禁止命名 Mutex
7. UI 线程亲和（WPF/Avalonia 控件）用 Dispatcher 调度，不用锁保护 UI 状态
8. 存量锁按 TASK099 批次收敛；新增锁直接视为缺陷，必须改为 Actor/Channel/CAS/租约

## 竞态条件测试

对于疑似 flaky test（竞态条件测试），必须多次运行确认：

```
for ($i = 0; $i -lt 5; $i++) {
    dotnet test --filter "TestName" -c Release --no-build
}
```

如果5次运行中有通过也有失败——确认是 flaky test。

## 根因定位

1. 获取完整堆栈跟踪来定位根因
2. 检查共享状态是否有唯一写者；没有唯一写者就是竞态根因，不是"锁粒度不够"
3. 检查 check-then-act 是否用 CAS 或单写者消息替代
4. SQLite 单连接并发用 WAL + 单写者连接，禁止加进程内锁
5. 检查是否存在无限等待（无超时的 WaitOne/WaitAsync/Task.Delay 轮询）
6. 检查是否有 .Result/.Wait() 阻塞异步操作
7. 检查 Channel 是否消费者停止消费、是否有界容量阻塞生产者

## 无锁改造决策树

1. 状态是否可收口到一个所有者？→ Actor 单写者邮箱（每 key 一个 actor 或单全局 actor）
2. 只读为主的快照？→ Immutable 结构 + Interlocked.CompareExchange 发布新快照
3. 生产/消费速率不匹配？→ 有界 Channel&lt;T&gt; + 显式背压/丢弃策略
4. 同一操作并发触发只允许一次？→ TaskCompletionSource + CAS 单飞
5. 跨进程竞争？→ FileMode.CreateNew 原子文件租约 + TTL 心跳 + 原子 rename 接管
6. 需要按键并行又保序？→ 按 key 哈希路由到独立 actor，actor 之间不共享可变状态

## 可用MCP工具

- `code_index_search`: 搜索锁相关符号（SemaphoreSlim/lock/Monitor/AsyncLock 等存量）
- `code_index_find_references`: 查找锁的所有使用位置
- `code_index_get_callers` / `code_index_get_callees`: 分析锁的调用链
- `find_bugs`: 查找并发相关的潜在错误
- `security_audit`: 安全审计（含并发安全检查）
""";
    }

    /// <summary>
    /// 创建 deadlock_audit 提示词部分。
    /// </summary>
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("deadlock_audit", GetContent);
    }
}