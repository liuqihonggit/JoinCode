# TASK051 — 情绪化工具闭环设计

> 状态：proposed
> 创建：2026-10-14
> 关联记忆：project-emotion-tool-missing-loop（工程四层架构已完整，唯一缺失情绪化工具闭环）

## 1. 背景

用户期望工具具备情绪化反馈能力：对丑代码表达情绪、教会 AI 什么是美、采集采纳率。工程已有完整的四层架构（工具层/推荐层/运行时层/数据层），底层基础设施已就绪（中间件管道、PithosDB、PromptSection、CronScheduler），唯一缺失的拼图是**情绪化工具**机制。

用户原始构想包含 5 个方面：
1. 工具本身提供情绪价值（read 多字段→建议拆分、死代码提示、bug/fixme 弹出、同情/焦虑/生气情绪）
2. 评分（顶美/好/合格/差/烂）
3. 教会它什么是美（环环相扣/强迫症/围绕核心生长/可读性和高性能）
4. 涉及情绪部分都需要加以评分（用户自由关闭、采集 AI 采纳率和正确率）
5. 工具提示词作为系统提示词 + 冷却期提示

经审视，用户方案中仍有 **12 个未落地的设计盲点**。本文档给出每个盲点的定论式落地方案。

## 2. 十二个盲点落地方案

### #1 情绪向量维度定义（致命）

**定论：1024 维潜空间（学习型）+ 5 维可解释投影（展示型）**

```csharp
// 1024 维潜空间 — 通过反向传播学习，用于相似度搜索和内部计算
public readonly struct EmotionLatent
{
    public readonly float[] Data;  // 长度 1024，∈ [-1, 1]
    public EmotionLatent(float[] data) => Data = data;
}

// 5 维可解释投影 — 从 1024 维线性投影而来，用于展示和调试
public readonly struct EmotionProjection
{
    public float Valence   { get; }   // [-1,1] -1消极 +1积极
    public float Arousal   { get; }   // [-1,1] -1平静 +1激动
    public float Dominance { get; }   // [-1,1] -1失控 +1掌控
    public float Confusion { get; }   // [0,1]   0清晰  1不懂（代码专属）
    public float Aesthetic { get; }   // [-1,1] -1丑   +1美（代码专属）
}
```

**理由：**
- 1024 维（2^10）是学习型潜空间，SIMD 友好，和常见 embedding 模型维度对齐，通过反向传播自动学习情绪表示，不需要手工设计映射规则
- 5 维（VAD+Confusion+Aesthetic）是可解释投影，从 1024 维线性投影而来，用于展示和调试。VAD 是学术界验证过的连续值模型
- 1024 维黑盒做计算（相似度搜索/反向传播），5 维白盒做解释（展示/调试/校准）

**落地位置：** `modules/perception/emotion/`（和 `code_index` 平级，复用 perception 的 ONNX/Tensor/SIMD 基础设施）

---

### #2 美学评分校准基准（致命）

**定论：三层校准 = Golden Set + Cohen's Kappa + 置信度标注**

1. **Golden Set**：人工标注 50-100 个代码片段的 5 级评分（每级 10-20 个），存 `modules/cognition/emotion/golden_set/`。一次性建设。
2. **校准**：AI 对 Golden Set 评分，与人类标注算 Cohen's Kappa。κ < 0.4 → AI 评分不可信 → 触发重新校准。校准脚本 `tools/emotion.calibrate`。
3. **置信度**：AI 每次评分附 `confidence: float[0,1]`，基于"被评代码与 Golden Set 已知样本的相似度"。低置信度评分不进入采纳率统计。

**理由：** 50-100 样本足够建标尺（类比 rubric）。κ 是统计学标准一致性度量：<0.4=差，0.4-0.6=中等，>0.6=好，有明确阈值。AI 自评必有 Dunning-Kruger 效应，无校准 = 自嗨。

---

### #3 采纳率正确率归因定义（致命）

**定论：2×2 矩阵 + F1 主指标 + 延迟判定**

