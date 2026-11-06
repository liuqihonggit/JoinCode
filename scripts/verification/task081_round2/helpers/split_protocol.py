from pathlib import Path

root = Path(r"D:/project/w3/native_gui/src/NativeGui.Core/web_host")
src = root / "DshRpcProtocol.cs"
dst = root / "DshInteractionProtocol.cs"

lines = src.read_text(encoding="utf-8").splitlines(keepends=True)

def block(start, end):
    return "".join(lines[start - 1:end])

header = (
    "namespace JoinCode.Gui.WebHost;\n"
    "\n"
    "// 交互类 DSH 协议 DTO（TASK066 C4）：代理预设、会话搜索、工作区路径打开、settings 替换/补丁、消息反馈。\n"
    "// JsonSerializable 注册仍集中在 DshRpcProtocol.cs 的 DshJsonContext。\n"
    "\n"
)

moved = block(1325, 1424) + "\n" + block(1464, 1482) + "\n" + block(1607, 1790)
dst.write_text(header + moved, encoding="utf-8")

kept = "".join(lines[:1324] + lines[1424:1463] + lines[1482:1606] + lines[1790:])
src.write_text(kept, encoding="utf-8")

print("src lines:", len(kept.splitlines()))
print("dst lines:", len((header + moved).splitlines()))
