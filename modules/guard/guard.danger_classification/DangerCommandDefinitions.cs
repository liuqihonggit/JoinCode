namespace Core.Security.DangerClassification;

/// <summary>
/// 危险命令定义 — 用 <see cref="DangerCommandAttribute"/> 标注命令,源码生成器扫描生成字典
/// </summary>
/// <remarks>
/// 字段值为命令名(如 "ls"),特性参数指定风险类型/危险等级/描述。
/// 命名约定:字段名用 PascalCase,连字符去除(Get-Content → GetContent)。
/// </remarks>
public static class DangerCommandDefinitions {
    // === Safe（白灯 / 自动通过）— 常见只读命令 ===
    /// <summary>列目录 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "列目录 — 只读")] public const string Ls = "ls";
    /// <summary>列目录 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "列目录 — 只读")] public const string Dir = "dir";
    /// <summary>读文件 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件 — 只读")] public const string Cat = "cat";
    /// <summary>读文件 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件 — 只读")] public const string Type = "type";
    /// <summary>读文件头 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件头 — 只读")] public const string Head = "head";
    /// <summary>读文件尾 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件尾 — 只读")] public const string Tail = "tail";
    /// <summary>文本搜索 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文本搜索 — 只读")] public const string Grep = "grep";
    /// <summary>文本搜索 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文本搜索 — 只读")] public const string Findstr = "findstr";
    /// <summary>统计 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "统计 — 只读")] public const string Wc = "wc";
    /// <summary>打印 — 只读(重定向写入由文件权限检查处理)</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "打印 — 只读（重定向写入由文件权限检查处理）")] public const string Echo = "echo";
    /// <summary>打印 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "打印 — 只读")] public const string Printf = "printf";
    /// <summary>当前目录 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "当前目录 — 只读")] public const string Pwd = "pwd";
    /// <summary>当前用户 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "当前用户 — 只读")] public const string Whoami = "whoami";
    /// <summary>主机名 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "主机名 — 只读")] public const string Hostname = "hostname";
    /// <summary>日期 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "日期 — 只读")] public const string Date = "date";
    /// <summary>系统信息 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "系统信息 — 只读")] public const string Uname = "uname";
    /// <summary>查找命令 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "查找命令 — 只读")] public const string Which = "which";
    /// <summary>查找命令 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "查找命令 — 只读")] public const string Where = "where";
    /// <summary>磁盘空间 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "磁盘空间 — 只读")] public const string Df = "df";
    /// <summary>目录大小 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "目录大小 — 只读")] public const string Du = "du";
    /// <summary>内存 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "内存 — 只读")] public const string Free = "free";
    /// <summary>进程列表 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "进程列表 — 只读")] public const string Ps = "ps";
    /// <summary>网络测试 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "网络测试 — 只读")] public const string Ping = "ping";
    /// <summary>读文件 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件 — 只读")] public const string GetContent = "Get-Content";
    /// <summary>列目录 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "列目录 — 只读")] public const string GetChildItem = "Get-ChildItem";
    /// <summary>获取项 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "获取项 — 只读")] public const string GetItem = "Get-Item";
    /// <summary>当前目录 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "当前目录 — 只读")] public const string GetLocation = "Get-Location";
    /// <summary>进程列表 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "进程列表 — 只读")] public const string GetProcess = "Get-Process";
    /// <summary>服务列表 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "服务列表 — 只读")] public const string GetService = "Get-Service";
    /// <summary>打印 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "打印 — 只读")] public const string WriteOutput = "Write-Output";
    /// <summary>文本搜索 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文本搜索 — 只读")] public const string SelectString = "Select-String";