| | AI 采纳 | AI 拒绝 |
|---|---|---|
| **建议正确** | TP | FN（错过好建议） |
| **建议错误** | FP（采纳坏建议） | TN（正确拒绝） |

- 主指标：**F1 = 2·P·R/(P+R)**
  - Precision = TP/(TP+FP)：AI 采纳的建议中多少是对的
  - Recall = TP/(TP+FN)：所有正确建议中 AI 采纳了多少
  - 单看 P 可刷（只对 100% 确定才建议），单看 R 可刷（什么都建议），F1 平衡两者
- **"建议正确/错误"判定**：短期用延迟判定（等代码下次修改/测试结果反推），长期用人类抽检 10% 校准
- 采集全部 4 个计数，不只存 F1（保留归因能力）

---

### #4 Goodhart 防线（致命）

**定论：三道防线 = 盲评 + 延迟反馈 + 评分函数不暴露**

1. **盲评**：评分中间件拿到的代码不带 author 元数据，AI 不知道评的是谁的代码 → 消除自我美化偏差
2. **延迟反馈**：AI 当前会话看不到自己的历史评分。评分后台聚合，定期生成报告供人类审查。AI 无法实时调整策略
3. **评分函数不暴露**：AI 只知道"会被评分"，不知道确切公式。权重从配置文件/DB 读取（可变），人类定期调整。**你无法优化你不知道的函数**

**理由：** 第 3 道是根本防线。即使 AI 逆向推导了当前权重，下次权重变了。前两道是辅助。

---

### #5 情绪累积/衰减模型

**定论：指数衰减 + 会话级累积**

```
mood[t] = 0.85 * mood[t-1] + 0.15 * event[t]
```

- 会话内累积，会话结束存档
- 新会话从 0 开始（避免上次心情污染这次判断）
- decay=0.85 可配置：高=心情稳定（长记忆），低=情绪化（短记忆）

**落地位置：** `modules/perception/emotion/EmotionAccumulator.cs`

---

### #6 情绪过载聚合

**定论：展示 Top-2 + 内部加权平均**

- 多种情绪同时触发：展示层只取强度 Top-2（避免过载），如"难过（多职责）+ 困惑（控制流混乱）"
- 内部 EmotionVector 按强度加权平均聚合成一个（用于累积心情）
- 冷却期内重复触发的只计数不展示，冷却结束一次性报"过去 X 分钟类似情况 N 次"

---

### #7 冷却期参数

**定论：三维冷却**

| 维度 | 冷却时间 | 理由 |
|------|---------|------|
| 同文件同类型提示 | 30min | 同文件已提示过"多字段"，不重复 |
| 同类型跨文件提示 | 10min | 连续多文件触发死代码，只提示第一次+计数 |
| 同会话同情绪 | 5min | 焦虑情绪 5min 内不重复触发 |

冷却期 AI 再次遇到同类：**静默计数**。冷却结束若计数 > 0，一次性汇总提示。冷却期提示文案需明确"短时间内不会再显示"，避免 AI 误会为被忽略。

---

### #8 执行前预判

**定论：加 `ToolPreJudgmentMiddleware`（Order=905，在 Aesthetic Order=910 之前）**

- read >2000 行 → 预提示"建议先 grep 定位"
- read 有未解决 bug 标记的文件 → 预提示
- **执行前预判 = 建议性（不阻塞），执行后反馈 = 评价性**

---

### #9 品味主权

**定论：分层主权 — 结构性 AI 说了算，风格性用户说了算**

- **结构性**（多职责/死代码/环环相扣断裂）→ AI 有权坚持，可标记"阻塞"（拒绝继续直到解决）。这是工程客观规律。
- **风格性**（命名/缩进/注释密度）→ 迁就用户，可一键关闭。这是偏好。

---

### #10 功能性 vs 表演性情绪

**定论：功能性为主，表演性为辅，表演必须由功能驱动**

