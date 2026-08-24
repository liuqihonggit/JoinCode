这是一份关于如何定义一个新的CLI（命令行工具）的完整架构指南，核心内容分为9个部分，最后还给出了CLI的定位总结：
 
1. 双模体验范式
- 面向开发者（DX）：提供彩色表格、TUI交互、交互式提示、OAuth跳转等友好体验。
- 面向Agent（AX）：输出纯JSON/NDJSON、零着色、不等待输入、支持API Key认证，还有 --dry-run 预演、 --yes 跳过确认等适配自动化的选项。
- 设计原则：检测到 NO_COLOR 或 APP_NO_TUI 时，自动降级为非交互模式。
2. 命令结构设计
- 命名规范：动词在前（create、list、delete）、短且好记、全局一致（ list 不混用 ls/get ）、避免和系统命令重名。
- 参数规则：位置参数放必需的ID，flag放可选的修饰项。
3. 输出契约
- stdout：只输出结构化数据，比如 {"ok": true, "data": {...}, "meta": {...}} 。
- stderr：只输出日志和提示信息。
- 退出码：用0表示成功，1表示参数错误，2表示认证失败，3表示资源未找到，4表示临时失败（可重试），5表示冲突（不可重试）。
4. 错误处理
- 结构化错误包含4个关键字段： code （机器可读）、 message （人类可读）、 hint （修复建议）、 retryable （是否可重试）。
- 采用Fail Fast策略：前置检查配置、Token、数据库，全部通过才执行核心逻辑，失败就立刻返回结构化错误。
5. 配置管理
- 四级优先级：命令行参数（Flag）> 环境变量（Env）> 配置文件（Config）> 默认值（Default）。
- 配置文件遵循XDG标准路径，同时明确密钥管理红线：禁止在shell历史中暴露API Key，推荐用环境变量 export MYCLI_API_KEY=xxx 的方式管理。
6. 安全设计
- 命令风险分级： read （只读，直接执行）、 write （修改，需确认）、 dangerous （不可逆，需复核）。
- 给Agent设置安全网：支持 --dry-run 先试跑，确认无误后再执行。
- 输入硬化：禁止路径穿越、控制字符、特殊字符注入，防范URL编码问题。
7. 可发现性
- 帮助信息分三层：Short（5-10词，动词开头）、Long（详细解释）、Example（3-5个可直接运行的示例）。
- 提供Schema自省功能，Agent可以动态查询参数定义、类型、权限。
- 维护 AGENTS.md 文档，记录常用工作流、不变量、注意事项和风险操作。
8. 技术选型建议
- Go：用Cobra + Viper，适合单文件分发、零依赖、跨平台场景。
- Rust：用Clap + Config.rs，适合高性能、内存安全、极致启动速度的场景。
- TypeScript：用Commander / Citty，适合生态丰富、快速原型开发。
- Python：用Click / Typer，适合数据处理、ML工具链场景。
- 选型原则：优先单文件分发、极少依赖、跨平台、启动速度<500ms。
9. 完整定义清单
- 输出契约：stdout只输出数据、核心命令支持 --json 、JSON采用 ok/data/meta 结构、 schema_version 存在。
- 交互：明确区分TTY/non-TTY、遵守 NO_COLOR 、非交互不等待输入、支持 --yes/--force 。
- 可发现性：分层 --help 、Short/Long/Example三层、支持shell补全、提供 mycli doctor 自检。
- 错误处理：退出码分层、 code/message/hint/retryable 字段、退出码在 --help 里文档化、Fail Fast前置检查。
- 安全： read/write/dangerous 分级、支持 --dry-run 、优先不验证命令行参数、输入硬化防幻觉。
- Agent： AGENTS.md 存在、Schema自省支持、 workflow gotchas 、Agent评测集。
 
最后总结：CLI正在变成可组合的文本协议，定义新CLI就是定义人机共用的文本协议，让命令结构成为能力地图、输出契约成为数据协议、错误语义成为决策依据、权限边界成为安全红线、帮助系统成为自描述能力、可组合性成为Unix哲学的回归。