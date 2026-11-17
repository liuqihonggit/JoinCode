# TASK085 — gui profile 装配归一：第二套实现消失，gui 路径首次真机验证

> 状态：已完成（2026-11-18 真机验证通过并提交）
> 起点：TASK082 第三十轮把「特性排序」改成「注册名 + YAML」（`resources/app/modules.yaml`），
> 但只覆盖了 CLI —— gui 那条装配路径仍是**手写 YAML 解析 + 手写 switch**，并且从未被真机启动过。

## 一、为什么要做

改造后的 `resources/app/modules.yaml` 声明了两个 profile，但只有一半被消费：

| 宿主 | 装配方式 | 谁生成 |
|---|---|---|
| CLI（程序集 `jcc`） | `AppModuleCatalog.Create("cli")` | `AppModule.Generator` 源码生成器 |
| GUI/TUI（程序集 `JoinCode.Engine`） | `EngineSessionFactory.LoadGuiModulesAsync()` | **手写** |

引擎侧那 38 行手写代码自己解析 YAML、自己维护「注册名 → 实现类型」的 switch，
与生成器是**同一决策的两个实现**。后果：

1. **拼错模块名只在运行时炸**。生成器路径下 `node.yaml` 写错名字是编译期错误；
   gui 路径写错名字要等到 GUI 宿主启动时才抛异常，而 GUI 宿主（native_gui / native_tui）
   日常 CI 根本碰不到 —— 这个盲区可以一直挂着。
2. **加模块要改两处**。YAML 登记 + 手写 switch 各加一行，漏一处就是静默不生效。
3. **gui 路径从未被真机验证过**。本轮之前，没有任何一次真实启动过 gui 引擎会话。

## 二、方案

### 2.1 profile 声明归属程序集

CLI 引用引擎，于是同一个编译单元里既有本程序集的模块、也有引用来的模块。
生成器若不知道「哪个 profile 归本程序集管」，就分不清「这个名字属于另一个程序集」与「这个名字打错了」。

```yaml
profiles:
  cli:
    assembly: jcc              # 与 MSBuild 的 AssemblyName 一致
    modules: [...]
  gui:
    assembly: JoinCode.Engine
    modules: [...]
```

生成器用 `compilation.AssemblyName` 自知归属，于是：

- **归属自己的 profile**：每个名字必须能解析，否则 JCCGEN002 编译期报错（拼错即报）；
- **本程序集的模块**：必须登记在归属自己的 profile 里，否则 JCCGEN002（新增模块忘登记即报）；
- **别人家的 profile**：跳过，不校验也不生成。

### 2.2 每个 profile 一个类型

两个程序集各自生成同名 `AppModuleCatalog` → 被同时引用的程序集撞类型（CS0436）。
改成**每个 profile 一个类型**：`CliModuleCatalog` / `GuiModuleCatalog`。

附带收益：profile 名从**运行时字符串**变成**编译期类型**，
「传了不存在的 profile 名」这条运行时错误路径随之结构性消失。

### 2.3 引擎侧改用生成物

`EngineSessionFactory.LoadGuiModulesAsync`（38 行手写解析 + switch）整段删除，
改为 `GuiModuleCatalog.Create()`。

## 三、验收

- [x] 引擎与 CLI 各自生成目录，均 0 警告 0 错误。
- [x] `ExplicitModuleOrderTests` 覆盖两侧：`CliModuleCatalog` / `GuiModuleCatalog` 与 YAML 逐项一致。
- [x] **真机**：探针 exe 真建一次 gui 引擎会话，`[MODULE]` 装配顺序与 gui profile 逐项一致。
- [x] `Verify-ExplicitOrder.py` 真机核对 CLI 顺序，适配新 YAML 形状。
- [x] CI 新增 `module-order` job：CLI 顺序核对 + gui 会话探针。

## 四、真机证据

### 4.1 gui 会话首次启动

```
$ JCC_DEBUGLOG=1 OPENAI_API_KEY=sk-dummy-probe artifacts/bin/GuiSessionProbe/Debug/net10.0/GuiSessionProbe.exe
[MODULE] CoreModule start
[MODULE] SlashCommandModule start
[MODULE] ClockModule start
[MODULE] BrowserModule start
[MODULE] HousekeepingModule start
[MODULE] McpInitModule start
[PROBE] gui 会话建立成功，服务提供程序: ServiceProvider
exit=0
```

顺序与 `resources/app/modules.yaml` 的 `gui` profile 逐项一致。

探针工程：`non_deliverables_tools/gui_session_probe/`（退出码即结论：0 成功 / 1 失败）。

### 4.2 CLI 顺序

```
$ python non_deliverables_tools/scripts/Verify-ExplicitOrder.py
段落 49/67 个（条件段按需注入），模块 8/8 个（cli←jcc，gui←JoinCode.Engine 6 个）
显式顺序表与真机装配一致，特性里无 Order 数字。
```

## 五、踩过的坑

1. **生成器的增量 API 惯用写法**：本仓既有生成器统一用
   `CompilationProvider.Select((compilation, _) => …).Combine(CompilationProvider.SelectMany(…))`，
   `Select` 的两参形态存在、单参形态不存在；`Select` 之后不能直接 `.Collect()`。
   照抄 `McpToolDispatchGenerator` 的写法最快，别自己发明。
2. **`ModuleScanResult` 与 `System.Runtime.CompilerServices.CompilationInfo` 撞名**会导致类型推断失败，
   自定义类型名避开 BCL 常见名。
3. **YAML 解析器的两个经典 bug**（C# 与 Python 版各踩一次）：
   - `modules:` 行被当成新的 profile 键 → 后续 `- 名称` 全被跳过；
   - 遍历所有 profile 时，归属程序集被后面的 profile 覆盖 → 必须在进入目标 profile 时取值。
4. **测试工程的分析器约束**：`using` 必须在 `GlobalUsings.cs`（JCC1005）；
   if 嵌套最多 2 层（JCC1009），用卫语句或三元表达式拍平。

## 六、记录

- 2026-11-18：归一 + 真机验证 + CI 门禁完成。
