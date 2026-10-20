/**
 * DSH 会话内容态捕获 — 打开 fixture 会话（含 2 轮真实渲染消息），截屏 + DOM 盒树
 *
 * 用法: node capture-conversation-dsh.mjs <url> <outDir> [WxH] [theme]
 * 产出: dsh-web-messages-{W}x{H}.png / dsh-messages-dom-{W}x{H}.json
 *
 * 前置: fixture 会话位于独立 DSH_HOME（.dsh-home-messages，由 dumps/make-fixture-session.mjs 生成）
 */
import { chromium } from 'playwright-core';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { findEdge, EDGE_LAUNCH_ARGS } from '../lib/edge.mjs';
import { openDshSession } from '../lib/dsh_session.mjs';

const [url, outDir, viewportArg = '1440x900', themeArg = 'dark'] = process.argv.slice(2);
if (!url || !outDir) {
  console.error('usage: node capture-conversation-dsh.mjs <url> <outDir> [WxH] [dark|light]');
  process.exit(1);
}
const vm = /^(\d+)x(\d+)$/.exec(viewportArg);
if (!vm) {
  console.error(`invalid viewport: ${viewportArg}`);
  process.exit(1);
}
const [width, height] = [parseInt(vm[1], 10), parseInt(vm[2], 10)];

mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch({
  executablePath: findEdge(),
  headless: true,
  args: EDGE_LAUNCH_ARGS,
});
const { page } = await openDshSession(browser, { url, width, height, theme: themeArg });
console.log('title:', await page.title());

const MARK = '帮我梳理';
const sessionRows = page.locator('.YDXeBa_sessionRow');
const rowCount = await sessionRows.count();
console.log('sidebar session rows:', rowCount);
const texts = [];
for (let i = 0; i < rowCount; i++) texts.push(((await sessionRows.nth(i).textContent()) || '').slice(0, 80));
console.log('rows:', JSON.stringify(texts));

let opened = false;
for (let i = 0; i < rowCount; i++) {
  await sessionRows.nth(i).click({ timeout: 5000 }).catch(() => {});
  await page.waitForTimeout(1500);
  const has = await page.evaluate((m) => document.body.innerText.includes(m), MARK);
  if (has) { opened = true; console.log(`opened fixture row #${i}`); break; }
}
if (!opened) {
  console.error('fixture conversation not found; aborting');
  await browser.close();
  process.exit(2);
}

await page.waitForTimeout(2500);
const shotPath = join(outDir, `dsh-web-messages-${width}x${height}.png`);
const shot = await page.screenshot({ path: shotPath, fullPage: false });
console.log(`screenshot: ${shotPath} (${shot.readUInt32BE(16)}x${shot.readUInt32BE(20)}, ${shot.length} bytes)`);

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
          const sameTag = Array.from(parent.children).filter((c) => c.tagName === cur.tagName);
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
        text: (el.children.length === 0 ? el.textContent || '' : '').slice(0, 120),
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

const dom = await collectDom();
const domPath = join(outDir, `dsh-messages-dom-${width}x${height}.json`);
writeFileSync(domPath, JSON.stringify(dom, null, 1));
console.log(`dom: ${domPath} (${dom.length} elements)`);

// 组件 outerHTML 快照（图标路径/类语义精确复刻用）
const htmlDump = await page.evaluate(() => {
  const pick = (selector, all = false) => {
    if (all) return Array.from(document.querySelectorAll(selector)).map((el) => el.outerHTML);
    const el = document.querySelector(selector);
    return el ? el.outerHTML : null;
  };
  return {
    userRows: pick('.Sixlwa_userRow', true),
    userBubble: pick('.Sixlwa_bubble'),
    userActions: pick('.xzv4MW_actions'),
    assistantRoot: pick('.hWmORq_root'),
    assistantFooter: pick('.TS9iAW_root'),
    divider: pick('.l_V-RG_root'),
    codeBlock: pick('._block_7gxqk_4'),
    inlineCode: pick('li code'),
    boldStrong: pick('.hWmORq_root strong, .hWmORq_root b'),
    assistantParagraph: pick('.hWmORq_root ._markdown_1ypvv_5 p'),
    orderedList: pick('.hWmORq_root ol'),
  };
});
const htmlPath = join(outDir, `dsh-messages-html-${width}x${height}.json`);
writeFileSync(htmlPath, JSON.stringify(htmlDump, null, 1));
console.log(`html: ${htmlPath}`);

await browser.close();
console.log('done');
