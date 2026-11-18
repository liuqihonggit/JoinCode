// 配置存储源一致性探针（TASK089 验收）
//
// 修前形状：/theme、GUI 读写 settings.json，而 config_get/config_set 按另一张表的
// source 读写 global.json —— 同一键两处答案，用户与 LLM 互相看不见对方的设置。
// 修后形状：存储源由契约层 ConfigKeyRegistry（唯一清单）决定，且旧 settings.json 里的 global 键会一次性搬走
//（TASK091 起这一步叫「迁移 v1」，跑过一次就记在 ~/.jcc/config-migrations.json，不再每轮重跑）。
//
// 三个场景都必须过：
//   1) 旧布局（只有 settings.json 有 theme）→ 读得到 + 搬到 global.json + settings 里删掉 + 可重复执行
//   2) 两边都有值 → global 权威、settings 的旧值清掉
//   3) 非 global 键不动（model 留在 settings），布尔值搬过去仍是布尔而非字符串
//
// 退出码：0 = 全部通过；1 = 有断言失败；2 = 环境/执行异常。

var fs = new PhysicalFileSystem();
var failures = new List<string>();

var root = Path.Combine(Path.GetTempPath(), $"jcc_cfg_probe_{DateTime.UtcNow:HHmmss}_{Environment.ProcessId}");
DirectoryHelper.EnsureDirectoryExists(fs, root);

static void Check(List<string> failures, bool condition, string message) {
    Console.WriteLine(condition ? $"  [OK]   {message}" : $"  [FAIL] {message}");
    if (!condition) failures.Add(message);
}

static void UseFolder(IFileSystem fs, string folder) {
    DirectoryHelper.EnsureDirectoryExists(fs, folder);
    AppDataConstants.Paths = AppDataPaths.CreateForTest(folder);
}

// 每个场景都用全新的 ConfigurationService 实例 —— 内存缓存会把「读错源」掩盖成一致。
static Task<string?> Read(string key) => new ConfigurationService(new PhysicalFileSystem()).GetAsync(key);

static async Task<JsonNode?> ReadNodeAsync(IFileSystem fs, string path) {
    if (!fs.FileExists(path)) return null;
    var text = await fs.ReadAllTextAsync(path).ConfigureAwait(false);
    return JsonNode.Parse(text);
}

static Task WriteTextAsync(IFileSystem fs, string path, string content)
    => fs.WriteAllTextAsync(path, content);

