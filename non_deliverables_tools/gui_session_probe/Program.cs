// 探针：真启动一次 GUI/TUI 引擎会话，验证 gui profile 的模块装配路径能跑通。
//
// 背景：gui 这条装配路径此前是「手写 YAML 解析 + 手写 switch 映射」，与 CLI 走的
// 生成器是两套实现，而且从未被真机启动过 —— 拼错模块名只会在运行时抛异常。
// 现在 gui profile 由 AppModule.Generator 生成（GuiModuleCatalog），拼错名字编译期就报错；
// 本探针负责证明「生成出来的那份确实能装起来」。
//
// 用法:
//   JCC_DEBUGLOG=1 dotnet run --project non_deliverables_tools/gui_session_probe
// 退出码: 0 = 会话建立成功 / 1 = 失败。
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
    return 0;
} catch (Exception ex) {
    Console.Error.WriteLine("[PROBE] gui 会话建立失败: " + ex);
    return 1;
}
