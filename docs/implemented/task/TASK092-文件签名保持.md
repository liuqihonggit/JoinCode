# TASK092 — 文件签名（BOM）保持

> 触发：TASK091 交付说明里把「`settings.json` 写入会带 UTF-8 BOM」记成遗留。用户当即指出
> 「不应该加入 BOM……如果是有签名就保持签名，如果没有就没有，这种才是健壮设计」。
> 复核后发现不只是「多一个 BOM」：同一个决定在仓里有 **三份实现**，行为彼此矛盾。

## 一、问题

### 1.1 宿主：无 BOM 文件被写入 BOM

`BclFileIO.DetectFromBOM` 对无签名文件返回 `Encoding.UTF8`，而 `Encoding.UTF8` 自带
preamble（`EF BB BF`）。`EncodeString(content, encoding)` 见到 preamble 就写出去，于是

```
DecodeBytes(无 BOM 文件) → encoding = Encoding.UTF8（带 BOM） → 写回多出 EF BB BF
```

`configuration` 侧另有一份 `FileEncodingDetector.DetectFromBOM`，**那份是对的**（无 BOM 返回
`new UTF8Encoding(false)`），但 `FileEncodingDetector.DecodeBytes` 转发的是
`BclFileIO.Instance.DecodeBytes` —— 走的是错的那份。所以修了上层也没用。

影响面：`DecodeBytes → EncodeString` 往返共 15 处写路径（config、file edit、apply patch、
sed 拦截、login/logout、mcp 配置、hooks、并发会话…），全部给用户文件注入 BOM。

### 1.2 工具 exe：BOM 被吃掉，UTF-16 被静默转码

`file` 组 exe 里同时存在两种行为：

| 工具 | 原实现 | 实际效果 |
|---|---|---|
| `apply_patch` | 自带 `Decode/Encode(hasBom)` | BOM 保留（对） |
| `edit` / `file_edit_regex` / `file_batch_edit` | `File.ReadAllText` + `File.WriteAllText` | BOM **被吃掉** |
| `write` | 同上 | 覆盖 BOM 文件后 BOM 丢失 |
| `file_insert_lines` / `file_delete_lines` | `File.WriteAllLines` | BOM 被吃掉 |

真机实测（编辑带 BOM 的文件）：

```
写前: ef bb bf 68 65 6c 6c 6f   ("\uFEFFhello")
写后: 67 6f 6f 64 62 79 65      ("goodbye")    ← BOM 没了
```

顺带暴露更严重的一条：`File.ReadAllText` 会按 BOM 正确解出 UTF-16 文本，而
`File.WriteAllText` 固定按无 BOM UTF-8 写回 —— **编辑一个 UTF-16 文件会把它悄悄转成 UTF-8**，
diff 整文件全改。

## 二、契约

一条规则，三个方向都要满足：

1. 原文件**有**签名（BOM）→ 改写后保留，且只有一个；
2. 原文件**没有**签名 → 改写后不得新增；
3. 编码本身也要跟着原文件走（UTF-16 的还写 UTF-16），不能只解决 BOM。

「签名」判定与「写回」必须成对，且只有一处实现 —— 否则三方各自解释，必然分叉。

## 三、改动

### 3.1 单一实现下沉到 `BclBridge`

`modules/async_primitives/bcl_bridge/BclFileIO.cs`：

- `DetectFromBOM(ReadOnlySpan<byte>)`：**唯一**签名判定。4 字节 BOM（UTF-32LE/BE）先判，
  否则 `FF FE 00 00` 会被截成 UTF-16LE；无签名返回 `Utf8NoBom`（preamble 为空）。
- `EncodeString(string, Encoding)`：有 preamble 就带上，没有就纯内容。
- `Utf8NoBom` 公开，供上层在「新文件 / 检测失败」时取默认值。
- `DecodeBytes` 的 `StreamReader` 保持 `detectEncodingFromByteOrderMarks` 默认 `true`：
  只会跳过与所选 encoding 自身 preamble 一致的那段，不会形成第二套判定。

`FileEncodingDetector`（Infrastructure）改为**薄转发**：`DetectFromBOM` / `DecodeBytes` /
`EncodeString` 全部指向 `BclFileIO`，删掉自己那份判定。与 TASK087 把参数名规则下沉到
契约层、上层留薄委托是同一个形状。

### 3.2 `file` 组 exe 接线

`FileCommands` 新增三个辅助并替换全部 6 处写路径：

- `ReadText(path)` → `(text, encoding)`，用 `BclFileIO.DecodeBytes`；
- `DetectEncoding(path)` → 只读前 4 字节探测（覆盖写用）；
- `WriteText(path, content, encoding)` → `BclFileIO.EncodeString` + `WriteAllBytes`。