- **功能性**（主）：情绪是决策信号。难过 → 提高测试覆盖率要求；困惑 → 触发更多调查。改变 AI 行为参数。
- **表演性**（辅）：输出层附加情感措辞（"此处令人难过"），不影响决策。
- **铁律**：不能凭空表演。必须"检测到多职责 → 功能性难过 → 输出层表达"。假装难过 = 禁止。

---

### #11 A/B 对照组

**定论：10% 随机对照组 + 会话级分流**

- 10% 工具调用进"无情绪反馈"对照组，90% 进实验组
- 会话级分流（同一会话始终在对照组或实验组，避免切换干扰）
- 比较两组：采纳率、任务完成质量、后续 bug 率

---

### #12 中间件注入点

**定论：情绪不改写返回内容，只附加元数据**

```csharp
public sealed record ToolResult
{
    public required string Content { get; init; }      // 原始，不变
    public ToolResultMetadata? Metadata { get; init; }
}

public sealed record ToolResultMetadata
{
    public EmotionLatent? EmotionLatent { get; init; }      // 1024 维潜空间
    public EmotionProjection? EmotionProjection { get; init; } // 5 维可解释投影
    public float? AestheticScore { get; init; }
    public IReadOnlyList<string> Suggestions { get; init; } = [];
}
```

下游中间件（缓存/日志/审计）拿原始 Content 不受影响。情绪由 `EmotionRenderer` 在输出给用户/AI 时才渲染成自然语言。**非侵入式，不影响管道一致性。**

---

## 3. 落地优先级

| 顺序 | 盲点 | 理由 | PR |
|------|------|------|-----|
| 1 | #1 维度定义 | 所有其他机制的数据基础 | PR1 |
| 2 | #12 注入点 | 决定管道架构，先定架构再填逻辑 | PR1 |
| 3 | #5 累积模型 | 有了维度就能累积 | PR1 |
| 4 | #6 过载聚合 | 有了累积就需要聚合 | PR1 |
| 5 | #2 校准基准 | 评分可信性的前提 | PR2 |
| 6 | #3 归因定义 | 采集的前提 | PR2 |
| 7 | #4 Goodhart | 采集后的防线 | PR2 |
| 8 | #7 冷却期 | UX 层，依赖 #1 数据结构 | PR3 |
| 9 | #8 执行前预判 | 独立中间件 | PR3 |
| 10 | #9 品味主权 | 策略层，依赖评分体系 | PR3 |
| 11 | #10 功能vs表演 | 输出层，依赖前面全部 | PR3 |
| 12 | #11 A/B 对照 | 实验层，最后叠加 | PR3 |

**PR1（#1/#12/#5/#6）**：数据结构 + 管道注入 + 累积 + 聚合 — 一条连贯链
**PR2（#2/#3/#4）**：评分可信性 — Golden Set + 归因 + Goodhart 防线
**PR3（#7-#11）**：UX/策略/输出/实验层 — 优化层

## 4. 架构挂载点

中间件管道（已有）：

```
工具执行(已有 Order=900)
  → 执行前预判(新增 Order=905 ToolPreJudgmentMiddleware)    [#8]
  → 结果分析(新增 Order=910 ToolAestheticMiddleware)        [#1 #6 #10]
  → 情绪评分(新增 Order=920 ToolAestheticScorer)            [#2 #3]
  → 采纳率记录(新增 Order=930 AdoptionTracker)              [#3 #4]
  → 提示词注入(已有 PromptSection Order=12 "aesthetic")     [#10 教会什么是美]
  → 持久化(已有 PithosDB)                                    [#3 #5]
  → 定时聚合(已有 CronScheduler)                             [#3 #11]
```

新建子域：`modules/perception/emotion/`（和 `code_index` 平级，复用 perception 的 ONNX/Tensor/SIMD 基础设施）

## 6. 学习型架构（1024 维 + 反向传播 + CPU 加速）

> 用户追加设计方向：① 1024 长度情绪向量 ② 反向传播学习 ③ CPU 加速小模型

### 6.1 模型架构

**纯 C# 手写小 MLP + 在线反向传播**

