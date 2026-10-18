# TASK053 — DSH 像素对齐对比校验体系任务清单

> 状态: in_progress
> 日期: 2026-10-17
> 设计: [DSG042](../design/DSG042-DSH像素级对齐对比校验体系.md) · 路线: C（校验先行，Avalonia 复刻 + WebView2 spike 并行评估）
> 工作流: 每任务 红测试 → 实现 → 编译 → 绿测试 → 文档 → git 提交 → 差评大师 → 修复
> 脚本类任务豁免单元测试（非交付品工具），验收方式 = 真实执行产出 golden 产物（对齐 ADR 0080 精神）

---

## P0 — 固化 capture 原型为正式卫星工具 ✅

- [x] P0-1 创建 `non_deliverables_tools/dsh_parity/` 卫星目录 + package.json（锁 `@deepseek-ai/dsh@0.2.0-rc.2` + playwright-core）
- [x] P0-2 `capture/capture-dsh.mjs`：CDP 截图 + DOM 盒树 + 双主题 token 提取（Edge 路径自动探测，参数化视口）
- [x] P0-3 `capture/capture-all.ps1`：启动 dsh(--no-open, DSH_HOME 隔离) → 捕获 → 按 PID 击杀（防残留）
- [x] P0-4 `.gitignore`（node_modules/.dsh-home/.tmp）+ README（运行方式）
- [x] P0-5 真实执行验证：新位置跑通全流程，golden 入库 `reference/`
- [x] P0-6 提交

## P1 — 场景矩阵扩展

- [ ] P1-1 对话框场景（permission/question）捕获脚本
- [ ] P1-2 设置面板场景
- [ ] P1-3 对话中场景（注入样例数据）
- [ ] P1-4 斜杠面板场景
- [ ] P1-5 场景定义文件 `scenarios.json` 驱动

### P1-6 交互态场景（hover/active/focus）✅ 首页完成

- [x] P1-6a `capture/state-targets.mjs` 映射唯一数据源（18 目标 DSH 选择器 ↔ 本地选择器）
- [x] P1-6b `capture/capture-states-dsh.mjs`：hover/active/focus 计算样式 golden → `reference/dsh-states.json`（`capture-all.ps1 -Script ... -DshHomeDir .dsh-home-states` 独立 home）
- [x] P1-6c `compare/verify-states.mjs`：本地对照 golden，DSH 变化项逐一必对，退出码门禁（PASS 0 不一致 / 0 警告）
- [x] P1-6d webui 落地：按钮 hover/active=interactive-bg-hover、corner active=interactive-bg-active、new-session→bg-layer-3、composer placeholder 拆分（pointer-events:none, caption 色）、输入 caret=button-info-fill、send 空输入 disabled
- [x] P1-6e 基帧回归：L3 diff 与 S1 基线一致（meanDelta 1.793, t12 1.36%）
- [x] P1-6f 实测结论：DSH 按钮无 :focus 样式；addBtn/brand 无 hover；placeholder 需经父元素 hover（pointer-events:none）
- [ ] P1-6g 其余页面（设置/对话/斜杠面板）交互态随场景矩阵补采

## P2 — L1 token 对等（CI 快）

- [ ] P2-1 红测试：token 对等测试骨架（缺映射即红）
- [ ] P2-2 `ui-parity-map.json` 初始化（dsw token → 本项目资源键）
- [ ] P2-3 token 生成器（dsh-tokens.json → C# 资源数据，禁手抄）
- [ ] P2-4 GuiPalette 切换到生成数据（Avalonia 路线首个落地）
- [ ] P2-5 绿测试 + 提交

## P3 — L2 几何对等

- [ ] P3-1 VisualTree 盒树 dump（与 dsh-dom-*.json 同构）
- [ ] P3-2 区域映射表（DSH 选择器 ↔ Avalonia 控件路径）
- [ ] P3-3 几何断言（±1px / 字号 / 颜色）+ diff-report.json

## P4 — L3 像素对等

- [ ] P4-1 帧 diff 引擎（文本掩膜 + 非文本区比例 + SSIM）
- [ ] P4-2 热力图输出 + 差异白名单
- [ ] P4-3 双主题 × 场景矩阵报告

