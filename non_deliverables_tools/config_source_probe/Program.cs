// 配置源分叉探针 — 验证同一 key 在「无 source 重载」与「按 SupportedSettings 源」下的读取是否一致。
// 背景：/config 命令走 GetAsync(key, ct) → 固定 UserSettings(settings.json)；
//      config_get 工具走 GetAsync(key, source) → theme 等 10 个键是 GlobalConfig(global.json)。
//      若两者答案不同，就是「同一语义两个实现」。
// 退出码：0 = 两路径一致（无分叉）；1 = 分叉；2 = 环境/执行失败。

var appData = Environment.GetEnvironmentVariable("JCC_APP_DATA_FOLDER")
    ?? throw new InvalidOperationException("JCC_APP_DATA_FOLDER 未设置");
Console.WriteLine($"[PROBE] app data folder = {appData}");

// 生产同款文件系统实现
var fs = new PhysicalFileSystem();


// 两份文件各放一个同名 key 的不同值：谁被读到就是谁赢。
// settings.json 是强类型 schema（current 分支）——必须按真实结构写，否则读不出来。
// global.json 是扁平 KV（对齐 TS getGlobalConfig）。
await fs.WriteAllTextAsync(AppDataConstants.Paths.GlobalConfigFilePath, """{"theme":"from-global"}""");
await fs.WriteAllTextAsync(AppDataConstants.Paths.SettingsFilePath, """{"current":{"theme":"from-settings"}}""");
Console.WriteLine($"[PROBE] global.json   = {await fs.ReadAllTextAsync(AppDataConstants.Paths.GlobalConfigFilePath)}");
Console.WriteLine($"[PROBE] settings.json = {(await fs.ReadAllTextAsync(AppDataConstants.Paths.SettingsFilePath)).Replace("\n", " ")}");

// 路径 1：/config 命令用的重载（无 source）
// 必须用独立实例 —— ConfigurationService 的内存缓存按 key 存、不区分存储源，
// 同一实例的第二次读会命中缓存而根本不碰磁盘（缓存会掩盖分叉）。
var viaSlash = await new ConfigurationService(fs).GetAsync("theme");
Console.WriteLine($"[PROBE] GetAsync(key) [无 source，等价 /config]        = {viaSlash ?? "(null)"}");

// 路径 2：config_get 工具用的重载（按 SupportedSettings 的 Source）
var themeConfig = SupportedSettings.GetConfig("theme")
    ?? throw new InvalidOperationException("SupportedSettings 缺 theme");
var source = themeConfig.Source == "global" ? SettingSource.GlobalConfig : SettingSource.UserSettings;
var viaTool = await new ConfigurationService(fs).GetAsync("theme", source);
Console.WriteLine($"[PROBE] GetAsync(key, {source}) [等价 config_get] = {viaTool ?? "(null)"}");

var divergent = viaSlash != viaTool;
Console.WriteLine(divergent
    ? "[PROBE] 结论：分叉 —— 两条路径对同一 key 给出不同答案"
    : "[PROBE] 结论：一致 —— 两条路径对同一 key 给出相同答案");
return divergent ? 1 : 0;
