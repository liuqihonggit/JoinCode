// 探针：真启动一次 GUI/TUI 引擎会话，验证 gui profile 的模块装配路径能跑通，
// 并验证该会话自己解析出的 IConfigurationService 对 theme 的读写落在 global.json（TASK089）。
//
// 背景：
//  1) gui 装配路径此前是「手写 YAML 解析 + 手写 switch 映射」，与 CLI 走的生成器是两套实现，
//     且从未被真机启动过 —— 拼错模块名只会在运行时抛异常。现在由 GuiModuleCatalog 生成，
//     本探针负责证明「生成出来的那份确实能装起来」。
//  2) 主题存储源分叉：GUI 曾读 settings.json，而 config 工具读 global.json。
//     这里用 GUI 会话自己解析出的服务实例读写一次，必须与 global.json 里的值一致。
//
// 用法:
//   JCC_APP_DATA_FOLDER=D:/tmp/xxx JCC_DEBUGLOG=1 dotnet run --project non_deliverables_tools/gui_session_probe
// 退出码: 0 = 会话建立成功且主题读写一致 / 1 = 失败。
//
// 模块装配顺序由引擎的 [MODULE] xxx start 打点输出（受 JCC_DEBUGLOG 控制）。

try {
    var session = await EngineSessionFactory.CreateGuiSessionAsync().ConfigureAwait(false);
    var services = session.Services;
    if (services is null) {
        Console.Error.WriteLine("[PROBE] 会话已返回但没有服务提供程序 — gui 装配路径未真正建立");
        return 1;
    }

    Console.WriteLine("[PROBE] gui 会话建立成功，服务提供程序: " + services.GetType().Name);

    if (services.GetService(typeof(IConfigurationService)) is not IConfigurationService configService) {
        Console.Error.WriteLine("[PROBE] gui 会话里解析不到 IConfigurationService");
        return 1;
    }

    if (services.GetService(typeof(IFileSystem)) is not IFileSystem fs) {
        Console.Error.WriteLine("[PROBE] gui 会话里解析不到 IFileSystem");
        return 1;
    }

    var globalPath = AppDataConstants.Paths.GlobalConfigFilePath;
    var settingsPath = AppDataConstants.Paths.SettingsFilePath;

    // GUI 的读路径（等价 PlaceholderChatSession.GetThemeAsync：走无 source 重载）
    var before = await configService.GetAsync("theme").ConfigureAwait(false);
    Console.WriteLine($"[PROBE] GUI 读 theme = {before ?? "(null)"}");

    const string probeValue = "light-daltonized";
    await configService.SetAsync("theme", probeValue).ConfigureAwait(false);
    var afterRead = await configService.GetAsync("theme").ConfigureAwait(false);

    var globalText = fs.FileExists(globalPath) ? await fs.ReadAllTextAsync(globalPath).ConfigureAwait(false) : "";
    var settingsText = fs.FileExists(settingsPath) ? await fs.ReadAllTextAsync(settingsPath).ConfigureAwait(false) : "";

    var ok = true;
    if (afterRead != probeValue) {
        Console.Error.WriteLine($"[PROBE] 写后读不一致: 期望 {probeValue}，实际 {afterRead ?? "(null)"}");
        ok = false;
    }
    if (!globalText.Contains(probeValue, StringComparison.Ordinal)) {
        Console.Error.WriteLine($"[PROBE] 值没有落到 global.json: {globalPath}");
        ok = false;
    }
    if (settingsText.Contains("\"theme\"", StringComparison.Ordinal)) {
        Console.Error.WriteLine($"[PROBE] settings.json 里仍出现 theme（存储源未收敛）: {settingsPath}");
        ok = false;
    }

    // 显式 global 源必须与 GUI 的默认读给出同一答案（同一张表的两个入口）
    var viaGlobal = await configService.GetAsync("theme", SettingSource.GlobalConfig).ConfigureAwait(false);
    if (viaGlobal != probeValue) {
        Console.Error.WriteLine($"[PROBE] 显式 global 源读到 {viaGlobal ?? "(null)"}，与 GUI 读路径不一致");
        ok = false;
    }

    Console.WriteLine(ok ? "[PROBE] GUI 主题读写与 global.json 一致" : "[PROBE] GUI 主题读写存在分叉");
    return ok ? 0 : 1;
} catch (Exception ex) {
    Console.Error.WriteLine("[PROBE] gui 会话建立失败: " + ex);
    return 1;
}
