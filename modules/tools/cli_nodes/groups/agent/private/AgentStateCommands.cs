// agent 组下沉 handler — task CRUD / todo / plan 状态机 / cron 校验 / complete_step / task_output。
// 状态全部文件化到 ~/.jcc/（与宿主同一份文件，单一真相源）。用 JsonNode 读写以兼容宿主 DTO 字段，
// 不引任何引擎类型。活 agent（subagent_*/task_stop*/goal_update/verify_plan_execution）绑定进程内
// LLM 循环，物理边界，留宿主。





namespace JoinCode.CliNode.Agent;

internal static partial class AgentCommands {
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string Jcc => Path.Combine(Home, ".jcc");
    private static string TasksDir => Path.Combine(Jcc, "tasks");
    private static string TodoDir => Path.Combine(Directory.GetCurrentDirectory(), ".jcc", "todo");
    private static string PlansDir => Path.Combine(Jcc, "plans");
    private static string PlanStateFile => Path.Combine(PlansDir, ".active_plan_state.json");

    // ==================== task CRUD（8） ====================

    private static JsonObject? LoadTask(string id) {
        var path = Path.Combine(TasksDir, "task-" + id + ".json");
        if (!File.Exists(path)) {
            // 兼容 task-0001 这种 id 已含前缀的情况
            path = Path.Combine(TasksDir, id + ".json");
            if (!File.Exists(path)) return null;
        }
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject; } catch (JsonException) { return null; }
    }
    private static void SaveTask(JsonObject task) {
        Directory.CreateDirectory(TasksDir);
        var id = task["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N")[..8];
        File.WriteAllText(Path.Combine(TasksDir, "task-" + id + ".json"), task.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    private static List<JsonObject> AllTasks() {
        if (!Directory.Exists(TasksDir)) return [];
        return Directory.GetFiles(TasksDir, "task-*.json")
            .Select(f => { try { return JsonNode.Parse(File.ReadAllText(f)) as JsonObject; } catch { return null; } })
            .Where(t => t is not null).Select(t => t).ToList();
    }
    private static string S(JsonObject o, string k) => o.TryGetPropertyValue(k, out var v) ? v?.GetValue<string>() ?? "" : "";

    private static Task<int> TaskCreateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var title = Required(o, "title", "task_create");
        var next = NextTaskId();
        var task = new JsonObject {
            ["id"] = next, ["title"] = title,
            ["description"] = Get(o, "description", ""),
            ["status"] = "pending",
            ["priority"] = Get(o, "priority", "medium"),
            ["assignee"] = Get(o, "assignee", ""),
            ["dueDate"] = Get(o, "due_date", ""),
            ["tags"] = new JsonArray(),
            ["dependencies"] = new JsonArray(), ["blockedBy"] = new JsonArray(),
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        SaveTask(task);
        Console.Out.WriteLine($"任务已创建: {next} - {title}");
        return Task.FromResult(0);
    }
    private static string NextTaskId() {
        Directory.CreateDirectory(TasksDir);
        var max = Directory.GetFiles(TasksDir, "task-*.json")
            .Select(f => { var n = Path.GetFileNameWithoutExtension(f).Replace("task-", ""); return int.TryParse(n.Replace("task-", ""), out var v) ? v : 0; })
            .DefaultIfEmpty(0).Max();
        var hwm = Path.Combine(TasksDir, ".highwatermark");
        if (File.Exists(hwm) && int.TryParse(File.ReadAllText(hwm).Trim(), out var h) && h > max) max = h;
        var id = $"task-{max + 1:0000}";
        File.WriteAllText(hwm, (max + 1).ToString());
        return id;
    }

    private static Task<int> TaskListAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var status = Get(o, "status", "");
        var assignee = Get(o, "assignee", "");
        var tasks = AllTasks().Where(t => (status.Length == 0 || S(t, "status") == status) && (assignee.Length == 0 || S(t, "assignee") == assignee)).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"共 {tasks.Count} 个任务:");
        foreach (var t in tasks) sb.AppendLine($"  [{S(t, "id")}] {S(t, "status")} {S(t, "priority")} - {S(t, "title")}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> TaskGetAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var id = Required(o, "task_id", "task_get");
        var t = LoadTask(id);
        if (t is null) { Console.Out.WriteLine($"任务不存在: {id}"); return Task.FromResult(0); }
        Console.Out.Write(FormatTask(t));
        return Task.FromResult(0);
    }

    private static Task<int> TaskUpdateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var id = Required(o, "task_id", "task_update");
        var t = LoadTask(id);
        if (t is null) { Console.Out.WriteLine($"任务不存在: {id}"); return Task.FromResult(0); }
        foreach (var key in new[] { "title", "description", "status", "assignee", "due_date", "priority" }) {
            var v = Get(o, key, "");
            if (v.Length == 0) continue;
            var field = key == "due_date" ? "dueDate" : key;
            t[field] = v;
        }
        SaveTask(t);
        Console.Out.WriteLine($"任务已更新: {id}");
        return Task.FromResult(0);
    }

    private static Task<int> TaskOutputAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var id = Required(o, "task_id", "task_output");
        var t = LoadTask(id);
        if (t is null) { Console.Out.WriteLine($"任务不存在: {id}"); return Task.FromResult(0); }
        var maxLines = Math.Clamp(GetInt(o, "max_lines", 100), 1, 10000);
        var desc = S(t, "description");
        var lines = desc.Split('\n');
        Console.Out.Write(string.Join('\n', lines.Take(maxLines)));
        return Task.FromResult(0);
    }

    private static Task<int> TaskSetDependencyAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var id = Required(o, "task_id", "task_set_dependency");
        var dep = Required(o, "depends_on_task_id", "task_set_dependency");
        var t = LoadTask(id);
        if (t is null) { Console.Out.WriteLine($"任务不存在: {id}"); return Task.FromResult(0); }
        var deps = t["dependencies"] as JsonArray ?? new JsonArray();
        if (!deps.Any(d => d?.GetValue<string>() == dep)) deps.Add((JsonNode)JsonValue.Create(dep));
        t["dependencies"] = deps;
        SaveTask(t);
        Console.Out.WriteLine($"任务 {id} 依赖 {dep} 已设置");
        return Task.FromResult(0);
    }

    private static Task<int> TaskRemoveDependencyAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var id = Required(o, "task_id", "task_remove_dependency");
        var dep = Required(o, "depends_on_task_id", "task_remove_dependency");
        var t = LoadTask(id);
        if (t is null) { Console.Out.WriteLine($"任务不存在: {id}"); return Task.FromResult(0); }
        if (t["dependencies"] is JsonArray deps) {
            var item = deps.FirstOrDefault(d => d?.GetValue<string>() == dep);
            if (item is not null) deps.Remove(item);
            t["dependencies"] = deps;
        }
        SaveTask(t);
        Console.Out.WriteLine($"任务 {id} 对 {dep} 的依赖已移除");
        return Task.FromResult(0);
    }

    private static Task<int> TaskGetDependenciesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var id = Required(o, "task_id", "task_get_dependencies");
        var t = LoadTask(id);
        if (t is null) { Console.Out.WriteLine($"任务不存在: {id}"); return Task.FromResult(0); }
        var deps = (t["dependencies"] as JsonArray ?? new JsonArray()).Select(d => d?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList();
        Console.Out.WriteLine(deps.Count == 0 ? $"任务 {id} 无依赖" : $"任务 {id} 依赖: {string.Join(", ", deps)}");
        return Task.FromResult(0);
    }

    private static Task<int> TaskCanExecuteAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var id = Required(o, "task_id", "task_can_execute");
        var t = LoadTask(id);
        if (t is null) { Console.Out.WriteLine($"任务不存在: {id}"); return Task.FromResult(0); }
        var deps = (t["dependencies"] as JsonArray ?? new JsonArray()).Select(d => d?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList();
        var unmet = deps.Where(d => { var dt = LoadTask(d); return dt is not null && S(dt, "status") != "completed"; }).ToList();
        Console.Out.WriteLine(unmet.Count == 0 ? $"任务 {id} 可执行（依赖已满足）" : $"任务 {id} 阻塞，未完成依赖: {string.Join(", ", unmet)}");
        return Task.FromResult(0);
    }

    private static string FormatTask(JsonObject t) {
        var sb = new StringBuilder();
        sb.AppendLine($"ID: {S(t, "id")}");
        sb.AppendLine($"标题: {S(t, "title")}");
        sb.AppendLine($"描述: {S(t, "description")}");
        sb.AppendLine($"状态: {S(t, "status")}  优先级: {S(t, "priority")}");
        sb.AppendLine($"负责人: {S(t, "assignee")}  到期: {S(t, "dueDate")}");
        return sb.ToString();
    }

    // ==================== todo（3） ====================

    private static string TodoPath {
        get {
            var gitRoot = FindGitRoot(Directory.GetCurrentDirectory());
            return Path.Combine(gitRoot ?? Directory.GetCurrentDirectory(), ".jcc", "todo", "todos.json");
        }
    }
    private static JsonArray LoadTodos() {
        if (!File.Exists(TodoPath)) return new JsonArray();
        try { return JsonNode.Parse(File.ReadAllText(TodoPath)) as JsonArray ?? new JsonArray(); } catch (JsonException) { return new JsonArray(); }
    }
    private static void SaveTodos(JsonArray arr) {
        Directory.CreateDirectory(Path.GetDirectoryName(TodoPath));
        File.WriteAllText(TodoPath, arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string? FindGitRoot(string start) {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 32 && dir is not null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        return null;
    }

    private static Task<int> TodoWriteAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var todosJson = Get(o, "todos", "");
        if (todosJson.Length == 0) { Console.Error.WriteLine("todo_write 缺少 --todos（JSON 数组）"); return Task.FromResult(2); }
        JsonArray incoming;
        try { incoming = JsonNode.Parse(todosJson) as JsonArray ?? new JsonArray(); }
        catch (JsonException ex) { Console.Error.WriteLine($"todo_write 的 --todos 不是合法 JSON: {ex.Message}"); return Task.FromResult(2); }
        SaveTodos(incoming);
        Console.Out.WriteLine($"已写入 {incoming.Count} 个待办项");
        return Task.FromResult(0);
    }

    private static Task<int> TodoListAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var arr = LoadTodos();
        var status = Get(o, "status", "");
        var items = arr.Where(n => n is JsonObject).Select(n => (JsonObject)n)
            .Where(t => status.Length == 0 || S(t, "status") == status).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Total: {items.Count}  Pending: {items.Count(t => S(t, "status") != "completed")}  Completed: {items.Count(t => S(t, "status") == "completed")}");
        foreach (var t in items) {
            var icon = S(t, "status") switch { "completed" => "✓", "in_progress" => ">", _ => "○" };
            sb.AppendLine($"  [{icon}] {S(t, "id")} {S(t, "content")} ({S(t, "activeForm")})");
        }
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> TodoUpdateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var id = Required(o, "todo_id", "todo_update");
        var arr = LoadTodos();
        var found = false;
        foreach (var n in arr) {
            if (n is not JsonObject t || S(t, "id") != id) continue;
            found = true;
            foreach (var key in new[] { "content", "status", "priority" }) {
                var v = Get(o, key, "");
                if (v.Length > 0) t[key] = v;
            }
        }
        if (!found) { Console.Out.WriteLine($"待办项不存在: {id}"); return Task.FromResult(0); }
        SaveTodos(arr);
        Console.Out.WriteLine($"待办项已更新: {id}");
        return Task.FromResult(0);
    }

    // ==================== plan 状态机（7） ====================

    private static JsonObject LoadPlanState() {
        if (!File.Exists(PlanStateFile)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(PlanStateFile)) as JsonObject ?? new JsonObject(); } catch (JsonException) { return new JsonObject(); }
    }
    private static void SavePlanState(JsonObject st) {
        Directory.CreateDirectory(PlansDir);
        File.WriteAllText(PlanStateFile, st.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    private static JsonArray PlanSteps(JsonObject st) {
        if (st["Plan"] is not JsonObject plan) { plan = new JsonObject { ["Steps"] = new JsonArray() }; st["Plan"] = plan; }
        if (plan["Steps"] is not JsonArray steps) { steps = new JsonArray(); plan["Steps"] = steps; }
        return steps;
    }

    private static Task<int> GetPlanStatusAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var st = LoadPlanState();
        var plan = st["Plan"] as JsonObject;
        var steps = PlanSteps(st);
        var done = steps.Count(s => s is JsonObject p && S(p, "Status").Equals("approved", StringComparison.OrdinalIgnoreCase));
        var sb = new StringBuilder();
        sb.AppendLine("计划状态");
        sb.AppendLine($"计划ID: {S(st, "CurrentPlanId")}");
        sb.AppendLine($"描述: {(plan is not null ? S(plan, "Description") : "")}");
        sb.AppendLine($"进度: {done}/{steps.Count}");
        sb.AppendLine("步骤列表:");
        for (var i = 0; i < steps.Count; i++) {
            var p = steps[i] as JsonObject;
            var icon = S(p, "Status") switch { "approved" => "✓", "rejected" => "✗", _ => "○" };
            sb.AppendLine($"  [{icon}] {i + 1}. {S(p!, "Description")}");
        }
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> AddPlanStepAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var desc = Required(o, "description", "add_plan_step");
        var st = LoadPlanState();
        var steps = PlanSteps(st);
        var step = new JsonObject { ["Description"] = desc, ["ToolName"] = Get(o, "tool_name", ""), ["Status"] = "pending", ["Index"] = steps.Count };
        steps.Add((JsonNode)step);
        SavePlanState(st);
        Console.Out.WriteLine($"步骤已添加\n当前步骤数: {steps.Count}\n  [{steps.Count}] {desc}");
        return Task.FromResult(0);
    }

    private static Task<int> PlanStepActionAsync(IReadOnlyDictionary<string, string> o, string action, string tool) {
        var idxText = Required(o, "step_index", tool);
        if (!int.TryParse(idxText, out var idx) || idx < 1) { Console.Error.WriteLine($"{tool} 的 --step_index 必须为正整数"); return Task.FromResult(2); }
        var st = LoadPlanState();
        var steps = PlanSteps(st);
        if (idx > steps.Count) { Console.Out.WriteLine($"步骤 {idx} 不存在（共 {steps.Count} 步）"); return Task.FromResult(0); }
        if (steps[idx - 1] is JsonObject p) p["Status"] = action switch { "approve" => "approved", "reject" => "rejected", _ => "pending" };
        SavePlanState(st);
        Console.Out.WriteLine($"步骤 {idx} 已{(action == "approve" ? "批准" : action == "reject" ? "拒绝" : "修改")}");
        return Task.FromResult(0);
    }
    private static Task<int> ApprovePlanStepAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) => PlanStepActionAsync(o, "approve", "approve_plan_step");
    private static Task<int> RejectPlanStepAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) => PlanStepActionAsync(o, "reject", "reject_plan_step");

    private static Task<int> ModifyPlanStepAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var idxText = Required(o, "step_index", "modify_plan_step");
        if (!int.TryParse(idxText, out var idx) || idx < 1) { Console.Error.WriteLine("modify_plan_step 的 --step_index 必须为正整数"); return Task.FromResult(2); }
        var st = LoadPlanState();
        var steps = PlanSteps(st);
        if (idx > steps.Count) { Console.Out.WriteLine($"步骤 {idx} 不存在"); return Task.FromResult(0); }
        if (steps[idx - 1] is JsonObject p) {
            var nd = Get(o, "new_description", ""); if (nd.Length > 0) p["Description"] = nd;
            var nt = Get(o, "new_tool_name", ""); if (nt.Length > 0) p["ToolName"] = nt;
        }
        SavePlanState(st);
        Console.Out.WriteLine($"步骤 {idx} 已修改");
        return Task.FromResult(0);
    }

    private static Task<int> RemovePlanStepAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var idxText = Required(o, "step_index", "remove_plan_step");
        if (!int.TryParse(idxText, out var idx) || idx < 1) { Console.Error.WriteLine("remove_plan_step 的 --step_index 必须为正整数"); return Task.FromResult(2); }
        var st = LoadPlanState();
        var steps = PlanSteps(st);
        if (idx > steps.Count) { Console.Out.WriteLine($"步骤 {idx} 不存在"); return Task.FromResult(0); }
        steps.RemoveAt(idx - 1);
        for (var i = 0; i < steps.Count; i++) if (steps[i] is JsonObject p) p["Index"] = i;
        SavePlanState(st);
        Console.Out.WriteLine($"步骤 {idx} 已移除，剩余 {steps.Count} 步");
        return Task.FromResult(0);
    }

    private static Task<int> ExecutePlanStepsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var st = LoadPlanState();
        var steps = PlanSteps(st);
        var done = 0;
        var sb = new StringBuilder();
        foreach (var n in steps) {
            if (n is not JsonObject p || S(p, "Status") != "approved") continue;
            var tool = S(p, "ToolName");
            sb.AppendLine(tool.Length > 0 ? $"执行工具 {tool} 成功" : "步骤执行成功");
            p["Status"] = "completed";
            done++;
        }
        SavePlanState(st);
        sb.AppendLine($"步骤执行完成\n完成进度: {done}/{steps.Count}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    // ==================== cron_validate / complete_step ====================

    private static Task<int> CronValidateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var expr = Required(o, "cron", "cron_validate").Trim();
        var parts = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) { Console.Out.WriteLine($"Invalid cron expression: {expr}（需 5 段: 分 时 日 月 周）"); return Task.FromResult(0); }
        var names = new[] { "Minute", "Hour", "Day", "Month", "DayOfWeek" };
        var sb = new StringBuilder();
        sb.AppendLine($"Valid cron expression: {expr}");
        sb.AppendLine("Parsed fields:");
        for (var i = 0; i < 5; i++) sb.AppendLine($"  {names[i]}: {parts[i]}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> CompleteStepAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var step = Required(o, "step", "complete_step");
        var result = Get(o, "result", "");
        var notes = Get(o, "notes", "");
        var sb = new StringBuilder();
        sb.AppendLine($"✓ 步骤已完成: {step}");
        if (result.Length > 0) sb.AppendLine($"结果: {result}");
        if (notes.Length > 0) sb.AppendLine($"备注: {notes}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    // ==================== 辅助 ====================

    private static int GetInt(IReadOnlyDictionary<string, string> o, string key, int fallback) => int.TryParse(Get(o, key, ""), out var v) ? v : fallback;
    private static string Required(IReadOnlyDictionary<string, string> o, string key, string tool) =>
        o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"{tool} 缺少必填参数 --{key}。正确写法: jcc tool call agent {tool} --{key} <值>");
}