## P5 — 双路线评估（已决策：B2 主线）

- [x] P5-1 WebView2 + NativeAOT 卫星 spike → 移至 [TASK054](TASK054-WebView2自研WebUI-spike任务清单.md)（用户选定 B2 后成为关键路径）
- [x] P5-2 路线决策（用户确认）：**B2 WebView2+自研 Web UI**（[ADR 0138](../../../../docs/implemented/adr/architecture/0138-gui-webview2-webui-rendering.md) accepted）；A 降为回退

## P6 — 按区域对齐重构（依 P5 决策：WebUI 区域；S2/S3 已通过，启动窗口开放）

- [x] P6-0a 产品 GUI 接入 WebView2 宿主（四件套落地）：TFM 平台化 `net10.0-windows10.0.26100.0` + `WebView2EnableCsWinRTProjection` + x64 + app.manifest 免注册 WinRT；`webui/` 静态资源随包分发；`--webui` 开关（默认 MainWindow 不变），`--webui-smoke` 冒烟（READY + NAVIGATED 双标记，退出码 0/2/3）
  - 验证：Debug 全绿（0 警告 0 错误）；GUI 测试套件 568 通过/0 失败；真机冒烟 `WEBVIEW2-READY runtime=155.0.4283.62` + `WEBUI-NAVIGATED success=True`，退出码 0
  - 附：测试工程 JCC9001 预存编译失败（HEAD 即红）已按诊断产物豁免惯例修复（6 文件 pragma + 豁免理由）
- [x] P6-0b 桥接协议（[ADR 0141](../../../../docs/implemented/adr/architecture/0141-webui-bridge-protocol.md) accepted）：类型化判别信封（type + 内联字段，`[JsonPolymorphic]` + `BridgeJsonContext` 源生成，AOT 安全）；首版 10 消息类型（web/ready·chat/send·permission/response·smoke/pong ⇄ app/init·chat/delta·chat/done·permission/request·smoke/ping·app/error）；web/ready 握手 + app/init 应答；未知消息 fail-closed + app/error 诱导；`webui/js/bridge.js` 薄层（jccBridge 零依赖）
  - 验证：单测 23 绿（全类型 roundtrip/判别字段/未知 type/非法 JSON）；E2E 红→绿（无 bridge.js 退出码 4 超时 → 实现后退出码 0，`BRIDGE web/ready` + `BRIDGE-SMOKE ok nonce=...` ~100ms）；全量 GUI 591 通过；L3 基帧 meanDelta 1.7927 无漂移 + 交互态 0 不一致
- [x] P6-1 侧栏（工作区/会话列表）：session-row 几何修复（`margin-top: 2px`，对齐 DSH `margin 2px 0 0`）；行交互态补齐（project-row/session-row hover/active=interactive-bg-hover + `cursor: pointer`，golden 重捕获实证 base/hover/active）；搜索 span→button（对齐 DSH searchButton 语义，选择器随更新）；真实会话数据链路（`sessions/list` 下发 GuiSessionStore 真实会话 + `session/select` 点击回传 + `web/sessions-rendered` 渲染 ACK 冒烟钩子）
  - 验证：桥接单测 29 绿（含 3 新消息 roundtrip，集合字段逐项比较）；E2E 红→绿（宿主先发 `sessions/list count=26` 无应答超时退出码 4 → bridge.js 渲染后退出码 0 `sessions-rendered count=26` + pong ~60ms）；全量 GUI 597 通过/0 失败；交互态 20 目标 PASS（0 不一致）；L3 meanDelta 1.7927 → 1.7799（t4 1.529→1.454%），侧栏行区 diff 1472→1270
