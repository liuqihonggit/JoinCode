# 基准对比调研代理提示词模板 — 四问深度版

> **用途**：本文档记录"基准对比工作流（C# vs ClaudeCode）"的调研代理派发提示词模板，强制每项差异/bug 调研走"四问深度法"——追溯到原始规范源头 + 独立验证，而非只对比已整理好的素材。
>
> **来源**：AGENTS.md「基准对比工作流」章节深度化升级（用户指令"更深入的版本而不是更大的版本"）
>
> **创建时间**：2026-10-25
>
> **关联**：AGENTS.md 基准对比工作流章节、TASK048（提示词模板先例）、TASK067/TASK069（TUI 差异对比实战）

---

## 一、四问深度法（方法论）

### 为什么是"深度"而非"更大"

扩大调研域数（广度）只会堆叠更多"实现不同"的条目；深度法要求每项差异追溯到**原始规范源头**并用**独立验证来源**证伪，避免：
- 盲信基准（基准本身也可能有 bug）
- 只看"已整理好的素材"（ts 源码现状/AI 写的差异文档/AGENTS.md 规则）而非一手资料
- "感觉差不多"收尾而无量化收敛证据
- 把"实现不同"误判为 bug（缺原始规范证据）

### 四问（每项差异/bug 调研前必答，答完才开工）

| 问 | 内容 | 反例（答不上来的表现） |
|----|------|------------------------|
| 1 专家隐性实践 | 该领域顶尖专家会做哪些用户没要求的事？ | "对比两份代码"——专家还会跨终端实测、UCD 查证、模糊测试、基准考古 |
| 2 原始素材清单 | 本项从哪些最原始的素材入手？ | "看 ClaudeCode 的 ts 源码"——原始素材是 ECMA-48/xterm 源码/UAX#/UCD/PR 讨论 |
| 3 具体方法与工具 | 本项用什么方法和工具？ | "读代码对比"——还要 `script` 录序列、`xxd` 字节 diff、`unicli` 查属性、TLA+ 验并发 |
| 4 独立验证来源 | 除了 AI 和用户，什么能证明结果正确？ | "测试通过"——还要终端模拟器渲染、UCD 测试套件、跨终端 CI、形式化证明 |

> 四问答不出的差异项 = 调研不充分，归入"待补深度"池，禁止直接走用户决策

---

## 二、调研代理提示词模板

### 模板：基准对比调研（四问深度版）

```
调研 {域} 的 {具体问题}。这是研究任务,不要写代码。

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
- C# 侧：{C# 路径}
- 基准侧：{ts 路径}
- 原始规范：{ECMA-48/UAX#/xterm 源码/UCD 等,必填}

### 返回结构化报告（返回体上限 {N} 行,超出落盘只返摘要）
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
```

### 填充示例（TUI 键序列解析域）

```
调研 键序列解析域 的 CSI 泛化边界扫描与 kitty 协议解析差异。这是研究任务,不要写代码。

### 四问前置
1. 专家隐性实践：
   - 追溯 ECMA-48 §5.4 控制序列参数字节范围（0x30-0x3F）而非只看 ts 实现
   - 查 xterm charproc.c 的 CSI 处理边界（intermediate bytes 0x20-0x2F）
   - kitty 协议 GitHub issue/PR 原始讨论（CSI u 编码争议）
   - 跨终端实测：xterm/iTerm2/Alacritty/WezTerm 喂同序列 diff 解析
   - 模糊测试：afl 喂随机 CSI 序列找崩溃
2. 原始素材清单：
   - ECMA-48/ISO-IEC 6429 §5.4（控制函数参数与中间字节）
   - xterm 源码 charproc.c（CSI 处理黄金基准）
   - kitty 协议 spec + GitHub discussions（CSI u 设计意图）
   - ClaudeCode KeyPressParser.ts 的 git blame（每个分支为什么加）
   - 真实终端 `script` 录制喂异常序列的原始字节
3. 具体方法与工具：
   - `script`/`tmux capture-pane -p` 录控制序列
   - `xxd`/`hexdump -C` 字节级 diff
   - `afl`/`honggfuzz` 模糊测试键序列解析器
   - `gh pr view`/`gh issue view` 拉基准设计讨论
   - Python `pty` 驱动跨终端对比
4. 独立验证来源：
   - xterm/Alacritty 渲染结果作为 ground truth
   - ECMA-48 conformance test suite
   - ClaudeCode 原始测试套件移植
   - 模糊测试 24h 无崩溃覆盖率报告
   - 跨终端 CI 矩阵

### 调研范围
- C# 侧：native_tui/src/NativeTui.Core/terminal/KeyPressParser.cs
- 基准侧：D:\project\claude-code-rust\claude-code-rev-main\src\...\useTextInput.ts
- 原始规范：ECMA-48 §5.4 + xterm charproc.c + kitty protocol spec

### 返回结构化报告（返回体上限 200 行,超出落盘只返摘要）
...（同模板）
```

