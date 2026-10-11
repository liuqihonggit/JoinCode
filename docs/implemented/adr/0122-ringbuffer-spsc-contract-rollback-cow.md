# 0122. RingBuffer 确认为 SPSC 契约,回退 Copy-on-Write 重写

- 状态：accepted
- 日期：2026-09-30
- 决策者：用户 + AI

## 背景

ADR 0121 将 RingBuffer 并发 bug 误判为生产代码缺陷,用"不可变数组 + volatile 引用 + CAS 交换"(Copy-on-Write)重写了整个数据结构。用户质疑:经典环形队列本身就是无锁的,问题应该是某个具体地方改不对,而非整个设计需要重写。

经复查:

1. **旧设计本身正确** — 可变数组 + head/tail 指针 + PaddedInt,注释"多生产者单消费者"。`TryEnqueue` 用 CAS(多生产者安全),`TryDequeue` 用 volatile(单消费者安全)。
2. **bug 根因是测试违反契约** — `Add` 方法 `while (!TryEnqueue(item)) TryDequeue(out _)` 在满时调用 `TryDequeue`(单消费者操作)。多线程并发 `Add` → 多个线程同时移动 head 指针 → 违反单消费者约束。测试 `RingBufferMultiWriterTests`(#76)和 `RingBufferTests.MultiThread_ConcurrentAdd_NoException` 错误假设 `Add` 支持多写者,违反了 SPSC 契约。
3. **实际调用方全是单线程** — 5 处调用方(ShannonEntropyDetector / StreamTokenDetector / ReasoningRound / ToolCallSequenceDetector / LogicFingerprintDetector)都是单线程推理循环 / Actor 邮箱调用 `Add`,从不多线程并发。
4. **Copy-on-Write 重写是过度设计** — O(1) 环形队列变成 O(n) 复制数组,改变了数据结构本质。

## 决策

1. **回退 Copy-on-Write 重写**,恢复原来的可变数组 + head/tail 指针 + PaddedInt 实现
2. **明确 SPSC 契约**:类注释改为"单生产者单消费者",`Add` 标注"单生产者覆盖写入 — 多线程并发调用 Add 违反 SPSC 契约;多生产者场景用 Actor 邮箱/消息管道序列化"
3. **删除违规多线程测试**:移走 `RingBufferMultiWriterTests` 和 `RingBufferTests.MultiThread_ConcurrentAdd_NoException`(违反 SPSC 契约)
4. **未来真需要 MPMC**:用 Actor / 消息管道序列化,不在 RingBuffer 里搞;或直接用 BCL `ConcurrentQueue<T>`

## 替代方案(考虑过但放弃)

- **保留 Copy-on-Write 重写**(ADR 0121):O(n) 复制,改变数据结构本质,过度设计。放弃。
- **改 Add 为 CAS 移动 head**:覆盖写入需要同时原子移动 head 和 tail,两个 CAS 不能原子一起执行,复杂度高且无实际需求。放弃。
- **用锁保护 Add**:违背无锁设计初衷,且无实际多生产者需求。放弃。

## 后果

- RingBuffer 恢复 O(1) Add/TryEnqueue/TryDequeue 性能
- 契约清晰:SPSC,单线程调用;多生产者用 Actor/消息管道
- 违规多线程测试已移除,不再误报
- ADR 0121 标记为 superseded by 0122

## 参考

- superseded: [0121](0121-ringbuffer-immutable-cas-lockfree.md)(Copy-on-Write 重写,已废弃)
- 项目并发集合选型决策树(AGENTS.md):"高性能单生产者单消费者 → 手写 SPSC Ring Buffer"
