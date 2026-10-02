"""黑盒验证：窗口前置后点击 📁 按钮验证目录树出现 + 窗口居中 + 互斥。"""
import sys, time, os, subprocess, ctypes
sys.path.insert(0, os.path.join(os.path.dirname(__file__)))
from pywinauto import Application

EXE = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"

def bring_to_front(win):
    """强制窗口前置"""
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

def main():
    print("=== 黑盒验证：📁 按钮 + 窗口居中 + 互斥 ===")
    subprocess.run(["taskkill", "/im", "JoinCode.Gui.exe", "/f"], capture_output=True)
    time.sleep(1)

    app = Application(backend="uia").start(EXE)
    time.sleep(4)
    win = app.top_window()
    bring_to_front(win)

    # 1. 验证窗口居中
    rect = win.rectangle()
    screen_w = ctypes.windll.user32.GetSystemMetrics(0)
    screen_h = ctypes.windll.user32.GetSystemMetrics(1)
    dx = abs((rect.left + rect.right) / 2 - screen_w / 2)
    dy = abs((rect.top + rect.bottom) / 2 - screen_h / 2)
    print(f"{'✅' if dx < 50 and dy < 50 else '⚠️'} 窗口居中 (偏差 dx={dx:.0f} dy={dy:.0f})")

    # 2. 点击 📁 按钮
    btn = find_button(win, "📁")
    if not btn:
        print("❌ 找不到 📁 按钮")
        app.kill()
        return
    btn.click_input()
    time.sleep(1.5)
    print("✅ 点击 📁 按钮")

    # 3. 验证目录树出现
    tree_items = win.descendants(control_type="TreeItem")
    texts = []
    for d in win.descendants():
        try:
            t = d.window_text() or ""
            if t and len(t) > 1:
                texts.append(t)
        except Exception:
            pass
    has_tree = any("目录树" in t for t in texts)

    if tree_items and has_tree:
        print(f"✅ 目录树已打开! {len(tree_items)} 个 TreeItem")
    elif has_tree:
        print("⚠️ 目录树面板出现但 TreeItem 为空")
    else:
        print("❌ 目录树未出现")

    # 4. 互斥验证：点击 💬 后目录树消失
    btn2 = find_button(win, "💬")
    if btn2:
        btn2.click_input()
        time.sleep(1)
        tree_items2 = win.descendants(control_type="TreeItem")
        has_sessions = any("会话" in (d.window_text() or "") for d in win.descendants())
        print(f"{'✅' if not tree_items2 and has_sessions else '⚠️'} 点击 💬 后目录树消失, Sessions 出现")

    # 5. 再点 📁 切回目录树
    btn3 = find_button(win, "📁")
    if btn3:
        btn3.click_input()
        time.sleep(1)
        tree_items3 = win.descendants(control_type="TreeItem")
        print(f"{'✅' if tree_items3 else '⚠️'} 再点 📁 目录树重新出现 ({len(tree_items3)} TreeItem)")

    app.kill()
    print("=== 验证结束 ===")

if __name__ == "__main__":
    main()