```
输入：768维（复用现有 OnnxEmbeddingClient 代码 embedding，带缓存）
  ↓
Linear(768, 256) + ReLU       // 197K params
  ↓
Linear(256, 1024) + Tanh      // 262K params
  ↓
输出：1024维情绪潜空间 ∈ [-1, 1]^1024
  ↓
Linear(1024, 5)               // 5K params（可解释投影，冻结或慢学习）
  ↓
输出：5维可解释投影 (V,A,D,Conf,Aest)
```

- 总参数 ~464K，float32 = 1.86MB
- 推理延迟：CPU SIMD ~0.5ms（缓存命中后 <1ms 总延迟）
- 训练延迟：单样本反向传播 ~1.5ms，批量 32 样本 ~15ms

### 6.2 反向传播实现

**前向传播**（`TensorPrimitives.Dot` SIMD 矩阵-向量乘法）：
```
h = ReLU(W1 · x + b1)        // W1: 256×768
z = Tanh(W2 · h + b2)        // W2: 1024×256, z: 1024维潜空间
p = W3 · z + b3              // W3: 5×1024, p: 5维投影
```

**反向传播**（链式法则，`Span<float>` 零分配）：
```
dp = ∂L/∂p                    // 损失对投影的梯度
dz = W3^T · dp + ∂L/∂z        // 反传到潜空间（+对比学习/一致性损失梯度）
dh = (z > 0) ⊙ (W2^T · dz)   // ReLU 反传
dW1 = dh · x^T, dW2 = dz · h^T, dW3 = dp · z^T  // 权重梯度
W -= lr · (dW + λ·W)          // SGD + L2 正则
```

**在线增量学习**：
- 每次工具调用产生（代码 embedding, 情绪反馈）对
- 反向传播微调 W1/W2（W3 投影层慢学习或冻结）
- 经验回放：保留最近 1024 个样本，每次随机抽 32 训练，避免偏移
- 学习率 1e-4 + L2 正则 1e-5，避免灾难性遗忘

### 6.3 CPU 加速方案

| 层 | 方案 | 现有基础 | 状态 |
|----|------|---------|------|
| 矩阵运算 | `TensorPrimitives.Dot` SIMD 矩阵-向量乘法 | `OnnxEmbedder.cs` 已用 | ✅ |
| 内存 | `Span<float>` 零分配 + stackalloc 中间结果 | 项目风格 | ✅ |
| embedding 缓存 | 复用 `EmbeddingIndex` 块哈希缓存 | 已实现 | ✅ |
| 模型加载 | mmap `.ort` 格式零拷贝 | `OnnxEmbedder.cs` 已实现 | ✅ |
| 编译 | NativeAOT + TreatWarningsAsErrors | 已强制 | ✅ |
| 量化 | int8 推理（训练时 float32） | 可选后续优化 | 🔵 |

### 6.4 训练数据策略

1024 维输出 + 464K 参数，50-100 个 Golden Set 不够。组合策略：

| 策略 | 标注成本 | 损失函数 | 说明 |
|------|---------|---------|------|
| 对比学习 | 低（相对标注） | ContrastiveLoss | "代码A比B更消极"，标注成本远低于绝对标注 |
| 自监督 | 零 | ConsistencyLoss | 同代码多次评估应一致 |
| 采纳率反馈 | 零 | AdoptionFeedback | AI 采纳→正反馈，拒绝→负反馈 |
| Golden Set | 中（50-100 样本 5 维） | MSE(投影, 标注) | 只标注 5 维投影，不标注 1024 维 |

**组合损失**：`L = α·Contrastive + β·Consistency + γ·Adoption + δ·MSE`
- α/β/γ 不需要人工标注，可立即开始训练
- δ 需要 Golden Set，但只标注 5 维投影，成本低
- α/β/γ/δ 权重从配置文件读取（Goodhart 防线 #4）

### 6.5 复用现有基础设施