    // === Safe（白灯 / 自动通过）— 常见开发工具命令 ==="
    /// <summary>.NET CLI — 开发工具(build/run/test 等)</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, ".NET CLI — 开发工具（build/run/test 等）")] public const string Dotnet = "dotnet";
    /// <summary>Node.js — 开发工具</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Node.js — 开发工具")] public const string Node = "node";
    /// <summary>npm 包管理器 — 开发工具</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "npm 包管理器 — 开发工具")] public const string Npm = "npm";
    /// <summary>npx 包执行器 — 开发工具</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "npx 包执行器 — 开发工具")] public const string Npx = "npx";
    /// <summary>Python — 开发工具</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Python — 开发工具")] public const string Python = "python";
    /// <summary>Python3 — 开发工具</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Python3 — 开发工具")] public const string Python3 = "python3";
    /// <summary>pip 包管理器 — 开发工具</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "pip 包管理器 — 开发工具")] public const string Pip = "pip";
    /// <summary>Java — 开发工具</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Java — 开发工具")] public const string Java = "java";
    /// <summary>Maven — 开发工具</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Maven — 开发工具")] public const string Mvn = "mvn";
    /// <summary>Gradle — 开发工具</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Gradle — 开发工具")] public const string Gradle = "gradle";

    // === Safe（白灯 / 自动通过）— 文本与数据处理工具 ===
    // 这几个都能间接执行命令（sed 的 e 命令、awk/jq 的 system()），
    // 但 catalog 里 node/python 早就是 Safe 了，口径必须一致 —— 否则「python 一键执行任意代码放行、
    // jq 同样能力却判未登记」才是真正的不一致。可执行的形式用组合检测单独升级（见 BuildCombinations）。
    /// <summary>文件查找 — 只读（-exec/-delete 单独升级）</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文件查找 — 只读（-exec / -delete 在组合检测中升级）")] public const string Find = "find";
    /// <summary>ripgrep — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "ripgrep — 只读")] public const string Rg = "rg";
    /// <summary>排序 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "排序 — 只读")] public const string Sort = "sort";
    /// <summary>去重 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "去重 — 只读")] public const string Uniq = "uniq";
    /// <summary>按列截取 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "按列截取 — 只读")] public const string Cut = "cut";
    /// <summary>字符替换 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "字符替换 — 只读")] public const string Tr = "tr";
    /// <summary>列对齐 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "列对齐 — 只读")] public const string Column = "column";
    /// <summary>目录树 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "目录树 — 只读")] public const string Tree = "tree";
    /// <summary>文件比较 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文件比较 — 只读")] public const string Diff = "diff";
    /// <summary>文件类型 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文件类型 — 只读")] public const string File = "file";
    /// <summary>文件元信息 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文件元信息 — 只读")] public const string Stat = "stat";
    /// <summary>取文件名 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "取文件名 — 只读")] public const string Basename = "basename";
    /// <summary>取目录名 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "取目录名 — 只读")] public const string Dirname = "dirname";
    /// <summary>解析真实路径 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "解析真实路径 — 只读")] public const string Realpath = "realpath";
    /// <summary>解析符号链接 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "解析符号链接 — 只读")] public const string Readlink = "readlink";
    /// <summary>序列 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "序列 — 只读")] public const string Seq = "seq";
    /// <summary>十六进制转储 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "十六进制转储 — 只读")] public const string Xxd = "xxd";
    /// <summary>Base64 编解码 — 只读（写文件由重定向与文件权限检查处理）</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Base64 编解码 — 只读")] public const string Base64 = "base64";
    /// <summary>MD5 校验 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "MD5 校验 — 只读")] public const string Md5sum = "md5sum";
    /// <summary>SHA1 校验 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "SHA1 校验 — 只读")] public const string Sha1sum = "sha1sum";
    /// <summary>SHA256 校验 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "SHA256 校验 — 只读")] public const string Sha256sum = "sha256sum";
    /// <summary>空操作 — 无副作用</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "空操作 — 无副作用")] public const string True = "true";
    /// <summary>空操作 — 无副作用</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "空操作 — 无副作用")] public const string False = "false";
    /// <summary>条件判断 — 只读</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "条件判断 — 只读")] public const string Test = "test";
    /// <summary>等待 — 无副作用</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "等待 — 无副作用")] public const string Sleep = "sleep";
    /// <summary>环境变量 — 包装器，真实命令由命令位解析取出</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "环境变量 — 包装器（真实命令由命令位解析取出）")] public const string Env = "env";
    /// <summary>JSON 处理 — 数据处理工具（system() 在组合检测中升级）</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "JSON 处理 — 数据处理工具（system() 在组合检测中升级）")] public const string Jq = "jq";
    /// <summary>awk 文本处理 — 数据处理工具（system() 在组合检测中升级）</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "awk 文本处理 — 数据处理工具（system() 在组合检测中升级）")] public const string Awk = "awk";
    /// <summary>sed 文本处理 — 数据处理工具（e 命令执行 / -i 原地写在组合检测中升级）</summary>
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "sed 文本处理 — 数据处理工具（-i 原地写在组合检测中升级）")] public const string Sed = "sed";

    // === LightValidation（绿灯ask / 可撤回）— 新增文件类操作 ===
    /// <summary>创建目录 — 可撤回(删掉空目录即可)</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.LightValidation, "创建目录 — 可撤回")] public const string Mkdir = "mkdir";
    /// <summary>创建/更新时间戳 — 可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.LightValidation, "创建/更新时间戳 — 可撤回")] public const string Touch = "touch";


    // === Dangerous（直接拒绝不提示）— 整盘/系统级不可逆操作 ===
    /// <summary>格式化文件系统 — 不可逆整盘操作</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "格式化文件系统 — 不可逆整盘操作")] public const string Mkfs = "mkfs";
    /// <summary>磁盘分区操作 — 不可逆整盘操作</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "磁盘分区操作 — 不可逆整盘操作")] public const string Fdisk = "fdisk";
    /// <summary>安全擦除文件 — 数据不可恢复</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Dangerous, "安全擦除文件 — 数据不可恢复")] public const string Shred = "shred";
    /// <summary>安全擦除 — 数据不可恢复</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Dangerous, "安全擦除 — 数据不可恢复")] public const string Wipe = "wipe";

    // === LightValidation（绿灯ask / 可撤回）— git 操作 ===
    /// <summary>git 操作 — 可撤回(git reset --hard 等破坏性操作在组合检测中升级)</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.LightValidation, "git 操作 — 可撤回（git reset --hard 等破坏性操作在组合检测中升级）")] public const string Git = "git";

    // === Execution（红灯ask / 不可撤回）— 磁盘/系统操作 ===
    /// <summary>格式化磁盘 — 不可逆操作</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "格式化磁盘 — 不可逆操作")] public const string Format = "format";
    /// <summary>磁盘分区管理 — 可能清盘</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "磁盘分区管理 — 可能清盘")] public const string Diskpart = "diskpart";
    /// <summary>直接磁盘读写 — 可能覆盖整盘</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "直接磁盘读写 — 可能覆盖整盘")] public const string Dd = "dd";
    /// <summary>系统关机 — 不可逆操作</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统关机 — 不可逆操作")] public const string Shutdown = "shutdown";
    /// <summary>系统重启 — 不可逆操作</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统重启 — 不可逆操作")] public const string Reboot = "reboot";
    /// <summary>系统停机 — 不可逆操作</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统停机 — 不可逆操作")] public const string Halt = "halt";
    /// <summary>注册表操作 — 影响系统配置</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "注册表操作 — 影响系统配置")] public const string Reg = "reg";
    /// <summary>注册表编辑器 — 影响系统配置</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "注册表编辑器 — 影响系统配置")] public const string Regedit = "regedit";
    /// <summary>系统服务管理 — 影响系统运行</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统服务管理 — 影响系统运行")] public const string Systemctl = "systemctl";
    /// <summary>防火墙规则 — 影响网络安全</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "防火墙规则 — 影响网络安全")] public const string Iptables = "iptables";
    /// <summary>网络配置 — 影响系统网络</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "网络配置 — 影响系统网络")] public const string Netsh = "netsh";
    /// <summary>计划任务 — 影响系统调度</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务 — 影响系统调度")] public const string Schtasks = "schtasks";
    /// <summary>定时任务 — 影响系统调度</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "定时任务 — 影响系统调度")] public const string Crontab = "crontab";
    /// <summary>服务控制 — 影响系统服务</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "服务控制 — 影响系统服务")] public const string Sc = "sc";

    // === Execution（红灯ask / 不可撤回）— 文件删除操作 ===
    /// <summary>文件删除 — 不可撤回,建议移动到 .xxx/ 目录</summary>
    [DangerCommand(CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Rm = "rm";
    /// <summary>文件删除 — 不可撤回,建议移动到 .xxx/ 目录</summary>
    [DangerCommand(CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Del = "del";
    /// <summary>文件删除 — 不可撤回,建议移动到 .xxx/ 目录</summary>
    [DangerCommand(CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Erase = "erase";
    /// <summary>文件删除 — 不可撤回,建议移动到 .xxx/ 目录</summary>
    [DangerCommand(CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string RemoveItem = "Remove-Item";
    /// <summary>目录删除 — 不可撤回,建议移动到 .xxx/ 目录</summary>
    [DangerCommand(CommandRisk.DirectoryDeletion, CommandDangerLevel.Execution, "目录删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Rmdir = "rmdir";
    /// <summary>目录删除 — 不可撤回,建议移动到 .xxx/ 目录</summary>
    [DangerCommand(CommandRisk.DirectoryDeletion, CommandDangerLevel.Execution, "目录删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Rd = "rd";

    // === Execution（红灯ask / 不可撤回）— 数据移动/复制 ===
    /// <summary>文件移动 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件移动 — 不可撤回")] public const string Mv = "mv";
    /// <summary>文件移动 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件移动 — 不可撤回")] public const string Move = "move";
    /// <summary>文件重命名 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件重命名 — 不可撤回")] public const string RenameItem = "Rename-Item";
    /// <summary>文件复制 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string Cp = "cp";
    /// <summary>文件复制 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string Copy = "copy";
    /// <summary>文件复制 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string Xcopy = "xcopy";
    /// <summary>文件复制 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string Robocopy = "robocopy";
    /// <summary>文件复制 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string CopyItem = "Copy-Item";

    // === Execution（红灯ask / 不可撤回）— 权限修改 ===
    /// <summary>权限修改 — 不可撤回</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "权限修改 — 不可撤回")] public const string Chmod = "chmod";
    /// <summary>属主修改 — 不可撤回</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "属主修改 — 不可撤回")] public const string Chown = "chown";
    /// <summary>属组修改 — 不可撤回</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "属组修改 — 不可撤回")] public const string Chgrp = "chgrp";
    /// <summary>属性修改 — 不可撤回</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "属性修改 — 不可撤回")] public const string Attrib = "attrib";
    /// <summary>ACL 修改 — 不可撤回</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "ACL 修改 — 不可撤回")] public const string Cacls = "cacls";
    /// <summary>ACL 修改 — 不可撤回</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "ACL 修改 — 不可撤回")] public const string Icacls = "icacls";

    // === Execution（红灯ask / 不可撤回）— 进程终止 ===
    /// <summary>进程终止 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "进程终止 — 不可撤回")] public const string Kill = "kill";
    /// <summary>批量进程终止 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "批量进程终止 — 不可撤回")] public const string Killall = "killall";
    /// <summary>进程终止 — 不可撤回</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "进程终止 — 不可撤回")] public const string Taskkill = "taskkill";

    // === Execution（红灯ask / 不可撤回）— 权限提升 ===
    /// <summary>权限提升 — 以 root 执行命令</summary>
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — 以 root 执行命令")] public const string Sudo = "sudo";
    /// <summary>权限提升 — 以其他用户执行</summary>
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — 以其他用户执行")] public const string Runas = "runas";
    /// <summary>启动进程 — 可能提权执行</summary>
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "启动进程 — 可能提权执行")] public const string StartProcess = "Start-Process";

    // === Execution（红灯ask / 不可撤回）— 远程执行/联网 ===
    /// <summary>远程请求 — 不可撤回</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回")] public const string Curl = "curl";
    /// <summary>远程下载 — 不可撤回</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程下载 — 不可撤回")] public const string Wget = "wget";
    /// <summary>远程请求 — 不可撤回</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回")] public const string InvokeWebRequest = "Invoke-WebRequest";
    /// <summary>远程请求 — 不可撤回</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回")] public const string InvokeRestMethod = "Invoke-RestMethod";

    // === Execution（红灯ask / 不可撤回）— PowerShell 危险 cmdlet（集成自 PsDangerousCmdlets）===
    /// <summary>远程命令执行 — 可执行任意代码</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程命令执行 — 可执行任意代码")] public const string InvokeCommand = "Invoke-Command";
    /// <summary>表达式执行 — 可执行任意代码</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "表达式执行 — 可执行任意代码")] public const string InvokeExpression = "Invoke-Expression";
    /// <summary>后台作业 — 可执行任意代码</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "后台作业 — 可执行任意代码")] public const string StartJob = "Start-Job";
    /// <summary>线程作业 — 可执行任意代码</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "线程作业 — 可执行任意代码")] public const string StartThreadJob = "Start-ThreadJob";
    /// <summary>远程会话 — 可跨机器执行</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程会话 — 可跨机器执行")] public const string NewPSSession = "New-PSSession";
    /// <summary>进入远程会话 — 可跨机器执行</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "进入远程会话 — 可跨机器执行")] public const string EnterPSSession = "Enter-PSSession";
    /// <summary>引擎事件注册 — 回调可执行任意代码</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "引擎事件注册 — 回调可执行任意代码")] public const string RegisterEngineEvent = "Register-EngineEvent";
    /// <summary>对象事件注册 — 回调可执行任意代码</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "对象事件注册 — 回调可执行任意代码")] public const string RegisterObjectEvent = "Register-ObjectEvent";
    /// <summary>WMI 事件注册 — 回调可执行任意代码</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "WMI 事件注册 — 回调可执行任意代码")] public const string RegisterWmiEvent = "Register-WmiEvent";
    /// <summary>模块加载 — 模块可执行任意代码</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块加载 — 模块可执行任意代码")] public const string ImportModule = "Import-Module";
    /// <summary>Import-Module 别名 — 模块加载</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Import-Module 别名 — 模块加载")] public const string Ipmo = "ipmo";
    /// <summary>模块安装 — 从远程仓库下载执行</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块安装 — 从远程仓库下载执行")] public const string InstallModule = "Install-Module";
    /// <summary>模块保存 — 从远程仓库下载</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块保存 — 从远程仓库下载")] public const string SaveModule = "Save-Module";
    /// <summary>模块更新 — 从远程仓库下载执行</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块更新 — 从远程仓库下载执行")] public const string UpdateModule = "Update-Module";
    /// <summary>脚本安装 — 从远程仓库下载执行</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "脚本安装 — 从远程仓库下载执行")] public const string InstallScript = "Install-Script";
    /// <summary>脚本保存 — 从远程仓库下载</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "脚本保存 — 从远程仓库下载")] public const string SaveScript = "Save-Script";
    /// <summary>WMI 方法调用 — 可生成进程</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "WMI 方法调用 — 可生成进程")] public const string InvokeWmiMethod = "Invoke-WmiMethod";
    /// <summary>Invoke-WmiMethod 别名 — WMI 方法调用</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Invoke-WmiMethod 别名 — WMI 方法调用")] public const string Iwmi = "iwmi";
    /// <summary>CIM 方法调用 — 可生成进程</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "CIM 方法调用 — 可生成进程")] public const string InvokeCimMethod = "Invoke-CimMethod";
    /// <summary>计划任务注册 — 持久化执行</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务注册 — 持久化执行")] public const string RegisterScheduledTask = "Register-ScheduledTask";
    /// <summary>计划任务创建 — 持久化执行</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务创建 — 持久化执行")] public const string NewScheduledTask = "New-ScheduledTask";
    /// <summary>计划任务动作 — 持久化执行</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务动作 — 持久化执行")] public const string NewScheduledTaskAction = "New-ScheduledTaskAction";
    /// <summary>计划任务设置 — 持久化执行</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务设置 — 持久化执行")] public const string SetScheduledTask = "Set-ScheduledTask";
    /// <summary>计划作业注册 — 持久化执行</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划作业注册 — 持久化执行")] public const string RegisterScheduledJob = "Register-ScheduledJob";
    /// <summary>别名设置 — 可劫持命令解析</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "别名设置 — 可劫持命令解析")] public const string SetAlias = "Set-Alias";
    /// <summary>Set-Alias 别名 — 可劫持命令解析</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "Set-Alias 别名 — 可劫持命令解析")] public const string Sal = "sal";
    /// <summary>别名创建 — 可劫持命令解析</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "别名创建 — 可劫持命令解析")] public const string NewAlias = "New-Alias";
    /// <summary>New-Alias 别名 — 可劫持命令解析</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "New-Alias 别名 — 可劫持命令解析")] public const string Nal = "nal";
    /// <summary>变量设置 — 可劫持命令解析</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "变量设置 — 可劫持命令解析")] public const string SetVariable = "Set-Variable";
    /// <summary>Set-Variable 别名 — 可劫持命令解析</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "Set-Variable 别名 — 可劫持命令解析")] public const string Sv = "sv";
    /// <summary>变量创建 — 可劫持命令解析</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "变量创建 — 可劫持命令解析")] public const string NewVariable = "New-Variable";
    /// <summary>New-Variable 别名 — 可劫持命令解析</summary>
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "New-Variable 别名 — 可劫持命令解析")] public const string Nv = "nv";
    /// <summary>项设置 — 数据修改</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "项设置 — 数据修改")] public const string SetItem = "Set-Item";
    /// <summary>Set-Item 别名 — 数据修改</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "Set-Item 别名 — 数据修改")] public const string Si = "si";
    /// <summary>项创建 — 数据修改</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "项创建 — 数据修改")] public const string NewItem = "New-Item";
    /// <summary>New-Item 别名 — 数据修改</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "New-Item 别名 — 数据修改")] public const string Ni = "ni";
    /// <summary>项清除 — 数据修改</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "项清除 — 数据修改")] public const string ClearItem = "Clear-Item";
    /// <summary>Clear-Item 别名 — 数据修改</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "Clear-Item 别名 — 数据修改")] public const string Cli = "cli";
    /// <summary>内容写入 — 数据修改</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "内容写入 — 数据修改")] public const string SetContent = "Set-Content";
    /// <summary>内容追加 — 数据修改</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "内容追加 — 数据修改")] public const string AddContent = "Add-Content";
    /// <summary>Add-Content 别名 — 数据修改</summary>
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "Add-Content 别名 — 数据修改")] public const string Ac = "ac";
    /// <summary>Invoke-WebRequest 别名 — 远程请求</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Invoke-WebRequest 别名 — 远程请求")] public const string Iwr = "iwr";
    /// <summary>Invoke-RestMethod 别名 — 远程请求</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Invoke-RestMethod 别名 — 远程请求")] public const string Irm = "irm";
    /// <summary>对象创建 — 可实例化 COM/ActiveX</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "对象创建 — 可实例化 COM/ActiveX")] public const string NewObject = "New-Object";
    /// <summary>BITS 传输 — 后台远程下载</summary>
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "BITS 传输 — 后台远程下载")] public const string StartBitsTransfer = "Start-BitsTransfer";

    // === Execution（红灯ask / 不可撤回）— 权限提升(补充) ===
    /// <summary>权限提升 — OpenBSD doas</summary>
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — OpenBSD doas")] public const string Doas = "doas";
    /// <summary>权限提升 — PolicyKit pkexec</summary>
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — PolicyKit pkexec")] public const string Pkexec = "pkexec";
    /// <summary>Start-Process 别名 — 启动进程可能提权</summary>
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "Start-Process 别名 — 启动进程可能提权")] public const string Saps = "saps";
    /// <summary>Start-Process 别名 — 启动进程可能提权</summary>
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "Start-Process 别名 — 启动进程可能提权")] public const string Start = "start";
}