- [x] P6-1b 会话流端到端（v1）：`chat/history` 消息（+`ChatHistoryEntry`，消息类型 13→14）；`WebChatFlow` 投影层（直连 `IJccChatSession.StreamAsync`，并发/未就绪 guard→app/error，异常→app/error+done，取消静默收尾）；WebUiWindow 引擎接线（`JccChatSession.CreateAsync` 后台组装+Dispatcher 热接、权限 fail-closed `DenyPermissionAsync`、transcript 注入会话存储）+ `OpenSessionAsync`（`GuiSessionStore.LoadAsync`→chat/history 渲染 + `SwitchSession`+`LoadHistoryAsync` 灌引擎）；web 侧 textarea+覆盖层 placeholder、消息区（`.msg-user`/`.msg-assistant` + `has-messages` 移底）、bridge.js 状态机（busy 防重入、delta 累积、done 复位、history 整体替换、会话点击先清空）
  - 验证：单测 35 绿（协议 31 + flow 4，ScriptedSession 假会话含门控/异常注入）+ 全量 GUI **603 通过/0 失败/3 跳过**；真机冒烟退出码 0（`sessions-rendered count=26` + pong）；交互态 20 目标 PASS；L3 meanDelta 1.7799 零漂移（placeholder 覆盖层保 golden 目标位置不变）
  - 真机手动验证（CDP 驱动真实 exe，非 mock）：26 真实会话行渲染✓、发送→用户消息渲染✓、真实引擎调用异常链路✓（agnes 上游 400 → app/error 引导文案 + done 复位，CLI 同环境同错证环境级）、会话切换→chat/history 替换✓；⚠️ 前排：LLM 成功流（delta 渲染）被上游阻断无法真机实证（agnes 400 / deepseek 401 / sensenova 空响应，CLI 同样失败），delta 路径由单测覆盖，待上游恢复后按 `dumps/manual-webui-flow.mjs` 复验
  - 差评大师修复（S/M/L 全处理）：S1 窗口关闭释放引擎（防 HTTP 连接池/FW/后台任务泄漏致进程不退）；S2 流式期间拒绝切换会话（`_flow.IsStreaming` 守卫，防 transcript 差量写错会话）+ 连点序号丢弃过期请求 + JS 流中忽略行点击；M1 关闭后迟到引擎即刻释放（`IsCancellationRequested` 守卫）；M2 令牌未取消的 OCE→回"响应超时"错误（原一刀切静默吞 HttpClient 超时）；M3 app/error 复位语义收敛；M4 选区跨重渲染保持；L1 会话 ID 白名单（防越目录）；L3 空列表回填占位行；L4 transcript 缺失回退引擎内存（对齐 MainViewModel）
  - 差评修复验证：单测 **608 通过/0 失败**（+5：超时 OCE/取消 OCE/纯 Thinking/空历史 roundtrip）；冒烟退出码 0（count=30）；交互态 20 目标 PASS；L3 meanDelta 1.779929012345679 比特级零漂移
  - 附：新基线（#426）引入 mcp `GitHubToolHandlers.Run.cs` JCC1009 三层 if 嵌套阻断 Mcp→GUI 编译链，已按语义等价卫语句拍平解阻
- [x] P6-2 composer（输入区）：29 元素 DOM 规格逐项收敛（侦察产物 `dumps/dom-dump-web.mjs` 我方几何转储 vs DSH `reference/dsh-dom-1440x900.json`）
  - 修复：row 内距 `0 8px 8px`→`2px 8px 6px`（tools y518→520 精确）；mode-chip `122×17 br0 pad 0 4`→`126×28 br8 pad 0 4px 0 8px lh20`（x517.3 y520 精确）；model-chip `166.2`→`180.2×28`（trailing 有效间隙 24px = DSH 双层 gap 叠加，x997.5 精确）；chevron svg 12→14（mode/model/工作区 chip2）；send-btn y516→y515 精确（`align-self:flex-start` + `margin-top:-2px` 复刻 DSH 实测微上移，trailing `align-self:stretch` 回到 y517 h34）；工作区 chip 行高 17→20（label y411 精确）+ chip2 宽 104→106
  - 白名单（已接受）：input 宽 Δ4（DSH 770.4 含 4px 透明滚动槽，不可见）；placeholder 覆盖层盒形（文本原点 483.3,455 一致）
  - 验证：DOM 转储逐元素与 DSH 表一致（除白名单）；交互态 20 目标 PASS（0 不一致）；L3 meanDelta **1.7799→1.4693**（t4 1.454→1.304 / t12 1.284→1.144 / t24 1.215→1.078）；真机冒烟退出码 0（sessions/list count=30 + sessions-rendered）；纯 CSS/HTML 变更无 C# 面，测试基线 608 不变
