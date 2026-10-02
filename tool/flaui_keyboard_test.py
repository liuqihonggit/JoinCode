"""黑盒测试 v5 — 尝试键盘激活按钮 + 验证点击坐标"""
import time
from pywinauto import Application
from pywinauto.keyboard import send_keys

EXE_PATH = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"

def dump_sidebar(win, label):
    print(f"\n  === {label} ===")
    try:
        all_ctrls = win.descendants()
        sidebar_ctrls = []
        for c in all_ctrls:
            r = c.rectangle()
            if r.left >= 370 and r.right <= 520 and r.top > 350 and r.bottom < 900 and r.width() > 0:
                sidebar_ctrls.append((c, r))
        print(f"  Side Bar 区域有 {len(sidebar_ctrls)} 个可见控件:")
        for i, (c, r) in enumerate(sidebar_ctrls[:15]):
            name = (c.window_text() or "")[:40]
            ctype = c.element_info.control_type
            print(f"    [{i}] {ctype}: '{name}' rect={r}")
    except Exception as e:
        print(f"  [FAIL] {e}")

def main():
    print("[1] 启动 GUI exe")
    app = Application(backend="uia").start(EXE_PATH, timeout=20)
    time.sleep(3)
    win = app.window(title_re=".*JoinCode.*")
    win.wait('ready', timeout=10)
    print(f"    窗口: {win.window_text()} ({win.rectangle()})")

    btn = win.child_window(title="📁", control_type="Button")
    if not btn.exists(timeout=2):
        print("    [FAIL] 📁 未找到")
        app.kill()
        return

    rect = btn.rectangle()
    print(f"    📁 rect={rect}")
    print(f"    📁 enabled={btn.is_enabled()}, visible={btn.is_visible()}")

    dump_sidebar(win, "初始状态")

    # 方式1: set_focus + click_input
    print("\n[2] set_focus + click_input")
    try:
        btn.set_focus()
        time.sleep(0.5)
        btn.click_input()
        print("    [OK] click_input 成功")
    except Exception as e:
        print(f"    [FAIL] {e}")
    time.sleep(1)
    dump_sidebar(win, "click_input 后")

    # 方式2: set_focus + Space 键
    print("\n[3] set_focus + Space 键")
    try:
        btn.set_focus()
        time.sleep(0.5)
        send_keys("{SPACE}")
        print("    [OK] Space 键已发送")
    except Exception as e:
        print(f"    [FAIL] {e}")
    time.sleep(1)
    dump_sidebar(win, "Space 键后")

    # 方式3: set_focus + Enter 键
    print("\n[4] set_focus + Enter 键")
    try:
        btn.set_focus()
        time.sleep(0.5)
        send_keys("{ENTER}")
        print("    [OK] Enter 键已发送")
    except Exception as e:
        print(f"    [FAIL] {e}")
    time.sleep(1)
    dump_sidebar(win, "Enter 键后")

    # 检查 TreeView
    print("\n[5] 检查 TreeView")
    try:
        trees = win.descendants(control_type="Tree")
        print(f"    共 {len(trees)} 个 Tree 控件")
        texts = win.descendants(control_type="Text")
        for t in texts:
            name = t.window_text() or ""
            if "目录" in name or "📂" in name:
                print(f"    [FOUND] '{name}' at {t.rectangle()}")
    except Exception as e:
        print(f"    [FAIL] {e}")

    print("\n[6] 清理")
    try:
        app.kill()
    except Exception:
        pass

if __name__ == "__main__":
    main()
