"""黑盒验收：语言切换按钮"""
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

results = []

def test(name, condition, detail=""):
    results.append((name, condition, detail))

# 1. 语言切换按钮存在（"中" 或 "En"）
try:
    lang_btn = None
    for c in win.descendants():
        try:
            t = c.window_text().strip()
            if t in ("中", "En"):
                r = c.rectangle()
                if r.width() > 3 and r.height() > 3:
                    lang_btn = c
                    break
        except:
            pass
    test("语言切换按钮存在", lang_btn is not None, f"text={lang_btn.window_text()}" if lang_btn else "")
except Exception as e:
    test("语言切换按钮存在", False, str(e))

# 2. 点击切换语言
try:
    if lang_btn:
        before = lang_btn.window_text().strip()
        lang_btn.click_input()
        time.sleep(1)
        # 重新查找按钮（文本可能已变）
        after = None
        for c in win.descendants():
            try:
                t = c.window_text().strip()
                if t in ("中", "En"):
                    r = c.rectangle()
                    if r.width() > 3 and r.height() > 3:
                        after = t
                        break
            except:
                pass
        test("点击切换语言", before != after, f"{before} → {after}")
    else:
        test("点击切换语言", False, "按钮不存在")
except Exception as e:
    test("点击切换语言", False, str(e))

# 3. 再次切换恢复
try:
    if lang_btn:
        lang_btn.click_input()
        time.sleep(1)
        restored = None
        for c in win.descendants():
            try:
                t = c.window_text().strip()
                if t in ("中", "En"):
                    r = c.rectangle()
                    if r.width() > 3 and r.height() > 3:
                        restored = t
                        break
            except:
                pass
        test("再次切换恢复", restored is not None, f"restored={restored}")
    else:
        test("再次切换恢复", False, "按钮不存在")
except Exception as e:
    test("再次切换恢复", False, str(e))

# 输出结果
passed = sum(1 for r in results if r[1])
total = len(results)
print(f"\n{'='*60}")
print(f"语言切换黑盒测试: {passed}/{total} 通过")
print(f"{'='*60}")
for r in results:
    status = "✅" if r[1] else "❌"
    print(f"  {status} {r[0]}", f"({r[2]})" if len(r) > 2 and r[2] else "")

app.kill()
print(f"\n总计: {passed}/{total} 通过")
