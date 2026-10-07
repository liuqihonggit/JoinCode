# ADR 0131: 提取下载器为独立组件

## 状态

accepted

## 背景

下载基建(IDownloader/IBatchDownloader + 多线程分片 + 断点续传)原位于 lib/infrastructure/network/downloader/,属于 Infrastructure 项目。

经分析,downloader 是自包含模块,所有外部依赖仅来自 Abstractions 层(IHttpClientProvider/IFileSystem/ServiceEntity/Register/INetworkConnectivityService/ImmutableHamT/Fsm 等),不依赖 Infrastructure 项目其他代码。反向依赖(Infrastructure 中 GitHubApiClient 用 IDownloader)是单向的,无循环风险。

## 决策

将 downloader 提取为独立 csproj lib/downloader/Downloader.csproj,作为七层架构中 Foundation 层的独立基础设施组件。

### 引用关系(提取后)

```
Abstractions (含 Structura/AsyncLock 传递)
     ↑
Downloader ──► (Fsm.Generator / EnumMetadata.Generator 作为 analyzer)
     ↑
Infrastructure (GitHubApiClient 用 IDownloader)
```

### 命名空间

保持 Infrastructure.Network.Downloader.* 不变(零代码改动,消费方 using 不变)。后续如需改命名空间为 JoinCode.Downloader.*,单独处理。

## 原因

1. 单一职责:下载是自包含能力,不应耦合在 Infrastructure 的杂项中
2. 轻量复用:其他项目可仅引用 Downloader(不引入整个 Infrastructure),减少编译依赖
3. 七层架构对齐:下载属于 Foundation 层能力,独立项目符合分层
4. 可独立打包:可单独发布 nuget 包,供外部项目复用
5. 测试隔离:下载测试可独立项目,不依赖 Infrastructure 测试基建

## 替代方案

### 方案B:保持在 Infrastructure

- 优点:零改动
- 缺点:耦合持续,其他项目引用 Infrastructure 引入大量无关代码
- 放弃原因:用户要求提取为独立组件

### 方案C:改命名空间为 JoinCode.Downloader.*

- 优点:语义清晰,项目名与命名空间一致
- 缺点:需批量替换 21 个文件 namespace + 所有消费方 using,改动大易错
- 放弃原因:渐进式优先,先提取项目,命名空间后续优化

## 验收标准

- [x] lib/downloader/Downloader.csproj 创建完成
- [x] downloader 代码移动到 lib/downloader/
- [x] Infrastructure.csproj 引用 Downloader.csproj
- [x] JoinCode.slnx 包含 Downloader.csproj
- [x] 全量编译 0 警告 0 错误
- [x] 全量单元测试通过(495+784+357)