- [ ] P6-2b 输入多行自动增高（首版 textarea 定高 52 + `overflow: hidden`；DSH 输入区随行数增高，需捕获 DSH 多行态 golden 后对齐）
- [x] P6-3 对话消息区：3 轮夹具 DSH 深度捕获（dom 377 元素 / html / styles / fullstyles 15 选择器全 longhand）→ 规格逐项落地
  - 规格要点（DSH 实测）：流节奏 user+6 / divider·assistant+16 / 页脚紧贴 assistant 后（独立 flowItem margin 12 + 根高 32，图标 +16）；列 742.4 居中（滚动条 5px + 右移 2px，x=485.3）；气泡 bg-layer-2 br20 pad `10px 16px` fs14/lh22 pre-wrap；分隔行 border-bottom=border-l2 h33 pad-bottom 8「已完成，用时 N秒」；markdown p bottom16 / ol·ul 16 0 / li+li 6 / 行内码 border-l1；代码块 bannerWrap=bg-base #151517 42 + header 24 + pre 45；消息态 composer input 36 → 卡 98（底 870）
  - 实现：`ChatHistoryEntry` +Timestamp（协议 14 类型不变，宿主 `ToLocalWallClock`，Web 侧本地 HH:mm）；bridge.js 全量渲染器（6 图标 svg 精确路径、renderMarkdown 段落/列表/行内码/粗体/围栏块、代码块换行切换+复制、用户/分隔/助手三类构建器、time/duration、chat delta/done/history 接线）；app.css 消息区整段；index.html `msg-column` + 主题引导脚本；tokens.css 生成器升双通道（`@media` 兜底 `:not([light])` + `[data-jcc-theme=dark]` 权威）；枚举生成器支持嵌套枚举 + GUI 接入 analyzer（GuiThemeVariant.ToValue）
  - 验证：单测 **609 通过/0 失败/3 跳过**（+1 Timestamp roundtrip）；真机 CDP（夹具 home + `--webui` + 9222）：3 user+3 assistant+3 divider+1 代码块渲染 ✓；DOM 47 配对项 **Δ(0,0,0,0) 全 PASS**（column/userRow/assistant/divider/footerActions/timeEnd/p/li/strong，内容高 1160==DSH）；样式抽查 bubble #2c2c2e / inlineCode #292929 / codeBlock #1b1b1c / divider 白线全 PASS；L3 meanDelta **1.56**（热区=已知缺口：头区 26px/轮次统计行/滚动条 overlay，消息核心区 ≤1.7）；冒烟退出码 0
  - 留待：对话头区（DSH 76px）与轮次统计行「N 轮 N 步」归 P6-3b；delta 成功流真机复验仍被上游阻断（LG 同前）
- [ ] P6-3b 对话头区（76px：标题+tabs）+ 轮次统计行「N 轮 N 步」
- [ ] P6-4 设置面板
- [ ] P6-5 对话框（权限/询问/确认）
- [ ] P6-6 CI 集成（L1/L2 入 CI）+ 验收记录

---

## 备注

