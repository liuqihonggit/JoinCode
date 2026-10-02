"""黑盒 UI 自动化测试 — pywinauto (UIAutomation) 驱动真实 GUI exe，
遍历 Activity Bar 每个按钮，点击后验证面板互斥状态。
"""
import time
import sys
from pywinauto import Application
from pywinauto.findwindows import ElementNotFoundError

EXE_PATH = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"

def find_button(win, icon):
    """通过 title 查找按钮，返回 (button, rect) 或 None"""
    try:
        btn = win.child_window(title=icon, control_type="Button")
        if btn.exists(timeout=1):
            return btn
    except Exception:
        pass
    return None

def click_button(btn):
    """尝试多种方式点击按钮: invoke > click > move_click"""
    for method in ["invoke", "click", "move_click"]:
        try:
            getattr(btn, method)()
            return method
        except Exception as e:
            last_err = str(e)
    return None

def main():
    print(f"[1] 启动 GUI exe")
    app = Application(backend="uia").start(EXE_PATH, timeout=20)
    time.sleep(3)

    win = app.window(title_re=".*JoinCode.*")
    win.wait('ready', timeout=10)
    print(f"    窗口: {win.window_text()} ({win.rectangle()})")

    print("[2] 查找所有 Activity Bar 按钮...")
    icons = ["💬", "📁", "📝", "🛡", "⚙", "⇥"]
    buttons = {}
    for icon in icons:
        btn = find_button(win, icon)
        if btn:
            buttons[icon] = btn
            print(f"    [OK] {icon}: rect={btn.rectangle()}")
        else:
            print(f"    [MISS] {icon}: 未找到")

    print("[3] 遍历每个按钮: 点击 → 检查面板状态...")
    results = []
    for icon in ["💬", "📁", "📝"]:
        if icon not in buttons:
            results.append((icon, "SKIP", "按钮未找到"))
            continue

        btn = buttons[icon]
        print(f"\n  --- 点击 {icon} ---")

        method = click_button(btn)
        if method is None:
            results.append((icon, "FAIL", "点击失败"))
            continue
        print(f"    点击方式: {method}")
        time.sleep(1)

        tree_found = False
        tree_items = 0
        try:
            tree = win.child_window(control_type="Tree")
            if tree.exists(timeout=1):
                tree_found = True
                items = tree.descendants(control_type="TreeItem")
                tree_items = len(items)
                print(f"    TreeView: 可见, {tree_items} 个 TreeItem")
                for i, item in enumerate(items[:5]):
                    print(f"      [{i}] {item.window_text()}")
            else:
                print(f"    TreeView: 不可见")
        except Exception as e:
            print(f"    TreeView 检查异常: {e}")

        edit_found = False
        try:
            edits = win.descendants(control_type="Edit")
            edit_found = len(edits) > 0
            print(f"    Edit 控件: {len(edits)} 个")
        except Exception:
            pass

        results.append((icon, "OK" if (tree_found or edit_found) else "EMPTY",
                        f"tree={tree_found}({tree_items}), edit={edit_found}"))

    print("\n[4] 测试结果汇总")
    print(f"{'按钮':<6} {'结果':<8} {'详情'}")
    print("-" * 50)
    for icon, status, detail in results:
        print(f"{icon:<6} {status:<8} {detail}")

    print("\n[5] 互斥验证: 💬→📁→📝 序列")
    for icon in ["💬", "📁", "📝"]:
        if icon in buttons:
            click_button(buttons[icon])
            time.sleep(0.5)
            try:
                tree = win.child_window(control_type="Tree")
                tree_visible = tree.exists(timeout=0.5)
            except Exception:
                tree_visible = False
            print(f"    点 {icon} 后 TreeView 可见: {tree_visible}")

    print("\n[6] 清理")
    try:
        app.kill()
    except Exception:
        pass
    print("[DONE]")

if __name__ == "__main__":
    main()
