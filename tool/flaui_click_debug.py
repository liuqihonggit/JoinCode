"""黑盒 UI 自动化测试 v3 — 打印点击异常详情，尝试坐标点击"""
import time
from pywinauto import Application
import ctypes

EXE_PATH = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"

def click_at(x, y):
    """用 Win32 API 发送鼠标点击到屏幕坐标"""
    ctypes.windll.user32.SetCursorPos(x, y)
    ctypes.windll.user32.mouse_event(0x0002, 0, 0, 0, 0)  # LEFTDOWN
    ctypes.windll.user32.mouse_event(0x0004, 0, 0, 0, 0)  # LEFTUP

def main():
    print("[1] 启动 GUI exe")
    app = Application(backend="uia").start(EXE_PATH, timeout=20)
    time.sleep(3)

    win = app.window(title_re=".*JoinCode.*")
    win.wait('ready', timeout=10)
    print(f"    窗口: {win.window_text()} ({win.rectangle()})")

    print("[2] 查找 📁 按钮并尝试多种点击方式...")
    btn = win.child_window(title="📁", control_type="Button")
    if not btn.exists(timeout=2):
        print("    [FAIL] 📁 按钮未找到")
        app.kill()
        return

    rect = btn.rectangle()
    print(f"    📁 rect={rect}")
    cx, cy = rect.left + (rect.right - rect.left) // 2, rect.top + (rect.bottom - rect.top) // 2
    print(f"    中心点: ({cx}, {cy})")

    # 方式1: invoke()
    print("\n  [方式1] invoke()")
    try:
        btn.invoke()
        print("    [OK] invoke 成功")
    except Exception as e:
        print(f"    [FAIL] {type(e).__name__}: {e}")

    time.sleep(1)

    # 方式2: click()
    print("\n  [方式2] click()")
    try:
        btn.click()
        print("    [OK] click 成功")
    except Exception as e:
        print(f"    [FAIL] {type(e).__name__}: {e}")

    time.sleep(1)

    # 方式3: click_input()
    print("\n  [方式3] click_input()")
    try:
        btn.click_input()
        print("    [OK] click_input 成功")
    except Exception as e:
        print(f"    [FAIL] {type(e).__name__}: {e}")

    time.sleep(1)

    # 方式4: 坐标点击 (Win32 mouse_event)
    print(f"\n  [方式4] Win32 坐标点击 ({cx}, {cy})")
    try:
        click_at(cx, cy)
        print("    [OK] 坐标点击已发送")
    except Exception as e:
        print(f"    [FAIL] {type(e).__name__}: {e}")

    time.sleep(2)

    # 检查 TreeView 是否出现
    print("\n[3] 检查 TreeView 是否出现...")
    try:
        tree = win.child_window(control_type="Tree")
        if tree.exists(timeout=2):
            items = tree.descendants(control_type="TreeItem")
            print(f"    [OK] TreeView 可见, {len(items)} 个 TreeItem")
            for i, item in enumerate(items[:10]):
                print(f"      [{i}] {item.window_text()}")
        else:
            print("    [FAIL] TreeView 不可见")
    except Exception as e:
        print(f"    [FAIL] {type(e).__name__}: {e}")

    # 检查所有 Custom 控件(Side Bar 内容)
    print("\n[4] 检查 Side Bar 区域控件...")
    try:
        customs = win.descendants(control_type="Custom")
        print(f"    共 {len(customs)} 个 Custom 控件")
        for i, c in enumerate(customs[:10]):
            r = c.rectangle()
            print(f"    Custom[{i}]: rect={r}, name='{c.window_text()[:50]}'")
    except Exception as e:
        print(f"    [FAIL] {e}")

    print("\n[5] 清理")
    try:
        app.kill()
    except Exception:
        pass

if __name__ == "__main__":
    main()
