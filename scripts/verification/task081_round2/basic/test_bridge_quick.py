#!/usr/bin/env python3
"""验证 bridge_serve 批量模式可用性"""
import subprocess, os, time, json, threading, urllib.request, uuid
from pathlib import Path

env = {**os.environ, 'JCC_ENDPOINT':'http://localhost:9901/v1', 'JCC_VENDOR':'openai', 'JCC_MODEL_ID':'gpt-5.6-sol', 'OPENAI_API_KEY':'test-key', 'JCC_SKIP_MODEL_FETCH':'1', 'JCC_DISABLE_RETRY':'1'}
JCC = 'artifacts/bin/JoinCode/Debug/net10.0/jcc.exe'
MOCK_EXE = 'artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe'

mock = subprocess.Popen([MOCK_EXE, '--port', '9901', '--interactive', '--no-open-browser'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
for _ in range(10):
    time.sleep(1)
    try: urllib.request.urlopen('http://localhost:9901/__control__/state', timeout=3); break
    except: pass

def inject(text):
    urllib.request.urlopen(urllib.request.Request('http://localhost:9901/__control__/clear', data=b'', method='POST'), timeout=3)
    cid = f'chatcmpl-{uuid.uuid4().hex[:20]}'
    chunks = [
        f'data: {json.dumps({"id":cid,"object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant"}}]})}\n\n',
        f'data: {json.dumps({"id":cid,"choices":[{"index":0,"delta":{"content":text}}]})}\n\n',
        f'data: {json.dumps({"id":cid,"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]})}\n\n',
        'data: [DONE]\n\n',
    ]
    body = json.dumps({'status':200,'content_type':'text/event-stream','is_stream':True,'stream_chunks':chunks})
    urllib.request.urlopen(urllib.request.Request('http://localhost:9901/__control__/inject', data=body.encode(), method='POST', headers={'Content-Type':'application/json'}), timeout=3)

t0 = time.time()
proc = subprocess.Popen([JCC, 'bridge_serve', '--trust', '--permission-mode', 'bypass'], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0)

events = []
def reader():
    for line in proc.stdout:
        line = line.decode('utf-8', errors='replace').strip()
        if line:
            try: events.append(json.loads(line))
            except: pass

t = threading.Thread(target=reader, daemon=True)
t.start()

time.sleep(4)
print(f'[启动] {time.time()-t0:.2f}s, 事件数: {len(events)}')
for e in events[:3]:
    print(f'  事件: type={e.get("type")}')

inject('你好')
proc.stdin.write((json.dumps({'type':'submit','text':'你好','seq':1}) + '\n').encode())
proc.stdin.flush()

deadline = time.time() + 10
while time.time() < deadline:
    if any(e.get('type') == 'turn_completed' for e in events):
        break
    time.sleep(0.2)

print(f'[提交后] 事件数: {len(events)}')
for e in events:
    et = e.get('type')
    if et in ('assistant_message','turn_completed','error'):
        print(f'  {et}: {str(e)[:200]}')

inject('第二个问题')
proc.stdin.write((json.dumps({'type':'submit','text':'第二个问题','seq':2}) + '\n').encode())
proc.stdin.flush()

deadline = time.time() + 10
while time.time() < deadline:
    if sum(1 for e in events if e.get('type') == 'turn_completed') >= 2:
        break
    time.sleep(0.2)

print(f'[第二轮] 事件数: {len(events)}')
for e in events:
    et = e.get('type')
    if et == 'assistant_message':
        print(f'  {et}: {str(e)[:200]}')

proc.stdin.write((json.dumps({'type':'quit'}) + '\n').encode())
proc.stdin.flush()
proc.wait(timeout=5)
print(f'[总耗时] {time.time()-t0:.2f}s (2轮对话, 1次启动)')
mock.kill()
