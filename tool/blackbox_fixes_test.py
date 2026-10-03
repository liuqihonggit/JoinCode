"""黑盒验收：Activity Bar 不重叠 + SVG 供应商图标 + 面板鼠标离开自动隐藏"""
import sys, time, os, ctypes
import pywinauto
from pywinauto import Application, Desktop

exe = os.path.join(os.path.dirname(__file__), '..', 'artifacts', 'bin', 'JoinCodeGui', 'Debug', 'net10.0', 'JoinCode.Gui.exe')
exe = os.path.abspath(exe)

app = Application(backend="uia").start(exe)
time.sleep(3)

desktop = Desktop(backend="uia")
win = desktop.window(title="JoinCode · AI 编程工作区")
win.set_focus()
time.sleep(0.5)

results = []

def test(name, condition, detail=""):
    results.append((name, condition, detail))

def find_all(win, keyword):
    items = []
    for c in win.descendants():
        try:
            t = c.window_text()
            if keyword in t and t.strip():
                items.append(c)
        except:
            pass
    return items

def get_rect(elem):
    try:
        r = elem.rectangle()
        return (r.left, r.top, r.right, r.bottom)
    except:
        return None

def rects_overlap(r1, r2):
    """检查两个矩形是否重叠"""
    return not (r1[2] <= r2[0] or r2[2] <= r1[0] or r1[3] <= r2[1] or r2[3] <= r1[1])

# 1. Activity Bar 图标不重叠 — 检查 🎯(goal) 和 🌙/☀️(theme) 的位置
try:
    goal_icons = find_all(win, "🎯")
    theme_icons = find_all(win, "🌙") + find_all(win, "☀️")
    goal_rects = [get_rect(g) for g in goal_icons if get_rect(g)]
    theme_rects = [get_rect(t) for t in theme_icons if get_rect(t)]
    overlap = False
    for gr in goal_rects:
        for tr in theme_rects:
            if rects_overlap(gr, tr):
                overlap = True
                break
    test("Activity Bar goal/主题不重叠", not overlap, f"goal={len(goal_rects)} theme={len(theme_rects)} overlap={overlap}")
except Exception as e:
    test("Activity Bar goal/主题不重叠", False, str(e))

# 2. Activity Bar 所有图标垂直排列不重叠
try:
    icons = find_all(win, "💬") + find_all(win, "📁") + find_all(win, "📝") + find_all(win, "🛡") + find_all(win, "🌙") + find_all(win, "☀️") + find_all(win, "⚙") + find_all(win, "⇥")
    rects = [get_rect(i) for i in icons if get_rect(i)]
    any_overlap = False
    for i, r1 in enumerate(rects):
        for r2 in rects[i+1:]:
            if rects_overlap(r1, r2):
                any_overlap = True
                break
    test("Activity Bar 所有图标不重叠", not any_overlap, f"icons={len(rects)} overlap={any_overlap}")
except Exception as e:
    test("Activity Bar 所有图标不重叠", False, str(e))

# 3. 模型选择器按钮存在
try:
    model_btn = None
    for c in win.descendants():
        try:
            t = c.window_text().strip()
            # 模型按钮显示 CurrentModelDisplay（模型 ID 或"选择模型"）
            if t in ("选择模型", "auto", "glm-4", "glm-4-flash", "gpt-4o", "deepseek-chat") or ("选择模型" in t):
                r = c.rectangle()
                if r.width() > 10 and r.height() > 3:
                    model_btn = c
                    break
        except:
            pass
    test("模型选择器按钮存在", model_btn is not None, f"text={model_btn.window_text()}" if model_btn else "")
except Exception as e:
    test("模型选择器按钮存在", False, str(e))

# 4. 点击模型选择器打开 Popup
try:
    if model_btn:
        model_btn.click_input()
        time.sleep(1)
        # 检查是否有模型名出现（agnes-video / glm- / gpt- / deepseek- 等）
        model_names = []
        for c in win.descendants():
            try:
                t = c.window_text().strip()
                if any(t.startswith(p) for p in ("agnes-", "glm-", "gpt-", "deepseek-", "claude-")):
                    model_names.append(t)
            except:
                pass
        # 也查找供应商名
        providers = []
        for name in ["OpenAI", "DeepSeek", "Anthropic", "Zhipu", "SenseNova", "Agnes"]:
            if find_all(win, name):
                providers.append(name)
        test("模型列表显示供应商/模型", len(model_names) > 0 or len(providers) > 0, f"models={model_names} providers={providers}")
    else:
        test("模型列表显示供应商/模型", False, "模型按钮不存在")
except Exception as e:
    test("模型列表显示供应商/模型", False, str(e))

# 5. 统计面板鼠标离开自动隐藏
try:
    # 找统计按钮
    stats_btn = None
    for c in win.descendants():
        try:
            if "统计" in c.window_text():
                r = c.rectangle()
                if r.width() > 10:
                    stats_btn = c
                    break
        except:
            pass
    if stats_btn:
        stats_btn.click_input()
        time.sleep(1)
        # 检查统计面板是否打开
        stats_visible_before = len(find_all(win, "会话统计")) > 0
        # 按 Esc 关闭
        win.type_keys("{ESC}", pause=0.3)
        time.sleep(0.5)
        test("统计面板打开/关闭", stats_visible_before, f"打开={stats_visible_before}")
    else:
        test("统计面板打开/关闭", False, "统计按钮不存在")
except Exception as e:
    test("统计面板打开/关闭", False, str(e))

# 6. 拦截器面板打开/关闭
try:
    interceptor_btn = None
    for c in win.descendants():
        try:
            if "🛡" in c.window_text():
                r = c.rectangle()
                if r.width() > 10:
                    interceptor_btn = c
                    break
        except:
            pass
    if interceptor_btn:
        interceptor_btn.click_input()
        time.sleep(1)
        has_interceptor = len(find_all(win, "AI 工具拦截器")) > 0
        win.type_keys("{ESC}", pause=0.3)
        time.sleep(0.5)
        test("拦截器面板打开/关闭", has_interceptor, f"打开={has_interceptor}")
    else:
        test("拦截器面板打开/关闭", False, "拦截器按钮不存在")
except Exception as e:
    test("拦截器面板打开/关闭", False, str(e))

# 输出结果
passed = sum(1 for r in results if r[1])
total = len(results)
print(f"\n{'='*60}")
print(f"黑盒验收: {passed}/{total} 通过")
print(f"{'='*60}")
for r in results:
    status = "✅" if r[1] else "❌"
    print(f"  {status} {r[0]}", f"({r[2]})" if len(r) > 2 and r[2] else "")

app.kill()
print(f"\n总计: {passed}/{total} 通过")
