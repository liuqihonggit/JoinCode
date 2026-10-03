"""调试：点击模型按钮后列出所有文本"""
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

# 找模型按钮
model_btn = None
for c in win.descendants():
    try:
        t = c.window_text().strip()
        if t == "auto":
            r = c.rectangle()
            if r.width() > 10 and r.height() > 3:
                model_btn = c
                print(f"找到模型按钮: text={t} rect={r}")
                break
    except:
        pass

if model_btn:
    model_btn.click_input()
    time.sleep(2)
    
    # 列出所有新文本
    texts = set()
    for c in win.descendants():
        try:
            t = c.window_text().strip()
            r = c.rectangle()
            if t and r.width() > 2 and r.height() > 2 and len(t) < 80:
                texts.add(t)
        except:
            pass
    
    print(f"\n点击后可见文本 ({len(texts)} 个):")
    for t in sorted(texts):
        print(f"  [{t}]")
else:
    print("未找到模型按钮")

app.kill()
