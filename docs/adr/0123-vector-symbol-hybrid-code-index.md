# 0123. 向量+符号混合代码索引架构

- 状态：accepted
- 日期：2026-09-30
- 决策者：用户 + AI

## 背景

通用 Coding Agent 需要代码索引支持两类查询：

1. **语义搜索**："找类似这段的代码"、"解释这个函数" — 需要向量嵌入 + ANN 搜索
2. **精确符号查询**："谁调用了 Foo"、"调用链 A→B" — 需要符号图 + BFS 遍历

现有 `server/code_index/` 模块已有符号图（TreeSitter 切块 + CallGraph + DependencyGraph + IncrementalUpdater），但无向量层，无法做语义搜索。

参考 Cursor 的向量切割方案（tree-sitter 切块 → 块哈希缓存 → 嵌入向量 → ANN 搜索），但 Cursor 是云端 SaaS 架构（turbopuffer + Merkle 树），本项目是客户端本地工具，需要不同的技术选型。

## 决策

采用**向量+符号混合查询**架构：

1. **符号图（已有，复用）**：TreeSitter 切块 + CallGraph + DependencyGraph + IncrementalUpdater
2. **向量层（新增）**：
   - 嵌入模型：all-MiniLM-L6-v2 **量化版**（22MB INT8），ONNX 独立 exe 跨进程
   - 向量搜索：`System.Numerics.Vector<float>` SIMD 暴力搜索（10K×256维 0.97ms）
   - 存储：纯内存，无持久化
3. **混合查询路由**：符号型走符号图，语义型走向量，混合型双路并行+融合
4. **降级链**：向量→符号搜索→ripgrep

**关键技术选型**：
- ONNX 在独立 exe 进程跑（非 AOT），主程序 AOT 通过 IPC 调用 — 绕过 NativeAOT 限制
- 量化模型（22MB）+ seq=32 + batch=32 — 实测 5000块索引 7.0s（<10s 目标）
- MRL 降维到 256 维 — 内存降 6x，搜索速度提 6x

## 替代方案（考虑过但放弃）

- **A. 纯符号图（Roslyn 语义层）**：用 Roslyn SemanticModel 精确解析重载/接口实现/泛型方法调用。放弃 — 无法做语义搜索（"找类似代码"），且 Roslyn 重、首次解析慢、NativeAOT 兼容性差。
- **B. 纯向量 RAG**：只用向量搜索，不做符号图。放弃 — 丢失精确调用关系（"谁调用Foo"需要确定性图遍历，向量搜索是近似的）。
- **C. 向量用 turbopuffer 云端**：Cursor 的方案。放弃 — 本地工具不需要 serverless 向量库，引入网络依赖+API 成本+延迟，过度设计。
- **D. ONNX 直接引用**：主程序直接引用 `Microsoft.ML.OnnxRuntime`。放弃 — AGENTS.md 禁止微软 AI 包直接引用（NativeAOT 不兼容），用卫星项目/独立 exe 跨进程绕过。
- **E. API 嵌入（主路径）**：调 OpenAI text-embedding-3-small。放弃作为主路径 — 网络延迟+API 成本+不离线。可作为精排补充。
- **F. Merkle 树增量同步**：Cursor 的方案。放弃 — 本地单进程不需要跨端同步协议，`ContentHash` 更简单。
- **G. HNSW 向量索引**：图索引 O(log n) 查询。放弃起步 — 10K 级别暴力+SIMD 已够快（0.97ms），HNSW 建图慢且复杂，后续量大再升级。

## 后果

- **正面**：
  - 离线+极速：5000块索引 7.0s（量化模型+seq=32），向量搜索 0.97ms
  - 双能力：语义搜索（向量）+ 精确符号查询（符号图）
  - 语言无关：TreeSitter 28+ 语言，向量层对任何语言一视同仁
  - 纯内存：无持久化开销，进程退出释放，重建 <10s
- **负面**：
  - 需维护 ONNX 卫星项目（`tool/onnx_embedding/`，非 AOT）
  - 模型文件分发（22MB 量化模型，首次运行下载或随程序打包）
  - seq=32 限制块大小，长函数需截断（可能丢失部分语义）
- **中性**：
  - DirectML GPU 不可用（此机器不兼容），CPU 推理已达标
  - 量化模型精度略低于原版（可接受，代码搜索非高精度任务）

## 参考

- 设计文档：[DSG030](../design/DSG030-vector-code-index-design.md)（完整架构+三条流水线+数据结构+实现路径）
- 卫星验证：`tool/simd_vector_verify/`（SIMD 0.97ms）、`tool/onnx_embedding_verify/`（ONNX 7.0s）
- 现有符号图：`server/code_index/`（CallGraph + DependencyGraph + IncrementalUpdater）
- AGENTS.md："nuget包: 拒绝全部微软的AI包，因为大部分不支持NativeAOT" → 卫星项目绕过
- AGENTS.md："复杂任务: 需要单独项目做测试,避免工程冗余,可以制作卫星项目" → ONNX 卫星项目
