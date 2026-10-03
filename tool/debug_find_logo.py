"""查找 ◆ 残留位置"""
import sys, time
sys.path.insert(0, r'D:\project\w1\tool')
from pywinauto import Desktop

desktop = Desktop(backend="uia")
win = desktop.window(title="JoinCode · AI 编程工作区")
win.wait('ready', timeout=10)
win.set_focus()
time.sleep(0.5)

for c in win.descendants():
    try:
        t = c.window_text()
        if t and "◆" in t:
            r = c.rectangle()
            print(f"◆ found: text={repr(t)} type={c.element_info.control_type} rect=({r.left},{r.top},{r.right},{r.bottom})")
    except:
        pass

# 也检查 "JoinCode" 文本
print("\n--- JoinCode 文本 ---")
for c in win.descendants():
    try:
        t = c.window_text()
        if t and "JoinCode" in t and len(t) < 30:
            r = c.rectangle()
            print(f"JoinCode: text={repr(t)} type={c.element_info.control_type} rect=({r.left},{r.top},{r.right},{r.bottom})")
    except:
        pass