---

## 三、原始素材速查表

| 领域 | 原始素材 | 获取方式 |
|------|----------|----------|
| 终端控制序列 | ECMA-48/ISO-IEC 6429 | ISO 官方 PDF |
| 终端行为基准 | xterm 源码 charproc.c/screen.c | git clone freedesktop/xterm |
| VT 设计意图 | DEC VT220/VT320 Programmer Reference Manual | web archive |
| 字符宽度 | Unicode UAX #11 + EastAsianWidth.txt | unicode.org |
| 文本分段 | Unicode UAX #29 + GraphemeBreakProperty.txt | unicode.org |
| emoji 属性 | Unicode UAX #44 + emoji-data.txt | unicode.org |
| kitty 键盘协议 | kitty GitHub spec + discussions | gh api / webfetch |
| 基准设计意图 | ClaudeCode git blame + PR/issue | `git blame` / `gh pr view` |
| 基准验收标准 | ClaudeCode 测试套件原文件 | 直接读 ts 测试 |
| 真实 LLM 流 | Claude API 原始 SSE 字节流 | mitmproxy / curl -N |
| 真实终端录制 | 控制序列原始字节 | `script` / `xterm -log` |
| 用户键位录制 | 输入字节流 | `cat \| xxd` / `tmux capture-pane -p` |
| 终端 conformance | vtest/tmt/pyte 测试向量 | 各工具仓库 |
| 本仓设计决策 | git log + commit message 原文 | `git log -p` / `git blame` |

---

## 四、独立验证来源速查表

| 来源 | 证明什么 | 获取方式 |
|------|----------|----------|
| 终端模拟器渲染结果 | 控制序列真实行为 ground truth | xterm/Alacritty/WezTerm 实跑 |
| Unicode UCD 测试套件 | 字符属性/分段算法正确性 | UnicodeTest 官方向量 |
| ECMA-48 conformance suite | 终端控制序列符合性 | vtest/tmt |
| ClaudeCode 原始测试套件 | 基准行为移植验收 | 跑基准测试（移植到 C#） |
| 跨终端 CI 矩阵 | 跨环境回归 | 6 终端 × 3 OS |
| 形式化验证证明 | 并发无死锁/不变量 | TLA+ TLC / Alloy |
| 模糊测试覆盖率 | 健壮性证据 | afl 24h 无崩溃 |
| git bisect | 回归引入点 | 二分历史 |
| 类型检查器/分析器 | 静态性质 | C# 编译器 + Roslyn 分析器 |
| 性能基准线 | 体感对齐 | hyperfine + dotnet-trace |
| 第三方代码评审 | 独立人判断 | 非 AI 非用户的 reviewer |
| 时间检验 | 决策耐久性 | 一段时间后回看 |

---

## 五、差异文档验收表（深度版强制列）

每项差异/bug 的验收表必须含后两列（AGENTS.md 强制钩子）：

| 差异项 | 两侧 文件:行号 | 差异描述 | 影响面 | 原始素材引用 | 独立验证来源 + 验证结果 |
|--------|----------------|----------|--------|--------------|------------------------|
| 示例 | C# KeyPressParser.cs:120 vs ts useTextInput.ts:257 | CSI 参数字节范围扫描差异 | 异常序列解析泄漏 | ECMA-48 §5.4 参数字节 0x30-0x3F | xterm charproc.c 实跑 + ECMA-48 conformance 通过 ✅ |

> 缺"原始素材引用"或"独立验证来源"列 = 调研不充分，退回重做

---

## 六、与 TASK048 的关系

TASK048 是"基建确定性测试补充"的提示词模板（检测+执行两阶段），本文档是"基准对比调研"的提示词模板（四问深度法），两者：
- **形态相同**：都是可复用的子代理派发模板
- **领域不同**：TASK048 面向单仓测试补充，本文档面向跨仓基准对比
- **互补**：基准对比发现差异后，若需补测试可接 TASK048 执行阶段模板

<!-- 🤖 Auto Decision: 2026-10-25 -->
<!-- 决策: 四问深度法作为基准对比调研的前置强制步骤,而非可选建议 -->
<!-- 原因: 用户明确要求"更深入的版本而不是更大的版本"——广度堆叠条目无意义,深度追溯原始规范+独立验证才能避免盲信基准和误判 -->
<!-- 替代方案: 把四问作为软建议(可跳过)——放弃,因为软建议等于没有,调研代理会回到只看 ts 源码的老路 -->
<!-- 验证: AGENTS.md 章节改写 + 本模板文档落地,待首次实战调研验证四问可答性 -->
