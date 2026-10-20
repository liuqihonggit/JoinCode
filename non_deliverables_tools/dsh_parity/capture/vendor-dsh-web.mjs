/**
 * DSH 前端整体快照（供 JoinCode C# 宿主原样托管）
 *
 * 用法: node vendor-dsh-web.mjs <authenticated-url> <outDir> [WxH]
 * 产出:
 *   www/                  dsh-web-frontend/dist 全量 + plugins/<包名>/<bundle>.js
 *   index.html            宿主注入行之后的首页（原样，含 __DSH_BOOT__ 与 combo 引用）
 *   boot.json             window.__DSH_BOOT__（客户端插件图，原样）
 *   transport.json        window.__DSH_TRANSPORT__
 *   protocol/http.jsonl   /api/* 请求与响应（协议探针：方法名/载荷样本）
 *   protocol/ws.jsonl     WebSocket 双向帧（协议探针）
 *   meta.json             版本、URL(脱敏)、统计
 *
 * 说明:
 *   - 页面 bundle 直接从 node_modules 复制（与 HTTP combo 响应同源同内容），不靠抓包，
 *     避免 event-stream / 长连接导致 body 读取挂起。
 *   - C# 宿主只需实现 combo 路由 `plugins/??a,b&rev=x`（按序拼接）+ 静态文件 + /api。
 */