| 现有设施 | 位置 | 复用方式 |
|---------|------|---------|
| OnnxEmbeddingClient | `modules/perception/code_index/embedding/` | 生成 768 维代码 embedding 作为模型输入 |
| TensorPrimitives | `modules/tools/onnx_embedding/OnnxEmbedder.cs` | SIMD 矩阵-向量乘法 |
| EmbeddingIndex 缓存 | `modules/perception/code_index/embedding/EmbeddingIndex.cs` | embedding 缓存，代码不变不重算 |
| .ort mmap 加载 | `OnnxEmbedder.cs:35-41` | 模型文件 mmap 零拷贝加载 |
| AST 工具 | `non_deliverables_tools/jcc_audit_ast_cli/` | 可选：提取结构特征补充 embedding |
| PithosDB | 已有 | 持久化训练样本和模型权重 |
| CronScheduler | 已有 | 定时聚合采纳率/训练批次 |

## 5. 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `EmotionLatent` 1024维潜空间 (#1) | `ToolAestheticMiddleware` 分析结果 | ❌ | ❌ |
| `EmotionProjection` 5维可解释投影 (#1) | 展示层 + 校准 | ❌ | ❌ |
| 小 MLP 模型 768→256→1024 (§6.1) | `EmotionInference` 推理入口 | ❌ | ❌ |
| 反向传播前向+反向 (§6.2) | `EmotionTrainer` 在线训练 | ❌ | ❌ |
| `TensorPrimitives.Dot` MatMul (§6.3) | 模型前向传播 | ❌ | ❌ |
| 经验回放 1024 样本 (§6.2) | `EmotionTrainer` 训练数据管理 | ❌ | ❌ |
| 对比学习+自监督+采纳率反馈 (§6.4) | 组合损失函数 | ❌ | ❌ |
| `ToolResultMetadata` 元数据附加 (#12) | 下游中间件 + `EmotionRenderer` | ❌ | ❌ |
| `EmotionAccumulator` 指数衰减 (#5) | 会话级心情累积 | ❌ | ❌ |
| 过载聚合 Top-2 策略 (#6) | `ToolAestheticMiddleware` 多触发合并 | ❌ | ❌ |
| Golden Set 50-100样本 (#2) | `tools/emotion.calibrate` 校准脚本 | ❌ | ❌ |
| Cohen's Kappa 一致性校验 (#2) | 校准脚本输出 | ❌ | ❌ |
| 2×2 归因矩阵 + F1 (#3) | `AdoptionTracker` + PithosDB | ❌ | ❌ |
| 盲评+延迟+函数不暴露 (#4) | 评分中间件 + 配置文件权重 | ❌ | ❌ |
| 三维冷却期 (#7) | `CooldownManager` | ❌ | ❌ |
| `ToolPreJudgmentMiddleware` (#8) | 中间件管道 Order=905 | ❌ | ❌ |
| 分层品味主权 (#9) | 阻塞标记 + 一键关闭 | ❌ | ❌ |
| 功能性→表演性驱动链 (#10) | `EmotionRenderer` 输出层 | ❌ | ❌ |
| A/B 10%对照组 (#11) | 会话级分流 + CronScheduler 聚合 | ❌ | ❌ |

## 6. 决策记录

<!-- 🤖 Auto Decision: 2026-10-14 -->
<!-- 决策: 用 1024 维潜空间(反向传播学习) + 5 维可解释投影(VAD+Confusion+Aesthetic) -->
<!-- 原因: 1024 维(2^10) SIMD 友好，通过反向传播自动学习情绪表示，不需要手工映射规则；5 维投影保留可解释性用于展示和校准 -->
<!-- 替代方案: 纯 5 维手工映射（无法学习）、纯 1024 维无投影（黑盒不可解释）、Plutchik 8 维（离散空间不适合反向传播）-->
<!-- 验证: 待实现 -->

<!-- 🤖 Auto Decision: 2026-10-14 -->
<!-- 决策: 纯 C# 手写小 MLP(768→256→1024, 464K 参数) + 在线反向传播，不用 ONNX -->
<!-- 原因: 用户要"反向传播学习"=运行时训练，ORT Training API 较重；460K 参数小 MLP 手写前向+反向完全可行；零外部依赖、NativeAOT 兼容 -->
<!-- 替代方案: ONNX 推理+离线训练（无法在线学习）、ORT Training（较重）、ML.NET（深度学习支持弱）-->
<!-- 验证: 待实现 -->

<!-- 🤖 Auto Decision: 2026-10-14 -->
<!-- 决策: 输入用 768 维 OnnxEmbeddingClient 代码 embedding（带缓存），不用 128 维手工特征 -->
<!-- 原因: 复用现有 embedding 基础设施，输入已是"理解了代码语义"的向量，质量远高于手工特征；embedding 有缓存(代码不变不重算)，首次~50ms，后续<1ms -->
<!-- 替代方案: 128 维手工 AST 特征（零延迟但质量低）、混合方案（embedding+手工特征拼接）-->
<!-- 验证: 待实现 -->

<!-- 🤖 Auto Decision: 2026-10-14 -->
<!-- 决策: 情绪子域放在 modules/perception/emotion/ 而非 modules/cognition/emotion/ -->
<!-- 原因: perception 已有 ONNX/Tensor/SIMD/Embedding 全套基础设施，emotion 本质是"从代码中感知情绪"；cognition 是纯逻辑层(推理/规划/工具调度)，不应直接依赖 ONNX -->
<!-- 替代方案: cognition/emotion/（需跨域引用 perception）、新建独立域（碎片化）-->
<!-- 验证: 待实现 -->

<!-- 🤖 Auto Decision: 2026-10-14 -->
<!-- 决策: 训练数据用对比学习+自监督+采纳率反馈(零标注) + Golden Set(仅标注5维投影) -->
<!-- 原因: 1024 维输出需要大量数据，但对比学习只需相对标注、自监督/采纳率反馈零标注、Golden Set 只标注 5 维投影(不是 1024 维)，组合后标注成本极低 -->
<!-- 替代方案: 纯 Golden Set 标注 1024 维（成本极高）、纯自监督（收敛慢）、纯采纳率反馈（无校准）-->
<!-- 验证: 待实现 -->

<!-- 🤖 Auto Decision: 2026-10-14 -->
<!-- 决策: 情绪不改写工具返回内容，只附加元数据(ToolResultMetadata) -->
<!-- 原因: 非侵入式，下游中间件(缓存/日志/审计)拿原始Content不受影响，管道一致性不被破坏 -->
<!-- 替代方案: 情绪直接拼接到Content（侵入式，破坏缓存一致性）-->
<!-- 验证: 待实现 -->

<!-- 🤖 Auto Decision: 2026-10-14 -->
<!-- 决策: 采纳率主指标用 F1 而非 Precision 或 Recall -->
<!-- 原因: 单看P可刷(只对100%确定才建议)，单看R可刷(什么都建议)，F1平衡两者且对极端策略不友好 -->
<!-- 替代方案: Precision（可被刷高）、Recall（可被刷高）、纯TP计数（无归因能力）-->
<!-- 验证: 待实现 -->

<!-- 🤖 Auto Decision: 2026-10-14 -->
<!-- 决策: Goodhart 防线用"评分函数不暴露"为根本防线 -->
<!-- 原因: 你无法优化你不知道的函数。权重从配置/DB读取(可变)，AI即使逆向推导当前权重，下次也变了 -->
<!-- 替代方案: 仅靠盲评+延迟（可被逆向推导后绕过）-->
<!-- 验证: 待实现 -->

<!-- 🤖 Auto Decision: 2026-10-14 -->
<!-- 决策: 品味主权分层——结构性AI说了算，风格性用户说了算 -->
<!-- 原因: 结构性(多职责/死代码)是工程客观规律，AI有权坚持；风格性(命名/缩进)是偏好，迁就用户 -->
<!-- 替代方案: 全部AI说了算（说教感重）、全部用户说了算（谄媚，失去价值）-->
<!-- 验证: 待实现 -->
