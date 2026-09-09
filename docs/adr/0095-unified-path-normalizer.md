# 0095. 统一路径归一化工具 PathNormalizer

- 状态：accepted
- 日期：2026-09-09
- 决策者：项目架构组

## 背景

全项目散落 4+ 个私有 NormalizePath 实现,行为不一致(Trim/TrimEnd 尾分隔符/try-catch/分隔符处理各异):
- FileWriter.NormalizePath:绝对/相对区分 + GetFullPath,不 TrimEnd 尾分隔符
- ThrottledFileService.NormalizePath:空检查 + GetFullPath,不 TrimEnd
- WorktreeLifecycleGuard.NormalizePath:Trim + GetFullPath + TrimEnd,try-catch 兜底(最完整)
- InMemoryFileSystem.NormalizePath:Replace 反斜杠为斜杠 + Trim 斜杠(转 / 分隔符)

不一致导致路径分隔符反斜杠/斜杠混用时处理失效 — 大小写守卫(PathCaseSensitiveGuard)依赖路径叶子名比对,尾分隔符使 Path.GetFileName 返回空,守卫检测失效。

## 决策

新建 PathNormalizer 静态工具(JoinCode.Abstractions.Utils),统一全项目路径归一化。

API:
- TrimTrailingSeparators(path):Trim + 去尾反斜杠/斜杠,不解析绝对路径
- GetLeafName(path):去尾分隔符 + Path.GetFileName(跨反斜杠/斜杠),纯字符串
- Normalize(path):Trim + Path.GetFullPath + 去尾分隔符,异常回退
- EqualsOrdinal(a, b) / EqualsIgnoreCase(a, b):归一化后 Ordinal / OrdinalIgnoreCase 比对

## 现状

- PathCaseSensitiveGuard 改用 PathNormalizer.GetLeafName(替私有方法)
- FileSystemRealPathResolver 改用 TrimTrailingSeparators 修尾斜杠 bug + _fs.GetFullPath(可 mock)
- 测试:PathNormalizerTests 16 用例 + PathCaseSensitiveGuardTests 混合分隔符 5 用例,全量 177 测试无回归

## 待迁移(渐进式)

散落的私有 NormalizePath 待逐步替换为 PathNormalizer.Normalize:
- FileWriter.NormalizePath(Infrastructure/IO/Services/FileOps)
- ThrottledFileService.NormalizePath(Infrastructure/IO/Services/FileOps)
- InMemoryFileSystem.NormalizePath(internal,转 / 分隔符,语义不同需评估)

迁移按 ADR 0007 渐进式,每处替换后编译+测试,不一次性全改。

## 替代方案

1. 各处继续私有实现:放弃。不一致是 bug 源(尾分隔符/大小写检测失效)。
2. 用第三方库:放弃。引入依赖,且项目已有 IFileSystem 抽象,不需再加。
3. PathNormalizer 做大小写归一化(返回小写):放弃。大小写归一化丢失真实大小写信息(大小写守卫正需要真实大小写比对),仅提供 EqualsIgnoreCase 比对工具。

## 后果

- 正面:全项目路径处理统一一致;分隔符反斜杠/斜杠混用可靠;大小写守卫检测不再因尾分隔符失效
- 负面:散落 NormalizePath 需渐进迁移;Normalize 依赖当前目录(相对路径),测试用绝对路径避免
- 中性:PathNormalizer 不做大小写归一化,仅 EqualsIgnoreCase 比对;InMemoryFileSystem 转 / 分隔符语义特殊,迁移需单独评估
