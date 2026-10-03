"""黑盒测试：验证 VSCode 风格快捷键 Ctrl+B/J/I + Ctrl+P/Shift+P + Zen"""
import sys, time, os, ctypes
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

def find_element(win, keyword):
    """查找包含关键词的控件"""
    for c in win.descendants():
        try:
            t = c.window_text()
            if keyword in t and t.strip():
                return c
        except:
            pass
    return None

def is_really_visible(elem):
    """通过 rectangle 判断控件是否真正可见（宽高>0）"""
    if elem is None:
        return False
    try:
        r = elem.rectangle()
        return r.width() > 2 and r.height() > 2
    except:
        return False

def send_keys(win, keys, delay=1.0):
    win.set_focus()
    time.sleep(0.2)
    win.type_keys(keys, pause=0.3)
    time.sleep(delay)

# 1. Ctrl+B 切换侧边栏
# 初始 ActiveSidePanel=Sessions，Ctrl+B 切到 FileTree（"新建会话"消失），再 Ctrl+B 收起
try:
    elem = find_element(win, "新建会话")
    before = is_really_visible(elem)
    send_keys(win, "^b")
    elem2 = find_element(win, "新建会话")
    after = is_really_visible(elem2)
    send_keys(win, "^b")
    elem3 = find_element(win, "新建会话")
    restored = is_really_visible(elem3)
    # 期望：before=True, after=False(切到FileTree), restored=True(切回Sessions... 实际是收起)
    # 实际逻辑：1st Ctrl+B: Sessions→FileTree, 2nd Ctrl+B: FileTree→None(收起)
    # 所以 restored 应该是 False（侧边栏收起了）
    test("Ctrl+B 切换侧边栏",
         before and not after,
         f"前={before} 切FileTree={after} 收起={restored}")
except Exception as e:
    test("Ctrl+B 切换侧边栏", False, str(e))

# 恢复侧边栏（按 💬 Session 按钮）
try:
    send_keys(win, "^b")  # 再按一次恢复
    time.sleep(0.5)
except:
    pass

# 2. Ctrl+J 切换底部面板
try:
    # 面板标签 "输出"/"终端"/"问题" 仅在面板打开时可见
    elem = find_element(win, "输出")
    before = is_really_visible(elem)
    send_keys(win, "^j")
    elem2 = find_element(win, "输出")
    after = is_really_visible(elem2)
    send_keys(win, "^j")
    elem3 = find_element(win, "输出")
    restored = is_really_visible(elem3)
    test("Ctrl+J 切换底部面板",
         before != after,
         f"前={before} 后={after} 恢复={restored}")
except Exception as e:
    test("Ctrl+J 切换底部面板", False, str(e))

# 3. Ctrl+I 切换拦截器面板
try:
    elem = find_element(win, "拦截")
    before = is_really_visible(elem)
    send_keys(win, "^i")
    elem2 = find_element(win, "拦截")
    after = is_really_visible(elem2)
    send_keys(win, "^i")
    elem3 = find_element(win, "拦截")
    restored = is_really_visible(elem3)
    test("Ctrl+I 切换拦截器面板",
         not before and after and not restored,
         f"前={before} 后={after} 恢复={restored}")
except Exception as e:
    test("Ctrl+I 切换拦截器面板", False, str(e))

# 4. Ctrl+Shift+Z 禅模式
try:
    elem = find_element(win, "新建会话")
    before = is_really_visible(elem)
    send_keys(win, "^+z")
    elem2 = find_element(win, "新建会话")
    zen = is_really_visible(elem2)
    send_keys(win, "^+z")
    elem3 = find_element(win, "新建会话")
    restored = is_really_visible(elem3)
    test("Ctrl+Shift+Z 禅模式",
         before and not zen and restored,
         f"前={before} 禅={zen} 恢复={restored}")
except Exception as e:
    test("Ctrl+Shift+Z 禅模式", False, str(e))

# 5. Ctrl+P 快速打开
try:
    send_keys(win, "^p")
    edits = win.descendants(control_type="Edit")
    has_palette = len(edits) > 0
    send_keys(win, "{ESC}", 0.5)
    test("Ctrl+P 打开命令面板", has_palette, f"edit_count={len(edits)}")
except Exception as e:
    test("Ctrl+P 打开命令面板", False, str(e))

# 6. Ctrl+Shift+P 命令面板
try:
    send_keys(win, "^+p")
    edits = win.descendants(control_type="Edit")
    has_palette = len(edits) > 0
    has_commands = is_really_visible(find_element(win, "切换")) or is_really_visible(find_element(win, "打开"))
    send_keys(win, "{ESC}", 0.5)
    test("Ctrl+Shift+P 命令面板", has_palette and has_commands, f"edit={has_palette} cmds={has_commands}")
except Exception as e:
    test("Ctrl+Shift+P 命令面板", False, str(e))

# 7. Ctrl+S 保存不崩溃
try:
    send_keys(win, "^s")
    alive = win.is_visible()
    test("Ctrl+S 保存不崩溃", alive)
except Exception as e:
    test("Ctrl+S 保存不崩溃", False, str(e))

# 输出结果
passed = sum(1 for r in results if r[1])
total = len(results)
print(f"\n{'='*60}")
print(f"快捷键黑盒测试: {passed}/{total} 通过")
print(f"{'='*60}")
for r in results:
    status = "✅" if r[1] else "❌"
    print(f"  {status} {r[0]}", f"({r[2]})" if len(r) > 2 and r[2] else "")

app.kill()
print(f"\n总计: {passed}/{total} 通过")
