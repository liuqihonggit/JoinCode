/**
 * DSH 参考物捕获 — CDP 驱动（playwright-core + 系统 Edge）
 *
 * 用法: node capture-dsh.mjs <url> <outDir> [WxH] [theme]
 * 产出: dsh-web-{W}x{H}.png / dsh-dom-{W}x{H}.json / dsh-tokens.json / dsh-meta.json
 *
 * 为什么不用 msedge --headless --screenshot --virtual-time-budget：
 * DSH UI 保持 WebSocket 长连接，虚拟时间永不推进，CLI 截图永久挂起。CDP 主动截屏才可靠。
 */
import { chromium } from 'playwright-core';
import { writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const [url, outDir, viewportArg = '1440x900', themeArg = 'dark'] = process.argv.slice(2);
if (!url || !outDir) {
  console.error('usage: node capture-dsh.mjs <url> <outDir> [WxH] [dark|light]');
  process.exit(1);
}
const vm = /^(\d+)x(\d+)$/.exec(viewportArg);
if (!vm) {
  console.error(`invalid viewport: ${viewportArg} (expect WxH, e.g. 1440x900)`);
  process.exit(1);
}
const [width, height] = [parseInt(vm[1], 10), parseInt(vm[2], 10)];

function findEdge() {
  const candidates = [
    process.env.DSH_PARITY_EDGE,
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  ].filter(Boolean);
  for (const p of candidates) if (existsSync(p)) return p;
  console.error('Edge not found. Set DSH_PARITY_EDGE env var to msedge.exe path.');
  process.exit(2);
}

mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch({
  executablePath: findEdge(),
  headless: true,
  args: ['--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1'],
});
const context = await browser.newContext({
  viewport: { width, height },
  deviceScaleFactor: 1,
  colorScheme: themeArg === 'light' ? 'light' : 'dark',
});
const page = await context.newPage();

console.log('navigating...');
await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
await page.waitForTimeout(3000);
try {
  await page.waitForLoadState('networkidle', { timeout: 12000 });
} catch {
  console.log('networkidle timeout (ws likely active), continuing');
}

// 首次运行引导弹窗（预览版说明）自动关闭，保证场景干净
for (let i = 0; i < 3; i++) {
  const btn = page.getByRole('button', { name: /继续|Continue|Got it|知道了|确定/ });
  if (await btn.first().isVisible().catch(() => false)) {
    console.log('dismissing onboarding modal');
    await btn.first().click().catch(() => {});
    await page.waitForTimeout(1500);
  } else break;
}
await page.waitForTimeout(2500);

const title = await page.title();
console.log('title:', title);

const shotPath = join(outDir, `dsh-web-${width}x${height}.png`);
const shot = await page.screenshot({ path: shotPath, fullPage: false });
console.log(`screenshot: ${shotPath} (${shot.readUInt32BE(16)}x${shot.readUInt32BE(20)}, ${shot.length} bytes)`);

/** 可见元素盒树：与 Avalonia VisualTree dump 同构（L2 几何对等的数据源） */
async function collectDom() {
  return await page.evaluate(() => {
    const out = [];
    const index = new Map();
    const attr = (el, name) => el.getAttribute(name) || undefined;
    const stablePath = (el) => {
      const parts = [];
      let cur = el;
      for (let depth = 0; cur && cur.nodeType === 1 && depth < 6; depth++) {
        let seg = cur.tagName.toLowerCase();
        const tid = cur.getAttribute('data-testid');
        if (tid) { parts.unshift(`${seg}[data-testid=${tid}]`); break; }
        const cls = typeof cur.className === 'string' && cur.className.trim()
          ? '.' + cur.className.trim().split(/\s+/).slice(0, 2).join('.')
          : '';
        const parent = cur.parentElement;
        if (parent) {
          const sameTag = Array.from(parent.children).filter(c => c.tagName === cur.tagName);
          if (sameTag.length > 1) seg += `:nth-of-type(${sameTag.indexOf(cur) + 1})`;
        }
        parts.unshift(seg + cls);
        cur = parent;
      }
      return parts.join(' > ');
    };
    for (const el of document.querySelectorAll('*')) {
      const r = el.getBoundingClientRect();
      if (r.width < 0.5 || r.height < 0.5) continue;
      const cs = getComputedStyle(el);
      if (cs.visibility === 'hidden' || cs.display === 'none' || parseFloat(cs.opacity) === 0) continue;
      const i = out.length;
      index.set(el, i);
      out.push({
        i,
        parent: el.parentElement ? (index.get(el.parentElement) ?? -1) : -1,
        tag: el.tagName.toLowerCase(),
        cls: (typeof el.className === 'string' ? el.className : '').slice(0, 160),
        testid: attr(el, 'data-testid'),
        role: attr(el, 'role'),
        aria: attr(el, 'aria-label'),
        path: stablePath(el),
        x: Math.round(r.x * 10) / 10,
        y: Math.round(r.y * 10) / 10,
        w: Math.round(r.width * 10) / 10,
        h: Math.round(r.height * 10) / 10,
        bg: cs.backgroundColor,
        color: cs.color,
        fs: cs.fontSize,
        fw: cs.fontWeight,
        ff: cs.fontFamily.slice(0, 80),
        lh: cs.lineHeight,
        br: cs.borderRadius,
        pad: cs.padding,
        mar: cs.margin,
        gap: cs.gap,
        disp: cs.display,
        fd: cs.flexDirection,
        ov: cs.overflow,
      });
      if (out.length >= 5000) break;
    }
    return out;
  });
}

/** 全部 CSS 自定义属性（--dsw-* / --ds-*）双主题解析值 */
async function collectTokens() {
  return await page.evaluate(() => {
    const names = new Set();
    for (const sheet of document.styleSheets) {
      let rules;
      try { rules = sheet.cssRules; } catch { continue; }
      if (!rules) continue;
      for (const rule of rules) {
        const text = rule.cssText || '';
        const m = text.match(/--(?:dsw|ds)-[a-z0-9-]+/g);
        if (m) m.forEach(n => names.add(n));
      }
    }
    const read = (target) => {
      const cs = getComputedStyle(target);
      const map = {};
      for (const n of names) {
        const v = cs.getPropertyValue(n).trim();
        if (v) map[n] = v;
      }
      return map;
    };
    document.documentElement.removeAttribute('data-ds-dark-theme');
    document.body.removeAttribute('data-ds-dark-theme');
    const light = read(document.body);
    document.body.setAttribute('data-ds-dark-theme', '');
    const dark = read(document.body);
    document.body.removeAttribute('data-ds-dark-theme');
    return { count: names.size, light, dark };
  });
}

const dom = await collectDom();
const domPath = join(outDir, `dsh-dom-${width}x${height}.json`);
writeFileSync(domPath, JSON.stringify(dom, null, 1));
console.log(`dom: ${domPath} (${dom.length} elements)`);

const tokens = await collectTokens();
const tokenPath = join(outDir, 'dsh-tokens.json');
writeFileSync(tokenPath, JSON.stringify(tokens, null, 1));
console.log(`tokens: ${tokenPath} (${tokens.count} names, light=${Object.keys(tokens.light).length}, dark=${Object.keys(tokens.dark).length})`);

writeFileSync(join(outDir, 'dsh-meta.json'), JSON.stringify({
  dshVersion: '0.2.0-rc.2',
  url: url.replace(/token=[^&]+/, 'token=REDACTED'),
  viewport: { width, height },
  deviceScaleFactor: 1,
  theme: themeArg,
  title,
  capturedAt: new Date().toISOString(),
}, null, 2));

await browser.close();
console.log('done');
