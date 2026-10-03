"""调试：列出所有按钮的文本"""
import sys, time
sys.path.insert(0, r'D:\project\w1\tool')
from pywinauto import Desktop

desktop = Desktop(backend="uia")
win = desktop.window(title="JoinCode · AI 编程工作区")
win.wait('ready', timeout=10)
win.set_focus()
time.sleep(0.5)

print("=== 所有 Button 控件 ===")
for i, b in enumerate(win.descendants(control_type="Button")):
    try:
        t = b.window_text()
        r = b.rectangle()
        vis = r.width() > 2 and r.height() > 2
        print(f"[{i}] text={repr(t)} visible={vis} rect=({r.left},{r.top},{r.right},{r.bottom})")
    except:
        pass

print("\n=== 所有含 emoji 的控件 ===")
for c in win.descendants():
    try:
        t = c.window_text()
        if t and len(t) <= 3 and t != "":
            r = c.rectangle()
            vis = r.width() > 2 and r.height() > 2
            if vis:
                print(f"text={repr(t)} type={c.element_info.control_type}")
    except:
        pass
