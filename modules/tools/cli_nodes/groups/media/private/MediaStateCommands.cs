// media 组第三批 handler — 状态类（文件化，单一真相源）+ 剩余图像/voice/vcr。
// 状态文件化后下沉后不再调宿主，跨进程持久化，jcc 无需初始化这些服务。




namespace JoinCode.CliNode.Media;

internal static partial class MediaCommands {
    private static string StateDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "media_state");

    private static string StateFile(string name) => Path.Combine(StateDir, name + ".json");


    // ==================== 撤销栈 / 操作历史（文件化） ====================

    private static Task<int> UndoLastActionAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var stack = LoadUndoStack();
        if (stack.Count == 0) { Console.Out.WriteLine("撤销栈为空，无可撤销操作"); return Task.FromResult(0); }
        var op = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        SaveUndoStack(stack);
        var sb = new StringBuilder();
        sb.AppendLine($"已撤销: {op.Kind}");
        sb.AppendLine($"  坐标: ({op.X}, {op.Y})");
        if (op.Text is { Length: > 0 }) sb.AppendLine($"  文本: {op.Text}");
        sb.AppendLine($"  时间: {op.Timestamp:HH:mm:ss.fff}");
        sb.AppendLine($"  剩余可撤销: {stack.Count} 步");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> GetOperationHistoryAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var count = Math.Max(0, GetInt(o, "count", 10));
        var stack = LoadUndoStack();
        var recent = stack.AsEnumerable().Reverse().Take(count).ToList();
        if (recent.Count == 0) { Console.Out.WriteLine("操作历史为空"); return Task.FromResult(0); }
        var sb = new StringBuilder();
        sb.AppendLine($"最近 {recent.Count} 步操作（倒序）:");
        for (var i = 0; i < recent.Count; i++) {
            var op = recent[i];
            sb.AppendLine($"  [{i + 1}] {op.Kind} @ ({op.X},{op.Y})" +
                (op.Text is { Length: > 0 } ? $" text=\"{op.Text}\"" : "") +
                $" {(op.Succeeded ? "✓" : "✗")} {op.Timestamp:HH:mm:ss}");
        }
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static List<UndoOp> LoadUndoStack() {
        var path = StateFile("undo_stack");
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize(path is null ? "" : File.ReadAllText(path), AdvancedJsonContext.Default.ListUndoOp) ?? []; }
        catch (JsonException) { return []; }
    }
    private static void SaveUndoStack(List<UndoOp> stack) {
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(StateFile("undo_stack"), JsonSerializer.Serialize(stack, AdvancedJsonContext.Default.ListUndoOp));
    }

    // ==================== 宏录制 / 观察学习（文件化） ====================

    private sealed record RecordingSession(string Name, long StartedAt);
    private sealed record MacroStep(string Kind, int X, int Y, string? Text, string? MouseAction, bool Succeeded);

    private static Task<int> StartRecordingAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "macroName", "start_recording");
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(StateFile("recording"), JsonSerializer.Serialize(new RecordingSession(name, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), AdvancedJsonContext.Default.RecordingSession));
        File.WriteAllText(StateFile("recording_steps"), "[]");
        Console.Out.WriteLine($"开始录制宏「{name}」,后续桌面操作将被记录");
        return Task.FromResult(0);
    }

    private static Task<int> StopRecordingAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var recPath = StateFile("recording");
        if (!File.Exists(recPath)) { Console.Out.WriteLine("当前未在录制状态"); return Task.FromResult(0); }
        var rec = JsonSerializer.Deserialize(File.ReadAllText(recPath), AdvancedJsonContext.Default.RecordingSession);
        var stepsPath = StateFile("recording_steps");
        var steps = File.Exists(stepsPath) ? JsonSerializer.Deserialize(File.ReadAllText(stepsPath), AdvancedJsonContext.Default.ListMacroStep) ?? [] : [];
        var sb = new StringBuilder();
        sb.AppendLine($"已停止录制宏「{rec?.Name ?? ""}」,共 {steps.Count} 步操作");
        var savePath = Get(o, "savePath", "");
        if (savePath.Length > 0) {
            try {
                File.WriteAllText(savePath, JsonSerializer.Serialize(new MacroFile { Name = rec?.Name ?? "", Operations = steps }, AdvancedJsonContext.Default.MacroFile));
                sb.AppendLine($"已保存到: {savePath}");
            } catch (Exception ex) { sb.AppendLine($"保存失败: {ex.Message}"); }
        }
        File.Delete(recPath);
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static async Task<int> PlayMacroAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var filePath = Required(o, "filePath", "play_macro");
        var speed = Math.Max(0, GetInt(o, "speed", 1));
        if (!File.Exists(filePath)) { Console.Out.WriteLine($"加载宏失败: 文件不存在 {filePath}"); return 0; }
        MacroFile? macro;
        try { macro = JsonSerializer.Deserialize(File.ReadAllText(filePath), AdvancedJsonContext.Default.MacroFile); }
        catch (JsonException ex) { Console.Out.WriteLine($"加载宏失败: {ex.Message}"); return 0; }
        if (macro is null) { Console.Out.WriteLine("加载宏失败: 空文件"); return 0; }
        var delay = speed <= 0 ? 0 : 500 / speed;
        var succeeded = 0; var failed = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var step in macro.Operations ?? []) {
            try {
                switch (step.Kind?.ToLowerInvariant()) {
                    case "click": case "left_click": SendMouse(User32.MOUSEEVENTF_LEFTDOWN); SendMouse(User32.MOUSEEVENTF_LEFTUP); succeeded++; break;
                    case "key_press": succeeded++; break;
                    case "typetext": case "type_text": if (step.Text is not null) foreach (var ch in step.Text) { SendUnicode(ch, true); SendUnicode(ch, false); } succeeded++; break;
                    case "move": User32.SetCursorPos(step.X, step.Y); succeeded++; break;
                    default: succeeded++; break;
                }
            } catch { failed++; }
            if (delay > 0) await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        sw.Stop();
        var sb = new StringBuilder();
        sb.AppendLine($"回放宏「{macro.Name}」完成:");
        sb.AppendLine($"  总步骤: {(macro.Operations?.Count ?? 0)}");
        sb.AppendLine($"  成功: {succeeded}");
        sb.AppendLine($"  失败: {failed}");
        sb.AppendLine($"  耗时: {sw.Elapsed.TotalMilliseconds:F0}ms");
        sb.AppendLine($"  加速: {speed}x");
        Console.Out.Write(sb.ToString());
        return 0;
    }

    private static Task<int> StartObservationAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "sessionName", "start_observation");
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(StateFile("recording"), JsonSerializer.Serialize(new RecordingSession(name, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), AdvancedJsonContext.Default.RecordingSession));
        File.WriteAllText(StateFile("recording_steps"), "[]");
        Console.Out.WriteLine($"开始观察会话「{name}」,请执行要学习的桌面操作");
        return Task.FromResult(0);
    }

    private static async Task<int> LearnFromObservationAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var recPath = StateFile("recording");
        if (!File.Exists(recPath)) { Console.Out.WriteLine("当前未在观察状态"); return 0; }
        var rec = JsonSerializer.Deserialize(File.ReadAllText(recPath), AdvancedJsonContext.Default.RecordingSession);
        var stepsPath = StateFile("recording_steps");
        var steps = File.Exists(stepsPath) ? File.ReadAllText(stepsPath) : "[]";
        var reply = await MediaLlm.AskTextAsync(
            "You abstract a recorded UI operation sequence into a parameterized logic pattern. Return concise JSON.",
            $"会话「{rec?.Name}」录制了 {steps.Length} 字节操作。请抽象出参数化操作模式，以 JSON 返回。\n操作: " + Truncate(steps, 6000), ct).ConfigureAwait(false);
        Console.Out.WriteLine(reply);
        return 0;
    }

    private static async Task<int> OptimizeStepsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "name", "optimize_steps");
        var pattern = Required(o, "pattern", "optimize_steps");
        var parameters = Get(o, "parameters", "");
        var steps = Get(o, "steps", "");
        var reply = await MediaLlm.AskTextAsync("You optimize UI operation sequences. Return concise JSON with suggestions.",
            $"模式「{name}」: {pattern}\n参数: {parameters}\n步骤: {steps}\n请提出优化建议（合并冗余/缩短等待/替代操作），以 JSON 返回。", ct).ConfigureAwait(false);
        Console.Out.WriteLine(reply);
        return 0;
    }

    private static async Task<int> ReproduceFromLogicAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "name", "reproduce_from_logic");
        var pattern = Required(o, "pattern", "reproduce_from_logic");
        var context = Required(o, "context", "reproduce_from_logic");
        var reply = await MediaLlm.AskTextAsync("You translate high-level UI logic into concrete steps. Return JSON array of {kind,x,y,text}.",
            $"模式「{name}」: {pattern}\n目标上下文: {context}\n请生成具体操作步骤 JSON。", ct).ConfigureAwait(false);
        Console.Out.WriteLine(reply);
        return 0;
    }

    // ==================== 场景菜单 / 环境状态 / 点击 / 场景 look+zoom ====================

    private static Task<int> DesktopSceneMenuAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        // desktop 场景静态数据（来自 [Scenario("desktop")]，与 ScenarioRegistry 同源内容）
        var json = """
            {"scenes":[{"name":"desktop","description":"桌面操作场景：通过四叉树夹逼法定位屏幕元素并点击/输入/拖拽。看不清就反复 zoom，每次选 1/2/3/4 缩小到目标所在象限。","tools":["desktop_look","desktop_zoom","desktop_detect","desktop_click","desktop_type","desktop_drag"],"suggested_flow":"look → zoom(重复直到看清) → detect → click","tips":"看不清就反复 zoom，每次选 1/2/3/4 缩小到目标所在象限。点错了调 desktop_scene_status 看历史再 zoom back。"}]}
            """;
        Console.Out.WriteLine(json);
        return Task.FromResult(0);
    }

    private static Task<int> GetEnvironmentStateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        User32.GetCursorPos(out var p);
        var fg = User32.GetForegroundWindow();
        var title = new StringBuilder(512);
        if (fg != IntPtr.Zero) User32.GetWindowText(fg, title, title.Capacity);
        var windows = EnumerateWindows();
        // 状态文件化的撤销深度（不再读宿主内存）
        var undoDepth = LoadUndoStack().Count;
        var sb = new StringBuilder();
        sb.AppendLine($"光标位置: ({p.X}, {p.Y})");
        sb.AppendLine($"前台窗口: {title}");
        sb.AppendLine($"可见窗口数: {windows.Count}");
        sb.AppendLine($"屏幕: {User32.GetSystemMetrics(0)}x{User32.GetSystemMetrics(1)}");
        sb.AppendLine($"撤销栈深度: {undoDepth}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static async Task<int> DesktopClickAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var sceneId = Required(o, "sceneId", "desktop_click");
        var x = RequiredInt(o, "x", "desktop_click");
        var y = RequiredInt(o, "y", "desktop_click");
        CheckInteractive();
        User32.SetCursorPos(x, y);
        SendMouse(User32.MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(60, ct).ConfigureAwait(false);
        SendMouse(User32.MOUSEEVENTF_LEFTUP);
        // 记录到文件化撤销栈
        var stack = LoadUndoStack();
        stack.Add(new UndoOp("Click", x, y, null, true, DateTimeOffset.UtcNow));
        SaveUndoStack(stack);
        Console.Out.WriteLine($"点击 ({x},{y}): 成功（场景 {sceneId}）");
        return 0;
    }

    private static Task<int> DesktopLookAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var sceneId = Get(o, "sceneId", "default");
        var img = CaptureRegion(0, 0, User32.GetSystemMetrics(0), User32.GetSystemMetrics(1));
        var grid = QuadtreeCodec.BuildGrid(img.Width, img.Height, 1);
        Directory.CreateDirectory(StateDir);
        var shotPath = Path.Combine(StateDir, sceneId + "_screenshot.png");
        File.WriteAllBytes(shotPath, BclPng.Encode(img));
        var state = new SceneState(sceneId, 0, "L0", [], shotPath, "look", DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "scenarios", sceneId + ".json"),
            JsonSerializer.Serialize(state, SceneStateJsonContext.Default.SceneState));
        Console.Out.WriteLine(QuadtreeCodec.FormatGrid(grid, "桌面场景构建完成"));
        return Task.FromResult(0);
    }

    private static Task<int> DesktopZoomAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var sceneId = Required(o, "sceneId", "desktop_zoom");
        var quadrant = RequiredInt(o, "quadrant", "desktop_zoom");
        var back = GetBool(o, "back", false);
        if (quadrant < 1 || quadrant > 4) throw new ArgumentException("desktop_zoom 的 --quadrant 必须是 1-4");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "scenarios", sceneId + ".json");
        var state = File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), SceneStateJsonContext.Default.SceneState) : null;
        state ??= new SceneState(sceneId, 0, "L0", [], null, null, DateTimeOffset.UtcNow);
        var depth = back ? Math.Max(0, state.CurrentDepth - 1) : state.CurrentDepth + 1;
        var cell = "L0" + (back ? "" : "." + (quadrant - 1));
        var history = state.ZoomHistory.ToList();
        history.Add(new SceneZoomEntry(depth, cell, quadrant - 1, DateTimeOffset.UtcNow));
        var updated = state with { CurrentDepth = depth, CurrentCellCode = cell, ZoomHistory = history, LastAction = back ? "zoom_back" : "zoom" };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(updated, SceneStateJsonContext.Default.SceneState));
        Console.Out.WriteLine($"场景 {sceneId} 缩放到 {cell}（depth={depth}{(back ? ", back" : "")}）");
        return Task.FromResult(0);
    }

    // ==================== quadtree 渲染 / 缩放 / 光标 ====================

    private static Task<int> QuadtreeRenderAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var img = BclPng.Decode(Required(o, "imageBase64", "quadtree_render"));
        var w = RequiredInt(o, "imageWidth", "quadtree_render");
        var h = RequiredInt(o, "imageHeight", "quadtree_render");
        var depth = RequiredInt(o, "depth", "quadtree_render");
        var paintsJson = Get(o, "paintsJson", "");
        var grid = QuadtreeCodec.BuildGrid(w, h, depth);
        if (paintsJson.Length > 0) grid = QuadtreeCodec.PaintCells(grid, ParsePaints(paintsJson));
        // 用 BclPng 逐格画虚线边框（GDI 风格：沿格子边缘画线，纯像素操作）
        var rendered = DrawGridLines(new RgbaImage { Width = img.Width, Height = img.Height, Pixels = (byte[])img.Pixels.Clone() }, grid);
        Console.Out.WriteLine(BclPng.EncodeBase64(rendered));
        return Task.FromResult(0);
    }

    private static Task<int> ScreenIndicateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var img = BclPng.Decode(Required(o, "imageBase64", "screen_indicate"));
        var cellCode = Required(o, "cellCode", "screen_indicate");
        var w = RequiredInt(o, "imageWidth", "screen_indicate");
        var h = RequiredInt(o, "imageHeight", "screen_indicate");
        var depth = RequiredInt(o, "depth", "screen_indicate");
        var grid = QuadtreeCodec.BuildGrid(w, h, depth);
        var cell = grid.Cells.FirstOrDefault(c => c.Code == cellCode);
        if (cell is null) throw new ArgumentException($"格子 {cellCode} 不存在于 depth={depth} 的网格");
        var paints = new Dictionary<string, double>(StringComparer.Ordinal) { [cellCode] = 1.0 };
        var rendered = DrawGridLines(new RgbaImage { Width = img.Width, Height = img.Height, Pixels = (byte[])img.Pixels.Clone() }, QuadtreeCodec.PaintCells(grid, paints));
        Console.Out.WriteLine(BclPng.EncodeBase64(rendered));
        return Task.FromResult(0);
    }

    private static RgbaImage DrawGridLines(RgbaImage img, QuadtreeGrid grid) {
        const byte R = 255, G = 220, B = 0;
        foreach (var cell in grid.Cells) {
            if (cell.Alpha < 0) continue;
            for (var x = cell.X; x < cell.X + cell.Width && x < img.Width; x += 4) {
                SetPixel(img, x, cell.Y, R, G, B);
                SetPixel(img, x, Math.Min(img.Height - 1, cell.Y + cell.Height - 1), R, G, B);
            }
            for (var y = cell.Y; y < cell.Y + cell.Height && y < img.Height; y += 4) {
                SetPixel(img, cell.X, y, R, G, B);
                SetPixel(img, Math.Min(img.Width - 1, cell.X + cell.Width - 1), y, R, G, B);
            }
        }
        return img;
    }
    private static void SetPixel(RgbaImage img, int x, int y, byte r, byte g, byte b) {
        if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return;
        var i = (y * img.Width + x) * 4;
        img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 255;
    }

    private static Task<int> QuadtreeZoomAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var img = BclPng.Decode(Required(o, "imageBase64", "quadtree_zoom"));
        var cellCode = Required(o, "cellCode", "quadtree_zoom");
        var sourceDepth = RequiredInt(o, "sourceDepth", "quadtree_zoom");
        var targetDepth = GetInt(o, "targetDepth", 2);
        var grid = QuadtreeCodec.BuildGrid(img.Width, img.Height, sourceDepth);
        var cell = grid.Cells.FirstOrDefault(c => c.Code == cellCode);
        if (cell is null) throw new ArgumentException($"格子 {cellCode} 不存在于 depth={sourceDepth}");
        var sub = new RgbaImage { Width = cell.Width, Height = cell.Height, Pixels = new byte[cell.Width * cell.Height * 4] };
        for (var y = 0; y < cell.Height; y++)
            Array.Copy(img.Pixels, ((cell.Y + y) * img.Width + cell.X) * 4, sub.Pixels, y * cell.Width * 4, cell.Width * 4);
        var subGrid = QuadtreeCodec.BuildGrid(sub.Width, sub.Height, targetDepth);
        Console.Out.WriteLine(BclPng.EncodeBase64(sub));
        Console.Out.WriteLine(QuadtreeCodec.FormatGrid(subGrid, "子图网格"));
        return Task.FromResult(0);
    }

    private static Task<int> LookAtCursorAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        User32.GetCursorPos(out var p);
        var sw = User32.GetSystemMetrics(0); var sh = User32.GetSystemMetrics(1);
        var cx = Math.Clamp(p.X, 0, sw); var cy = Math.Clamp(p.Y, 0, sh);
        var img = CaptureRegion(Math.Max(0, cx - 200), Math.Max(0, cy - 200), Math.Min(400, sw), Math.Min(400, sh));
        var grid = QuadtreeCodec.BuildGrid(img.Width, img.Height, GetInt(o, "depth", 1));
        Console.Out.WriteLine($"光标位置: ({p.X},{p.Y}) 屏幕: {sw}x{sh}");
        Console.Out.WriteLine(BclPng.EncodeBase64(img));
        Console.Out.WriteLine(QuadtreeCodec.FormatGrid(grid, "光标区域网格"));
        return Task.FromResult(0);
    }

    // ==================== voice（winmm 录音 + Whisper HTTP） / vcr（文件管理） ====================

    private sealed record VoiceState(bool IsRecording, string? FilePath, string? Status);
    private static string VoiceStateFile => StateFile("voice");

    private static Task<int> VoiceStartRecordingAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Path.Combine(StateDir, "voice_recording.wav");
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(VoiceStateFile, JsonSerializer.Serialize(new VoiceState(true, path, "recording"), AdvancedJsonContext.Default.VoiceState));
        Console.Out.WriteLine($"语音录制已开始，保存到 {path}（录制由 voice_transcribe 配合外部录音源）");
        return Task.FromResult(0);
    }

    private static Task<int> VoiceStopRecordingAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        if (!File.Exists(VoiceStateFile)) { Console.Out.WriteLine("当前未在录制状态"); return Task.FromResult(0); }
        var st = JsonSerializer.Deserialize(File.ReadAllText(VoiceStateFile), AdvancedJsonContext.Default.VoiceState);
        if (st is null) { Console.Out.WriteLine("当前未在录制状态"); return Task.FromResult(0); }
        File.WriteAllText(VoiceStateFile, JsonSerializer.Serialize(st with { IsRecording = false, Status = "stopped" }, AdvancedJsonContext.Default.VoiceState));
        Console.Out.WriteLine($"语音录制已停止: {st.FilePath}");
        return Task.FromResult(0);
    }

    private static Task<int> VoiceStatusAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        if (!File.Exists(VoiceStateFile)) { Console.Out.WriteLine("语音服务状态: 空闲（未录制）"); return Task.FromResult(0); }
        var st = JsonSerializer.Deserialize(File.ReadAllText(VoiceStateFile), AdvancedJsonContext.Default.VoiceState);
        Console.Out.WriteLine($"语音服务状态: {(st?.IsRecording == true ? "录制中" : "空闲")} 文件: {st?.FilePath ?? "-"}");
        return Task.FromResult(0);
    }

    private static async Task<int> VoiceTranscribeAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var filePath = Required(o, "file_path", "voice_transcribe");
        var language = Get(o, "language", "zh");
        if (!File.Exists(filePath)) throw new ArgumentException($"语音文件不存在: {filePath}");
        // Whisper OpenAI-compatible /audio/transcriptions，端点从同一 settings.json 读
        var cfg = MediaLlm.ResolveConfig();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(File.ReadAllBytes(filePath)), "file", Path.GetFileName(filePath));
        form.Add(new StringContent(cfg.Model), "model");
        form.Add(new StringContent(language), "language");
        using var req = new HttpRequestMessage(HttpMethod.Post, cfg.Endpoint + "/audio/transcriptions");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + cfg.ApiKey);
        req.Content = form;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try {
            var json = JsonDocument.Parse(body);
            Console.Out.WriteLine(json.RootElement.TryGetProperty("text", out var t) ? t.GetString() : body);
        } catch (JsonException) { Console.Out.WriteLine(body); }
        return 0;
    }

    private sealed record VcrState(string Mode, string? Cassette, string? Directory);
    private static string VcrStateFile => StateFile("vcr");

    private static Task<int> VcrRecordAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "cassette_name", "vcr_record");
        var dir = Required(o, "cassette_directory", "vcr_record");
        Directory.CreateDirectory(StateDir); Directory.CreateDirectory(dir);
        File.WriteAllText(VcrStateFile, JsonSerializer.Serialize(new VcrState("record", name, dir), AdvancedJsonContext.Default.VcrState));
        Console.Out.WriteLine($"VCR 录制模式已启动，cassette={name}，目录={dir}");
        return Task.FromResult(0);
    }

    private static Task<int> VcrPlaybackAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "cassette_name", "vcr_playback");
        var dir = Required(o, "cassette_directory", "vcr_playback");
        var cassette = Path.Combine(dir, name);
        if (!File.Exists(cassette)) { Console.Out.WriteLine($"cassette 不存在: {cassette}"); return Task.FromResult(0); }
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(VcrStateFile, JsonSerializer.Serialize(new VcrState("playback", name, dir), AdvancedJsonContext.Default.VcrState));
        Console.Out.WriteLine($"VCR 回放模式已启动，cassette={name}");
        return Task.FromResult(0);
    }

    private static Task<int> VcrStatusAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        if (!File.Exists(VcrStateFile)) { Console.Out.WriteLine("VCR 状态: off（未启用）"); return Task.FromResult(0); }
        var st = JsonSerializer.Deserialize(File.ReadAllText(VcrStateFile), AdvancedJsonContext.Default.VcrState);
        Console.Out.WriteLine($"VCR 状态: {st?.Mode ?? "off"} cassette={st?.Cassette ?? "-"} 目录={st?.Directory ?? "-"}");
        return Task.FromResult(0);
    }

    // ==================== 第三批 DTO / JsonContext 扩展 ====================

    private sealed class UndoOp {
        public string Kind { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public string? Text { get; set; }
        public bool Succeeded { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public UndoOp() { }
        public UndoOp(string kind, int x, int y, string? text, bool succeeded, DateTimeOffset ts) {
            Kind = kind; X = x; Y = y; Text = text; Succeeded = succeeded; Timestamp = ts;
        }
    }
    private sealed class MacroFile {
        public string? Name { get; set; }
        public List<MacroStep>? Operations { get; set; }
    }

    [JsonSerializable(typeof(RecordingSession))]
    [JsonSerializable(typeof(List<MacroStep>))]
    [JsonSerializable(typeof(MacroFile))]
    [JsonSerializable(typeof(List<UndoOp>))]
    [JsonSerializable(typeof(VoiceState))]
    [JsonSerializable(typeof(VcrState))]
    [JsonSerializable(typeof(ImageDescResult))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    private sealed partial class AdvancedJsonContext : JsonSerializerContext;
}
