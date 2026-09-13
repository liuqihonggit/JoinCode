# llm/agents 工程补全 XML 注释任务

## 任务目标
- 为 `llm/agents` 工程所有 public/internal 成员补全 XML 文档注释
- csproj 已配置 `GenerateDocumentationFile=true`（Debug 模式，llm/Directory.Build.props）
- 完成后在 agents 工程内移除 CS1591 抑制，让编译强制要求注释完整

## 分析结果（2026-09-14）

**总计**：126 个 cs 文件，101 个有缺漏，**662 处**需要补全 XML 注释

### 按目录分布
| 目录 | 文件数 | 缺漏处数 |
|------|--------|----------|
| Configuration | 1 | 12 |
| Coordinator | 44 | 336 |
| Core | 2 | 18 |
| DependencyInjection | 1 | 3 |
| Doctor | 16 | 121 |
| Output | 2 | 2 |
| Plugins | 1 | 1 |
| Services | 32 | 157 |
| ToolHandlers | 2 | 12 |

### Coordinator 子目录分布
| 子目录 | 缺漏处数 |
|--------|----------|
| Backend | 36 |
| Core | 143 |
| DualModel | 1 |
| Fork | 50 |
| Inheritance | 0 |
| Swarm | 60 |
| Team | 46 |

## 4 个子代理分工（均衡 ≈ 165 处/代理）

| 子代理 | 负责目录 | 预估处数 |
|--------|----------|----------|
| 1 | Coordinator/Core + Coordinator/DualModel + Coordinator/Backend | 180 |
| 2 | Coordinator/Swarm + Coordinator/Team + Coordinator/Fork | 156 |
| 3 | Services + Configuration | 169 |
| 4 | Doctor + Core + ToolHandlers + DependencyInjection + Output + Plugins | 157 |

## 规则
- 禁止删除已有注释，只补全缺失的
- XML 注释文本中尖括号必须转义为 `&lt;` `&gt;`
- 注释用中文，描述成员用途
- 并行期间禁止 git commit/push
- 每个子代理只编译自己负责的文件，不做全量测试

## 完成后
- 主代理统一编译 llm/agents 工程验证
- 在 agents csproj 覆盖 CS1591 抑制，强制注释完整
- 提交 git
