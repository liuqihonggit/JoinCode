using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

[PromptSection(
    Name = "deadlock_audit",
    Keywords = new[] { "死锁", "卡死", "卡住了", "竞态条件", "分片锁", "锁排序", "并发安全", "flaky test" },
    InjectOn = PromptSectionInject.Keyword,
    Order = 61)]
public static class DeadlockAuditSection
{
    public static string GetContent()
    {
        return """
# 死锁与并发审计规则

当用户提到"死锁"、"卡死"、"卡住了"、"竞态条件"、"分片锁"等关键词时，你必须执行以下审计：

## 锁审计检查清单

1. 重新审核每个锁，检查是否存在死锁问题
2. 锁必须排序——从小到大加锁，从大到小解锁
3. 尽可能所有锁加入超时释放（使用 TimeoutLock），除非用户明确拒绝
4. 异步上下文中禁止使用 lock/Monitor，必须改用 SemaphoreSlim
5. 多锁场景必须按固定顺序获取

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
2. 检查共享状态是否有并发保护（ConcurrentDictionary/lock/Interlocked）
3. 检查 check-then-act 模式是否原子
4. SQLite 单连接并发需加锁或使用 WAL 模式
5. 检查是否存在无限等待（无超时的 WaitOne/WaitAsync）
6. 检查是否有 .Result/.Wait() 阻塞异步操作
7. 检查 Channel/BlockingCollection 是否消费者停止消费

## 分片锁优化

1. 按键哈希分片减少锁争用
2. 每个分片独立 TimeoutLock
3. 分片数取质数（31/61/127）减少哈希碰撞
4. 锁排序：按分片索引从小到大加锁
5. 跨分片操作需获取所有相关分片锁，按序获取

## 可用MCP工具

- `code_index_search`: 搜索锁相关符号（SemaphoreSlim/lock/Monitor等）
- `code_index_find_references`: 查找锁的所有使用位置
- `code_index_get_callers` / `code_index_get_callees`: 分析锁的调用链
- `find_bugs`: 查找并发相关的潜在错误
- `security_audit`: 安全审计（含并发安全检查）
""";
    }

    public static SystemPromptSection Create()
    {
        return SystemPromptSection.Cached("deadlock_audit", GetContent);
    }
}
