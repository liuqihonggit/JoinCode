# 0124. 向量模型缺失自动下载

- 状态：accepted
- 日期：2026-10-01
- 决策者：用户 + AI

## 背景

`CodeIndexer.TryInitEmbeddingIndex` 当前行为：检查 `%AppData%/jcc/embedding/model_quantized.onnx` + `vocab.txt`，**缺失时静默 return**（向量搜索降级为不可用，用户毫无感知）。`OnnxEmbeddingClient` 构造函数则直接抛 `FileNotFoundException`。

问题：用户首次使用时向量搜索"默默不工作"，无引导无提示，违反 AGENTS.md 规则8"错误提示必须有诱导方式"。

用户要求："向量模型缺失的话，应该跟 git 一样，给用户默认下载"——参考 `git submodule update --init` / `git lfs pull` 缺失资产时从预配置远程自动拉取的行为模式。

## 决策

向量模型缺失时**自动下载**，而非静默降级：

1. **下载源：双源竞赛**（国内用镜像、国外用官方，自动适配网络环境）
   - 源1（官方）：`https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/`
   - 源2（镜像）：`https://hf-mirror.com/Xenova/all-MiniLM-L6-v2/resolve/main/`
   - **选源策略**：并发 HEAD 两个源的 model URL（3s 超时），先返回 200 的源胜出，用胜出的源下载 model + vocab。无需检测 IP 地理位置——国内官方超时则镜像胜出，国外官方快则官方胜出。
   - 实测：本机 `huggingface.co` 超时不可达，`hf-mirror.com` 可达且 `Content-Length` 与本地完全匹配
   - 文件：
     - `onnx/model_quantized.onnx`（22,972,370 字节）
     - `vocab.txt`（231,508 字节）
2. **完整性校验**：SHA256 硬编码（下载后校验，不匹配抛异常）
   - model: `AFDB6F1A0E45B715D0BB9B11772F032C399BABD23BFC31FED1C170AFC848BDB1`
   - vocab: `07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3`
3. **行为**：`TryInitEmbeddingIndex` 缺失时调 `EmbeddingModelDownloader.EnsureAsync()` → 下载到 `%AppData%/jcc/embedding/` → SHA256 校验 → 下完继续初始化；**下载失败才降级**（保持原有静默跳过 + 日志警告）
4. **下载机制**：复用 `IHttpClientProvider` 抽象（`HttpClient.GetByteArrayAsync`），22MB 文件无需断点续传
5. **进度提示**：下载前 `Console.Error.WriteLine` 输出提示（对齐 commit 75d7148 的 stderr 进度风格），让用户知道正在下载

## 替代方案（考虑过但放弃）

- **A. 单源 hf-mirror**：只用镜像。放弃 — 国外用户访问镜像反而慢，用户明确要求"国内用国内源、国外用国外的"。
- **B. 检测 IP 地理位置**：调 IP 定位 API 判断国内/国外。放弃 — 引入额外网络依赖、API 可能失效、隐私问题；并发竞赛天然解决，无需检测。
- **C. 顺序尝试（官方→镜像 fallback）**：先试官方，超时再镜像。放弃 — 国内用户每次都要等官方超时（15s），体验差；竞赛方案国内用户立即用镜像。
- **D. 项目 GitHub Release 自托管**：把模型打包发到本项目 Release。放弃 — 需先上传模型到 Release，增加发布流程复杂度；GitHub Release 国内同样可能慢。
- **E. 可配置源（settings.json 覆盖默认）**：硬编码双源为默认，`embedding.model_url` 可覆盖。暂不采用 — 双源竞赛已覆盖国内外，配置层过度设计；未来有多源需求时再扩展。
- **F. 用 RangeDownloader 断点续传**：复用 `lib/infrastructure/network/downloader/`。放弃 — 22MB 文件一次性下载即可，断点续传复杂度不值得。
- **G. 保持静默降级 + 仅日志提示**：不改行为，只加提示。放弃 — 用户明确要求"默认下载"，首次使用体验差。

## 后果

- **正面**：首次使用向量搜索开箱即用，无需用户手动找模型；对齐 git 的"缺失即拉取"体验。
- **负面**：首次下载 22MB 需网络（~数秒~数十秒取决于带宽）；若 hf-mirror.com 故障则降级（与原行为一致）。
- **中性**：SHA256 硬编码意味着模型升级需改代码（可接受，模型版本应固定以保证向量一致性）。

## 验证

- 单元测试：mock `HttpMessageHandler` 返回预设字节 → 验证下载+校验+写入
- 单元测试：SHA256 不匹配 → 抛异常
- 单元测试：文件已存在且校验通过 → 跳过下载
- 集成测试：`VectorIndexE2ETests` 保持 CI 无网络时跳过（不依赖自动下载）