- 参考物版本锚定 `0.2.0-rc.2`；升级需显式重捕获 + diff 审计（防参考物漂移）
- golden 产物入库 `reference/`（PNG + JSON，KB 级）；瞬时产物写 `dumps/`（gitignore）
- 卡点教训：`msedge --headless --screenshot --virtual-time-budget` 对 WebSocket 长连接页面永久挂起，必须用 CDP 主动截屏

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: P6-0a 采用 `--webui` 增量开关接入（默认仍走原 MainWindow），WebUiWindow 独立成类 -->
<!-- 原因: 渲染路线切换属高风险面；开关式接入让原 Dock 路径零回归，验证通过后 P6-1 再收敛单窗口；P6 区域对齐按窗口分类推进互不阻塞 -->
<!-- 替代方案: ① 直接切换默认渲染（回归面过大，弃）；② MainWindow 内嵌 WebView2Host（侵入 Dock 布局，P6-1 区域化时再评估） -->
<!-- 验证: GUI 编译 0 警告 0 错误；测试 568 通过/0 失败；真机冒烟 READY+NAVIGATED 退出码 0 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 根 props 三处 TFM 条件放宽为 StartsWith('net10.0') + WebView2/CsWinRT 版本号上收统一 props；spike 补 ExcludeStructura -->
<!-- 原因: 四件套要求 GUI/测试/spike 均为 windows TFM，原精确匹配 'net10.0' 会让这三类工程漏配 Structura/分析器；版本号唯一定义防两处漂移 -->
<!-- 替代方案: 逐工程手工挂引用（重复三份且日后易漂移，弃） -->
<!-- 验证: GUI/测试/spike 三工程编译全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 测试工程 JCC9001 36 处预存编译失败按"诊断产物豁免"修复（pragma + 豁免理由），不引入 IFileSystem -->
<!-- 原因: 违规点为帧图落盘（dumps 约定）/仓库根定位/临时目录夹具，均为测试基础设施且需真实文件系统语义；项目已有 JCC3010/JCC3013 同款豁免惯例 -->
<!-- 替代方案: InMemoryFileSystem 替换（帧图产物即真实文件诉求，InMemory 语义错误，弃） -->
<!-- 验证: HEAD 复现 36 错误（预存红）→ 修复后 0 警告 0 错误，568 测试全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: composer 几何以 DSH 实测"有效值"复刻（trailing 扁平化取间隙和 24px、send margin-top -2px 上移、chip br8 透明底） -->
<!-- 原因: DSH DOM 含 standardControls 中间层与视觉微调（send 比内容框高 2px），照搬节点结构会引入无消费者包装层；我方 DOM 扁平，以 getBoundingClientRect 逐元素等效收敛，L3 差可证 -->
<!-- 替代方案: ① 逐层复刻 DSH DOM 节点（多 2 层包装无消费者，弃）；② 近似对齐忽略 2px 微调（composer 区 L3 残留 diff，弃） -->
<!-- 验证: DOM 转储逐元素一致（除白名单 input 宽 4px 透明槽）+ 交互态 PASS + L3 1.7799→1.4693 + 冒烟退出码 0 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 桥接协议用"类型判别信封（type + 内联字段）"而非 JSON-RPC/嵌套 payload -->
<!-- 原因: [JsonPolymorphic]+源生成在 AOT 下零反射直出，record 结构相等让 roundtrip 单测极简；JSON-RPC 的 params 弱类型违反 ADR 0133 禁动态解析 -->
<!-- 替代方案: ① JSON-RPC 2.0（params=JsonElement 动态，弃）；② AddHostObjectToScript（AOT 不可用，spike 已证） -->
<!-- 验证: 23 单测绿 + 真机冒烟退出码 0 + 全量 591 绿 + 像素零漂移 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 冒烟契约含 web/ready 握手（非仅 ping/pong），退出码新增 4=桥接超时/nonce 不符 -->
<!-- 原因: 页面脚本先于 NAVIGATED 执行（实测 ready 早 3ms），若 host 订阅晚于页面就绪会丢信号；握手把"页面就绪"显式化为协议事件，看门狗兜底 -->
<!-- 替代方案: 仅导航完成即发 ping（时序竞态：订阅前消息丢失，弃）；轮询 ExecuteScript 探测（绕协议，弃） -->
<!-- 验证: E2E 红（无 bridge.js → 退出码 4）→ 绿（握手 + nonce 往返 ~100ms 退出码 0）✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: P6-0b 收尾时 ADR 0141 先以 proposed 写入 docs/proposed/adr/architecture/ 随实现验证后迁 implemented 并改 accepted -->
<!-- 原因: 遵循 AGENTS "新架构决策必须先写 ADR 再实现 + 实现后改状态" 工作流；编号取 0141 避开 main 上 TASK053 文本预占的 0140（SSRF fail-closed） -->
<!-- 替代方案: 直接用 0140（与并行分支预占冲突风险，弃） -->
<!-- 验证: ADR 迁移 + docs/README 计数同步（152→153 / 9→8 / 125→126）✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: P6-1 侧栏会话列表接真实 GuiSessionStore 数据（新增 sessions/list·session/select·web/sessions-rendered 三消息），冒烟契约扩展渲染 ACK -->
<!-- 原因: 静态占位行无法验收"列表真能渲染"；web/sessions-rendered ACK 把"协议通但渲染断"变成冒烟可机检（超时并入退出码 4）；空列表保留静态"新会话"行=全新状态语义 -->
<!-- 替代方案: ① mock 静态行（假数据不可验收，弃）；② 仅截图比对（冒烟无渲染断链机检，弃）；③ 独立 HTTP API 拉会话（绕桥接协议双通道，弃） -->
<!-- 验证: E2E 红（26 条真实会话发出无 ACK → 退出码 4）→ 绿（count=26 ACK + pong 退出码 0）✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 行交互态先重捕获 DSH golden（state-targets 扩 projectRow/sessionRow）再对齐 CSS，不凭惯例直写 -->
<!-- 原因: sessionRow 的 selected 态 bg 与 hover 态相同（0.08），凭猜测会掩盖真实差异；golden 实证 projectRow hover/active=0.08 + cursor:pointer，searchBtn 态经 span→button 后保持 -->
<!-- 替代方案: 按按钮惯例直接加 hover（若 DSH 行无 hover 则引入假差异，弃） -->
<!-- 验证: 重捕获 20 目标 → 本地 verify 红（projectRow hover/active 2 处）→ 补 CSS 后 PASS（0 不一致）✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: WebUI 会话流用新薄投影层 WebChatFlow 直连 IJccChatSession.StreamAsync，不复用 MainViewModel -->
<!-- 原因: MainViewModel 绑定 Avalonia/Dock 概念与 ObservableCollection，属 GUI 视图层；WebUI 只需"流事件→桥接消息"纯投影，复用会把 Avalonia 依赖泄漏进 webview 层且带出无关状态机 -->
<!-- 替代方案: ① MainViewModel 接入（依赖泄漏 + 破坏 webview 层纯度，弃）；② bridge.js 直接解析 ChatStreamEvent（协议双份实现，弃） -->
<!-- 验证: 4 单测绿（映射顺序/空与 Thinking 跳过/异常 path/并发与未就绪 guard）+ 真机错误链路 app/error+done ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 打开会话顺序 = 先 chat/history 渲染 UI、再 SwitchSession+LoadHistoryAsync 灌引擎；窗口级 CancellationTokenSource 统一取消在途发送 |
<!-- 原因: history 先渲染让 UI 立即反馈（引擎历史加载可能较慢）；JCC3001 要求即发即忘传令牌，窗口关闭时取消避免 Dispose 后触碰宿主（WebView2Host.Send 空判兜底） |
<!-- 替代方案: ① 先灌引擎再渲染（UI 延迟感知差，弃）；② 每消息独立 CTS（生命周期管理冗余，弃） -->
<!-- 验证: 真机切换会话 chat/history 替换 ✓ + 冒烟/单测全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 真机手动验证走 WebView2 远程调试端口（WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222）+ playwright connectOverCDP 驱动真实页面（脚本入 dumps/） -->
<!-- 原因: GUI 无可机读自动化入口；CDP 可执行"输入→发送→观察渲染/切换会话"全链路且对生产 exe 零侵入（仅调试期环境变量） -->
<!-- 替代方案: ① 肉眼截图（不可复跑/无断言，弃）；② 页面内注入脚本直调 jccBridge（绕真实输入事件，弃） -->
<!-- 验证: 26 会话渲染/发送→用户消息/真实引擎异常引导/会话切换全通 ✓（delta 成功流待上游可用复验）✅ -->