`write` 对已存在文件沿用其编码，新文件用 `Utf8NoBom`；`edit` / `file_edit_regex` /
`file_batch_edit` 用读时拿到的编码；`file_insert_lines` / `file_delete_lines` 改为
「切行 → 拼接 → 按原编码写回」，切行口径与 `File.ReadAllLines` 对齐（`\r\n` / `\r` / `\n`，
末尾换行不产生空行）。

`FilePatchCommands` 删掉自带的 `Decode/Encode(hasBom)`，改为
`DetectFromBOM` 定编码 + `EncodeString` 写回；**保留**它原本的严格性 —— UTF-8 用
`new UTF8Encoding(false, true)` 解码，非法字节直接抛，避免把 GBK 之类的文件「改烂」。

### 3.3 依赖墙的唯一例外

工具 exe 的 `cli_node.props` 要求「只引契约层」。这里做了**一处、且只给 file 组**的例外
（`jcc_tool_file.csproj` 内显式引用），理由写在 csproj 注释里：

- `BclBridge` 是零依赖 BCL 隔离层（只包 `System.IO`，无 `ProjectReference`、无第三方包）；
- 不给它加，就必须在 exe 里再写一套 BOM 判断 —— 事实上已经写成三份且互相矛盾；
- 只加在 file 组：把依赖加进公共 props 会让另外 12 个组白背一份依赖。

`BclBridge.csproj` 加 `ExcludeStructura=true`：纯 `System.IO` 用不到 Structura，
否则引用它的工具 exe 会多拷 141 KB（每个 exe 一份）。

## 四、验证

### 4.1 真机

隔离 `JCC_APP_DATA_FOLDER`，跑真实 `jcc.exe`：

| 场景 | 结果 |
|---|---|
| `config_set theme` / `permissions.defaultMode` 写无 BOM `settings.json` | 前 3 字节仍 `7b 22 63`，`autoFetchModels` 仍 `false` |
| 同上写带 BOM `settings.json` | 前 3 字节仍 `ef bb bf`，无重复 BOM |
| `file edit` 带 BOM 文件 | `ef bb bf 67…`，BOM 保留 |
| `file edit` 无 BOM 文件 | `67 6f 6f…`，无新增 BOM |
| `file edit` UTF-16LE 文件 | `ff fe 67 00`，仍是 UTF-16（此前会被转成 UTF-8） |
| `file write` 覆盖带 BOM 文件 / 新建文件 | `ef bb bf 72…` / `62 72 61` |
| `file file_insert_lines` 带 BOM 文件 | `ef bb bf 6c…` |
| `file apply_patch` 带 BOM 文件 | `ef bb bf 6f…`，`OK Modified … (1 hunk(s))` |

### 4.2 测试

- `JoinCode.BclBridge.Tests` **41** 条：新增 UTF-32 两分支、无签名 `GetPreamble()` 为空、
  以及 6 组「解码再编码字节完全一致」的往返不变量（无 BOM / UTF-8 BOM / UTF-16LE/BE / UTF-32LE/BE）。
- `Guard.Config.Tests` **1314** 条：新增 `ConfigFileEncodingTests` 4 条 —— 扁平键与嵌套键写入
  都不得给无 BOM 文件加签名、带 BOM 文件保留且只有一个、重复保存不累积。
  已用「把 `DetectFromBOM` 临时改回返回 `Encoding.UTF8`」实测：4 条中 3 条变红
  （`SaveFlatKey_Utf8BomFile_KeepsSingleBom` 仍绿 —— 旧行为本来就保留 BOM，它锁的是「不重复写」），
  还原修复后 4/4 绿。
- `Hands.ToolHandlers.Tests` **611** 条通过（`FileEditLogic` 走同一条编解码）。

### 4.3 门禁

`Verify-ToolParamNames.py`（36 项）、`Verify-RegistrationOrder.py`、
`Verify-ExplicitOrder.py`、`Verify-ToolNodes.ps1 -DeclarationsOnly`（13 组 / 635 工具 / 388 声明隔离）全绿。
`Pack-ToolNodes.ps1` 13 组装配通过（10876 KB）。

## 五、结论与遗留

- 「有签名保持签名、没有就不加」现在是**一条可断言的不变量**，由 `BclBridge` 一处实现、
  host 与 exe 两侧转发；新增写文件路径只要用这对函数就不会再犯。
- 遗留：`InMemoryFileSystem` 的 `ByteContent` 分支仍直接用 `Encoding.UTF8.GetBytes`
  （测试替身，不落盘，保持无 BOM 语义正确，未改）。
- 遗留：`BclBridge.deps.json` 已不含 Structura，但历史构建目录里可能残留 `Structura.dll`
  （文件仍在、无引用），`Pack-ToolNodes` 不清理旧文件，不影响运行。
