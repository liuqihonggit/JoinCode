"""黑盒 UI 自动化测试 v4 — click_input 点击后检查 Side Bar 内容变化"""
import time
from pywinauto import Application

EXE_PATH = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"

def dump_sidebar(win, label):
    """打印 Side Bar 区域(L264~R500)的所有控件"""
    print(f"\n  === {label} ===")
    try:
        all_ctrls = win.descendants()
        sidebar_ctrls = []
        for c in all_ctrls:
            r = c.rectangle()
            if r.left >= 260 and r.right <= 510 and r.top > 270 and r.bottom < 1060 and r.width() > 0:
                sidebar_ctrls.append((c, r))
        print(f"  Side Bar 区域有 {len(sidebar_ctrls)} 个可见控件:")
        for i, (c, r) in enumerate(sidebar_ctrls[:30]):
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

    dump_sidebar(win, "初始状态(Sessions 面板)")

    print("\n[2] 用 click_input 点击 📁")
    btn = win.child_window(title="📁", control_type="Button")
    if not btn.exists(timeout=2):
        print("    [FAIL] 📁 未找到")
        app.kill()
        return

    try:
        btn.click_input()
        print("    [OK] click_input 成功")
    except Exception as e:
        print(f"    [FAIL] {e}")
    time.sleep(2)

    dump_sidebar(win, "点击 📁 后")

    print("\n[3] 检查 TreeView 控件")
    try:
        trees = win.descendants(control_type="Tree")
        print(f"    共 {len(trees)} 个 Tree 控件")
        for i, t in enumerate(trees):
            r = t.rectangle()
            print(f"    Tree[{i}]: rect={r}, visible={r.width() > 0 and r.height() > 0}")
            if r.width() > 0:
                items = t.descendants(control_type="TreeItem")
                print(f"      {len(items)} 个 TreeItem")
                for j, item in enumerate(items[:10]):
                    print(f"        [{j}] {item.window_text()}")
    except Exception as e:
        print(f"    [FAIL] {e}")

    print("\n[4] 检查所有 Text 控件中是否有'目录树'文字")
    try:
        texts = win.descendants(control_type="Text")
        for t in texts:
            name = t.window_text() or ""
            if "目录" in name or "文件" in name or "📂" in name:
                print(f"    [FOUND] '{name}' at {t.rectangle()}")
    except Exception as e:
        print(f"    [FAIL] {e}")

    print("\n[5] 清理")
    try:
        app.kill()
    except Exception:
        pass

if __name__ == "__main__":
    main()