<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 会话流 v1 差评修复全收（S1/S2/M1-M4/L1/L3/L4）：关闭即释放引擎、流中拒切换会话、连点序号、OCE 区分取消与超时、选区保持、ID 白名单 -->
<!-- 原因: S1 引擎不释放致 HTTP 连接池/FW 泄漏（对照 MainViewModel.DisposeAsync 先例）；S2 流中 SwitchSession 使 transcript 差量写错会话（数据完整性）；M2 一刀切静默吞掉 HttpClient 超时会误导用户 -->
<!-- 替代方案: ① 流中切会话改为取消本轮流（改变引擎行为面大，留待 P6-5 会话管理评估，弃）；② app/error 加归属字段区分本轮（协议扩容，当前守卫已使路径不可从 UI 到达，暂缓） -->
<!-- 验证: 608 通过/0 失败（+5 路径测试）+ 冒烟 0 + 交互态 PASS + L3 比特级零漂移 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: WebUI 主题以宿主 app/init 下发的 `data-jcc-theme` 属性为权威通道，media 查询降为兜底（tokens.css 生成器升双通道），WebView2 Profile.PreferredColorScheme 仅作提示 -->
<!-- 原因: 真机实证 runtime 155 下 Profile.PreferredColorScheme=Dark 后页面 prefers-color-scheme 仍 false（官方 issue #5392 同款：142-155 期间该属性时有不生效）；属性通道可自证可控，且 OS 暗色下也能强制亮色（反向场景同被记录） -->
<!-- 替代方案: ① 仅靠 Profile.PreferredColorScheme（不可靠，弃）；② JS 全量重设 CSS 变量（与 tokens.css 双份定义，弃） -->
<!-- 验证: 真机 bubble #2c2c2e / inlineCode #292929 / codeBlock #1b1b1c / divider 白线全 PASS（pressDark=false 下属性通道生效）✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: enum_metadata 生成器扩展扫描嵌套枚举 + GUI 项目接入 analyzer（GuiThemeVariant.ToValue 供 app/init 下发 lowercase 主题值） -->
<!-- 原因: 主题值须来自 [EnumValue] 唯一数据源（规则6 禁重复字符串）；生成器原仅扫命名空间直下枚举，漏嵌套；全仓扫描证实仅 GuiPalette.GuiThemeVariant 一个嵌套 [EnumValue] 枚举（其余命中均为字符串样例假阳性），风险可控 -->
<!-- 替代方案: ① WebUiWindow 手写 switch 映射字符串（重复数据源，弃）；② 枚举提为顶层类型（100+ 引用/测试改动，弃） -->
<!-- 验证: 生成 GuiThemeVariantExtensions.g.cs + GUI 编译 0 警告 0 错误 + 609 测试全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 助手页脚实现为独立流项 `.assistant-footer`（margin-top 12 + pad-top 4 + 盒高 32 包 h28 动作行，内层 margin-left -6），markdown 末元素底边距裁剪（`:last-child { margin-bottom:0 }`） -->
<!-- 原因: DSH 几何实证页脚是独立 flowItem（TS9iAW_root h32 @ assistant底+12，图标 +16；assistant 流项高度不含页脚）；入流项后下一回合 rhythm 6/16 自动正确；compare 的 Δx -6 由内层 -6 承担（DSH .xzv4MW_actions 同款） -->
<!-- 替代方案: 单层 .assistant-actions 盒（-6 溢出使配对盒 x/w 漂移 Δ(-6,6)，弃） -->
<!-- 验证: footerActions[0..2] + timeEnd[0..2] Δ(0,0,0,0)；assistant[0..2] h 304/167/24 精确 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 代码块下边距按 DSH 几何实证取 16px（覆盖全单探针读到的 11px），margin: 16px 0 -->
<!-- 原因: 几何为准绳：DSH p4 局部 795 = block 底 779 + 16；单探针 11px 疑为 DSH 某中间层读数（若按其实现 assistant h 162≠167 直接反证） -->
<!-- 替代方案: 保留 11px（assistant[1] h -5、后 8 项连续 -5 漂移，弃） -->
<!-- 验证: assistant[1] 162→167 精确 + p[4]/p[5]/strong/divider[2] 全 Δ0 ✅ -->