try {
    // ── 场景 1：旧布局 ────────────────────────────────────────────────
    Console.WriteLine("[场景 1] 旧布局：theme 只在 settings.json");
    UseFolder(fs, Path.Combine(root, "s1"));
    var settingsPath = AppDataConstants.Paths.SettingsFilePath;
    var globalPath = AppDataConstants.Paths.GlobalConfigFilePath;
    await WriteTextAsync(fs, settingsPath, """{"current":{"theme":"from-settings","language":"zh"}}""").ConfigureAwait(false);

    var themeRead = await Read("theme").ConfigureAwait(false);
    Check(failures, themeRead == "from-settings", $"迁移前读取得到用户原值（实际 {themeRead ?? "(null)"}）");

    var global1 = await ReadNodeAsync(fs, globalPath).ConfigureAwait(false);
    Check(failures, global1?["theme"]?.GetValue<string>() == "from-settings", "值已落到 global.json");

    var settings1 = await ReadNodeAsync(fs, settingsPath).ConfigureAwait(false);
    Check(failures, settings1 is not null && settings1["current"]?["theme"] is null, "settings.json 的 current.theme 已删除");
    Check(failures, settings1?["current"]?["language"]?.GetValue<string>() == "zh", "非 global 键 language 原样保留");

    var viaGlobal = await new ConfigurationService(new PhysicalFileSystem())
        .GetAsync("theme", SettingSource.GlobalConfig).ConfigureAwait(false);
    Check(failures, viaGlobal == "from-settings", $"显式指定 global 源与默认路径答案一致（实际 {viaGlobal ?? "(null)"}）");

    var snapshotGlobal = await fs.ReadAllTextAsync(globalPath).ConfigureAwait(false);
    var snapshotSettings = await fs.ReadAllTextAsync(settingsPath).ConfigureAwait(false);
    _ = await Read("theme").ConfigureAwait(false);
    var afterGlobal = await fs.ReadAllTextAsync(globalPath).ConfigureAwait(false);
    var afterSettings = await fs.ReadAllTextAsync(settingsPath).ConfigureAwait(false);
    Check(failures, afterGlobal == snapshotGlobal && afterSettings == snapshotSettings, "再跑一次不产生任何写入（幂等）");

    // language 属 settings 源 —— 证明「收敛存储源」没有把非 global 键一起搬走或读不到
    var languageRead = await Read("language").ConfigureAwait(false);
    Check(failures, languageRead == "zh", $"非 global 键走 settings 源仍可读（实际 {languageRead ?? "(null)"}）");

    // ── 场景 2：两边都有值 ────────────────────────────────────────────
    Console.WriteLine("[场景 2] 冲突：global 与 settings 都有 theme");
    UseFolder(fs, Path.Combine(root, "s2"));
    settingsPath = AppDataConstants.Paths.SettingsFilePath;
    globalPath = AppDataConstants.Paths.GlobalConfigFilePath;
    await WriteTextAsync(fs, globalPath, """{"theme":"from-global"}""").ConfigureAwait(false);
    await WriteTextAsync(fs, settingsPath, """{"current":{"theme":"from-settings"}}""").ConfigureAwait(false);

    var conflictRead = await Read("theme").ConfigureAwait(false);
    Check(failures, conflictRead == "from-global", $"冲突时 global 权威（实际 {conflictRead ?? "(null)"}）");
    var settings2 = await ReadNodeAsync(fs, settingsPath).ConfigureAwait(false);
    Check(failures, settings2?["current"]?["theme"] is null, "冲突时 settings.json 的旧值被清掉");

    // ── 场景 3：类型与嵌套键 ──────────────────────────────────────────
    Console.WriteLine("[场景 3] 布尔值与嵌套键");
    UseFolder(fs, Path.Combine(root, "s3"));
    settingsPath = AppDataConstants.Paths.SettingsFilePath;
    globalPath = AppDataConstants.Paths.GlobalConfigFilePath;
    await WriteTextAsync(fs, settingsPath, """{"current":{"debuglog":true,"permissions":{"defaultMode":"plan"}}}""").ConfigureAwait(false);

    var debugRead = await Read("debuglog").ConfigureAwait(false);
    Check(failures, debugRead == "true", $"debuglog 读得到（实际 {debugRead ?? "(null)"}）");

    var global3 = await ReadNodeAsync(fs, globalPath).ConfigureAwait(false);
    Check(failures, global3?["debuglog"]?.GetValueKind() == JsonValueKind.True, "布尔值搬到 global.json 仍是布尔（不是字符串 \"true\"）");

    // 注意：嵌套键的「读」本身不支持（生成的 GetSettingByKey 不认带点的键，既有缺口），
    // 本探针只断言迁移没把嵌套结构弄坏。
    var settings3 = await ReadNodeAsync(fs, settingsPath).ConfigureAwait(false);
    Check(failures, settings3?["current"]?["permissions"]?["defaultMode"]?.GetValue<string>() == "plan", "嵌套结构原样留在 settings.json");
} catch (Exception ex) {
    Console.WriteLine($"[PROBE] 执行异常: {ex}");
    return 2;
}

Console.WriteLine();
if (failures.Count > 0) {
    Console.WriteLine($"[PROBE] 结论：{failures.Count} 项失败");
    foreach (var f in failures) Console.WriteLine($"         - {f}");
    return 1;
}

Console.WriteLine("[PROBE] 结论：三条路径 + 迁移全部一致");
Console.WriteLine($"[PROBE] 临时目录: {root}");
return 0;
