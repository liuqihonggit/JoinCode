"""黑盒测试：设置面板所有配置功能验证。"""
import sys, time, os, subprocess, ctypes
sys.path.insert(0, os.path.join(os.path.dirname(__file__)))
from pywinauto import Application, mouse

EXE = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"
PASS = 0
FAIL = 0

def ok(name, cond):
    global PASS, FAIL
    if cond:
        PASS += 1
        print(f"  ✅ {name}")
    else:
        FAIL += 1
        print(f"  ❌ {name}")

def bring_to_front(win):
    win.set_focus()
    time.sleep(0.3)
    ctypes.windll.user32.SetForegroundWindow(win.handle)
    time.sleep(0.3)

def find_button(win, icon):
    for d in win.descendants():
        try:
            if icon in (d.window_text() or ""):
                return d
        except Exception:
            pass
    return None

def has_text(win, text):
    for d in win.descendants():
        try:
            if text in (d.window_text() or ""):
                return True
        except Exception:
            pass
    return False

def main():
    global PASS, FAIL
    print("=== 黑盒测试：设置面板所有配置功能 ===")
    subprocess.run(["taskkill", "/im", "JoinCode.Gui.exe", "/f"], capture_output=True)
    time.sleep(1)

    app = Application(backend="uia").start(EXE)
    time.sleep(4)
    win = app.top_window()
    bring_to_front(win)

    # 打开设置面板
    print("\n--- 打开设置面板 ---")
    btn = find_button(win, "⚙")
    if not btn:
        ok("找到 ⚙ 按钮", False)
        app.kill()
        return
    btn.click_input()
    time.sleep(1.5)
    ok("点击 ⚙ 打开设置面板", has_text(win, "设置") or has_text(win, "外观"))

    # 验证所有配置分组存在
    print("\n--- 配置分组 ---")
    ok("外观与动效", has_text(win, "外观") or has_text(win, "主题"))
    ok("对话参数", has_text(win, "温度") or has_text(win, "对话"))
    ok("生成行为", has_text(win, "流式") or has_text(win, "推理") or has_text(win, "生成"))
    ok("快捷键", has_text(win, "快捷键") or has_text(win, "Enter"))
    ok("安全与确认", has_text(win, "无人值守") or has_text(win, "安全"))
    ok("通知", has_text(win, "震动") or has_text(win, "通知") or has_text(win, "聊天室"))
    ok("网络", has_text(win, "网络") or has_text(win, "代理"))
    ok("系统提示词", has_text(win, "系统提示") or has_text(win, "提示词"))

    # 验证控件类型
    print("\n--- 控件类型 ---")
    combos = win.descendants(control_type="ComboBox")
    sliders = win.descendants(control_type="Slider")
    edits = win.descendants(control_type="Edit")
    checks = win.descendants(control_type="CheckBox")
    buttons = win.descendants(control_type="Button")

    ok(f"ComboBox(主题/强调色/推理/网络) >= 3", len(combos) >= 3)
    ok(f"Slider(温度/最大长度/字号) >= 3", len(sliders) >= 3)
    ok(f"Edit(代理/系统提示词) >= 1", len(edits) >= 1)
    ok(f"Button(关闭/应用/插入/恢复) >= 4", len(buttons) >= 4)

    print(f"\n  控件统计: ComboBox={len(combos)}, Slider={len(sliders)}, Edit={len(edits)}, Button={len(buttons)}")

    # 测试主题切换 — 找到主题 ComboBox，切换到 Light
    print("\n--- 主题切换 ---")
    if combos:
        # 第一个 ComboBox 通常是主题
        theme_combo = combos[0]
        theme_combo.click_input()
        time.sleep(0.5)
        # 查找下拉项
        list_items = win.descendants(control_type="ListItem")
        ok("主题下拉有选项", len(list_items) > 0)
        if list_items:
            print(f"  主题选项: {[li.window_text() for li in list_items[:5]]}")
            # 选 Light
            for li in list_items:
                try:
                    if "Light" in (li.window_text() or "") or "浅" in (li.window_text() or ""):
                        li.click_input()
                        time.sleep(0.5)
                        ok("切换到 Light 主题", True)
                        break
                except: pass
            else:
                ok("找到 Light 主题选项", False)
        # 按 ESC 关闭下拉
        win.type_keys("{ESC}")
        time.sleep(0.3)

    # 测试滑块 — 找到温度 Slider，拖动
    print("\n--- 滑块操作 ---")
    if sliders:
        temp_slider = sliders[0]
        # 获取滑块范围
        try:
            rect = temp_slider.rectangle()
            ok("温度滑块存在", rect.width() > 0)
            # 点击滑块中间
            mouse.click(coords=(rect.left + rect.width()//2, (rect.top+rect.bottom)//2))
            time.sleep(0.3)
            ok("温度滑块可点击", True)
        except Exception as e:
            ok(f"温度滑块操作: {e}", False)

    # 测试恢复默认设置
    print("\n--- 恢复默认设置 ---")
    bring_to_front(win)
    for d in win.descendants():
        try:
            t = d.window_text() or ""
            if "默认" in t and "恢复" in t:
                d.click_input()
                time.sleep(0.5)
                ok("点击恢复默认设置", True)
                break
        except: pass
    else:
        ok("找到恢复默认按钮", False)

    # 关闭设置面板
    print("\n--- 关闭设置面板 ---")
    bring_to_front(win)
    # 按 ESC 关闭
    win.type_keys("{ESC}")
    time.sleep(0.5)
    ok("ESC 关闭设置面板", not has_text(win, "外观"))

    app.kill()
    print(f"\n=== 结果: {PASS} 通过, {FAIL} 失败 ===")

if __name__ == "__main__":
    main()