import { chromium } from 'playwright-core';
import { cpSync, existsSync, mkdirSync, readdirSync, renameSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { findEdge, EDGE_LAUNCH_ARGS } from '../lib/edge.mjs';
import { settleDsh } from '../lib/dsh_session.mjs';

const [url, outDir, viewportArg = '1440x900'] = process.argv.slice(2);
if (!url || !outDir) {
  console.error('usage: node vendor-dsh-web.mjs <url> <outDir> [WxH]');
  process.exit(1);
}
const vm = /^(\d+)x(\d+)$/.exec(viewportArg);
if (!vm) {
  console.error(`invalid viewport: ${viewportArg}`);
  process.exit(1);
}
const [width, height] = [parseInt(vm[1], 10), parseInt(vm[2], 10)];
const origin = new URL(url).origin;

const toolRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const modulesRoot = join(toolRoot, 'node_modules');
const distRoot = join(modulesRoot, '@deepseek-ai', 'dsh-web-frontend', 'dist');
if (!existsSync(join(distRoot, 'index.html'))) {
  console.error(`dist not found: ${distRoot}`);
  process.exit(1);
}

mkdirSync(join(outDir, 'www'), { recursive: true });
mkdirSync(join(outDir, 'protocol'), { recursive: true });

const httpTrace = [];
const wsTrace = [];
const failures = [];

function withTimeout(promise, ms, label) {
  return Promise.race([
    promise,
    new Promise((_, reject) => setTimeout(() => reject(new Error(`timeout ${label}`)), ms)),
  ]);
}

function redactHeaders(headers) {
  const out = { ...headers };
  for (const key of Object.keys(out)) {
    if (/cookie|authorization|token/i.test(key)) out[key] = 'REDACTED';
  }
  return out;
}

const browser = await chromium.launch({
  executablePath: findEdge(),
  headless: true,
  args: EDGE_LAUNCH_ARGS,
});
const context = await browser.newContext({
  viewport: { width, height },
  deviceScaleFactor: 1,
  colorScheme: 'dark',
});
const page = await context.newPage();

page.on('request', (request) => {
  const u = new URL(request.url());
  if (u.origin !== origin || !u.pathname.startsWith('/api')) return;
  const entry = {
    method: request.method(),
    path: u.pathname + u.search,
    requestHeaders: redactHeaders(request.headers()),
    requestBody: (request.postData() ?? '').slice(0, 200000),
  };
  request.__trace = entry;
  httpTrace.push(entry);
});

page.on('response', (response) => {
  const entry = response.request().__trace;
  if (entry === undefined) return;
  entry.status = response.status();
  response.allHeaders().then((headers) => {
    entry.responseHeaders = redactHeaders(headers);
    const contentType = headers['content-type'] ?? '';
    if (contentType.includes('text/event-stream')) {
      entry.responseBody = '<stream>';
      return;
    }
    withTimeout(response.text(), 5000, 'api-body')
      .then((body) => { entry.responseBody = body.slice(0, 200000); })
      .catch((error) => { entry.responseBody = `<unreadable: ${error.message}>`; });
  }).catch(() => {});
});

page.on('websocket', (ws) => {
  const urlPath = ws.url().replace(origin, '');
  ws.on('framesent', (frame) => {
    wsTrace.push({ dir: 'send', path: urlPath, bytes: frame.payload.length, payload: String(frame.payload).slice(0, 12000) });
  });
  ws.on('framereceived', (frame) => {
    wsTrace.push({ dir: 'recv', path: urlPath, bytes: frame.payload.length, payload: String(frame.payload).slice(0, 12000) });
  });
});

console.log('navigating...');
await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
await settleDsh(page);

const boot = await page.evaluate(() => window.__DSH_BOOT__ ?? null).catch(() => null);
const transport = await page.evaluate(() => window.__DSH_TRANSPORT__ ?? null).catch(() => null);

let indexHtml = '';
try {
  const response = await context.request.get(`${origin}/`, { timeout: 20000 });
  indexHtml = await response.text();
} catch (error) {
  failures.push(`index fetch: ${error instanceof Error ? error.message : String(error)}`);
}

cpSync(distRoot, join(outDir, 'www'), { recursive: true });

const entryIds = (boot?.entries ?? []).map((entry) => entry.id);
const copied = [];
for (const id of entryIds) {
  const libDir = join(modulesRoot, id, 'lib');
  if (!existsSync(libDir)) {
    failures.push(`missing package: ${id}`);
    continue;
  }
  const targetDir = join(outDir, 'www', 'plugins', id);
  if (existsSync(targetDir) && !statSync(targetDir).isDirectory()) {
    renameSync(targetDir, `${targetDir}.stale-${Date.now()}`);
  }
  mkdirSync(targetDir, { recursive: true });
  for (const file of readdirSync(libDir)) {
    if (!file.endsWith('.js')) continue;
    cpSync(join(libDir, file), join(targetDir, file));
    copied.push(`${id}/lib/${file}`);
  }
}

writeFileSync(join(outDir, 'index.html'), indexHtml);
writeFileSync(join(outDir, 'boot.json'), JSON.stringify(boot, null, 1));
writeFileSync(join(outDir, 'transport.json'), JSON.stringify(transport, null, 1));
await page.waitForTimeout(1500);
writeFileSync(join(outDir, 'protocol', 'http.jsonl'), httpTrace.map((r) => JSON.stringify(r)).join('\n'));
writeFileSync(join(outDir, 'protocol', 'ws.jsonl'), wsTrace.map((r) => JSON.stringify(r)).join('\n'));

const apiPaths = [...new Set(httpTrace.map((r) => r.path.split('?')[0]))].sort();
writeFileSync(join(outDir, 'meta.json'), JSON.stringify({
  dshVersion: '0.2.0-rc.2',
  url: url.replace(/token=[^&]+/, 'token=REDACTED'),
  viewport: { width, height },
  capturedAt: new Date().toISOString(),
  bootEntries: boot?.entries?.length ?? 0,
  bootBatches: boot?.batches?.length ?? 0,
  pluginFiles: copied.length,
  apiPaths,
  wsFrames: wsTrace.length,
  failures,
}, null, 2));

await browser.close();

console.log(`boot entries=${boot?.entries?.length ?? 0} batches=${boot?.batches?.length ?? 0}`);
console.log(`plugin files=${copied.length}`);
console.log(`api paths=${apiPaths.length}: ${apiPaths.join(', ')}`);
console.log(`ws frames=${wsTrace.length} (send=${wsTrace.filter((f) => f.dir === 'send').length})`);
if (failures.length > 0) console.log(`failures=${failures.length}: ${failures.slice(0, 5).join(' | ')}`);
