"""调试：列出所有可见按钮文本"""
import sys, time, os
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

# 列出所有可见文本控件
texts = set()
for c in win.descendants():
    try:
        t = c.window_text().strip()
        r = c.rectangle()
        if t and r.width() > 5 and r.height() > 3 and len(t) < 50:
            texts.add(t)
    except:
        pass

for t in sorted(texts):
    print(f"  [{t}]")

app.kill()
