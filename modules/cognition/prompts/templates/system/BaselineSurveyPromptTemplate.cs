namespace Core.Prompts.Templates.System;

/// <summary>
/// 基准对比调研提示词模板 — 四问深度法。
/// 每项差异/bug 调研前必答四问：专家隐性实践/原始素材清单/具体方法与工具/独立验证来源。
/// 深度优先于广度：追溯到原始规范源头 + 独立验证，而非扩大调研域数。
/// </summary>
/// <remarks>
/// 消费者：主代理派发调研子代理（Task subagent_type=explore）时程序化引用本模板。
/// 关联：AGENTS.md「基准对比工作流」章节、TASK072 提示词模板文档。
/// </remarks>
[PromptTemplate(Name = "baseline_survey", Category = PromptTemplateCategory.System, Description = "基准对比调研提示词模板（四问深度法）— 追溯原始规范 + 独立验证", HasParameters = true)]
public static class BaselineSurveyPromptTemplate {
    /// <summary>
    /// 获取基准对比调研提示词（四问深度版）。
    /// </summary>
    /// <param name="domain">调研域（如"键序列解析"/"渲染管线"）。</param>
    /// <param name="problem">具体问题（如"CSI 泛化边界扫描"/"宽字符折行"）。</param>
    /// <param name="csharpPath">C# 侧源码路径。</param>
    /// <param name="baselinePath">基准侧源码路径。</param>
    /// <param name="originalSpec">原始规范引用（如"ECMA-48 §5.4"/"Unicode UAX #11"），必填。</param>
    /// <param name="returnLineLimit">返回体行数上限，超出落盘只返摘要。</param>
    /// <returns>四问深度法调研提示词。</returns>
    public static string GetSurveyPrompt(
        string domain,
        string problem,
        string csharpPath,
        string baselinePath,
        string originalSpec,
        int returnLineLimit) {
        return $"""
调研 {domain} 的 {problem}。这是研究任务,不要写代码。

### 四问前置（答完才开工,每问 ≥3 条具体内容）
1. 专家隐性实践：本领域顶尖专家会做哪些用户没要求的事？
   （如跨终端实测、UCD 原始数据查证、模糊测试、并发形式化验证、基准 git blame 考古、量化收敛度量）
2. 原始素材清单：本项从哪些最原始的素材入手？
   （如 ECMA-48 标准、xterm 源码、Unicode UAX #11/#29、ClaudeCode PR/issue 原始讨论、真实终端 -dumpcsi 录制）
   禁止只看"已整理好的素材"（ts 源码现状/差异文档/AI 总结）
3. 具体方法与工具：本项用什么方法和工具？
   （如 script/tmux capture-pane 录序列、xxd 字节 diff、unicli 查 Unicode 属性、TLA+ 验并发、afl 模糊测试、gh pr view 拉设计意图）
4. 独立验证来源：除了 AI 和用户,什么能证明结果正确？
   （如终端模拟器渲染结果、Unicode UCD 测试套件、ClaudeCode 原始测试套件、跨终端 CI 矩阵、形式化证明、模糊测试覆盖率）

### 调研范围
- C# 侧：{csharpPath}
- 基准侧：{baselinePath}
- 原始规范：{originalSpec}（必填,禁止留空）

### 返回结构化报告（返回体上限 {returnLineLimit} 行,超出落盘只返摘要）
- 四问回答（每问 ≥3 条具体内容,禁止空泛原则）
- 差异清单：每项含
  ① 两侧 文件:行号
  ② 差异描述
  ③ 影响面
  ④ 原始素材引用（规范条款/标准文档/UCD 数据/PR 讨论 等）
  ⑤ 独立验证来源（终端模拟器/测试套件/CI 矩阵/形式化证明 等）
- bug 候选：每项含 基准行为证据 + C# 行为证据 + 原始规范条款（三证齐全才判 bug）
- 判据：自身注释契约违背才判 bug,否则归差异（参见 AGENTS.md 判定纪律）

### 禁止
- 禁止只看 ts 源码现状就下列异
- 禁止把"实现不同"判为 bug（必须有原始规范证据）
- 禁止 git commit（并行期间由主代理统一提交）
- 禁止删除文件
""";
    }
}
