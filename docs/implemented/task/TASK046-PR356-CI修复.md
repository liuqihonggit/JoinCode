# PR #356 CI 修复

同步上游 main（5fe068b8），保留 GUI 改造并修复 CI 暴露的时序问题。

| 基建实现 | 消费点 | 已实现 | 已验收 |
| --- | --- | --- | --- |
| 上游 rebase | codex/gui-experience | 是，无冲突 | 是 |
| 历史加载完成信号 | AnalyticsServiceTests | 是 | 406 项 BrainOther 测试通过 |
| 启动超时重试分类 | CoverageTestBase | 是 | 6 项分类测试及 22 项命令 E2E 通过 |
| 回归验证及推送 | PR #356 | 本地验证完成 | GUI 539 项通过，推送待完成 |

原因：Actor DisposeAsync 会取消消费队列，不能用作尚未开始的历史加载完成屏障。测试应等待加载日志发出的完成信号再释放服务。MockServer 的 GEN027/GEN028 启动超时需要纳入已有 16 次重试；其他配置或业务异常仍立即失败。

验证：新增重试回归测试首先因缺少分类函数编译失败，实现后 6 项通过。完整 BrainOther、GUI 及命令 E2E 共 973 项通过，无跳过。命令 E2E 实际启动 MockServer 和 jcc.exe。首次本地 E2E 被旧发布残留的 hostfxr/hostpolicy 文件干扰，归档到 .xxx 后，使用进程级 DOTNET_ROOT 指向私有 .NET 10 SDK 完成验证。没有修改系统运行时设置。

审查：保留现有 16 次上限，最终失败仍抛原异常；前缀精确匹配，配置文本包含错误码不会触发重试。历史测试使用异步完成信号，不依赖延迟或释放时机，不改变服务生产行为。
