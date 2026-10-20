/**
 * DSH 交互态捕获 — hover / active / focus 计算样式 golden
 *
 * 用法: node capture-states-dsh.mjs <url> <outDir> [WxH] [theme]
 * 产出: dsh-states.json（与 capture-dsh.mjs 同会话条件：CDP/Edge/固定视口/深色主题）
 *
 * 采集约定：
 *  - base: 中性指针位 + 失焦后读取
 *  - hover: locator.hover() 后等待 450ms（transition 收敛）
 *  - active: mousedown 保持按压读取，随后位移出元素再松开（避免触发 click 导航）
 *  - focus: 点击目标后读取；对可能导航/开面板的目标读完后 reload 复位
 */
import { chromium } from 'playwright-core';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { findEdge, EDGE_LAUNCH_ARGS } from '../lib/edge.mjs';
import { openDshSession, settleDsh } from '../lib/dsh_session.mjs';
import { STATE_TARGETS, STATE_PROPS, NEUTRAL_POINT } from './state-targets.mjs';

const [url, outDir, viewportArg = '1440x900', themeArg = 'dark'] = process.argv.slice(2);
if (!url || !outDir) {
  console.error('usage: node capture-states-dsh.mjs <url> <outDir> [WxH] [dark|light]');
  process.exit(1);
}
const vm = /^(\d+)x(\d+)$/.exec(viewportArg);
if (!vm) {
  console.error(`invalid viewport: ${viewportArg} (expect WxH)`);
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
const title = await page.title();
console.log('title:', title);

const readStyle = (sel) => page.evaluate(({ sel, props }) => {
  const el = document.querySelector(sel);
  if (!el) return null;
  const cs = getComputedStyle(el);
  const out = {};
  for (const p of props) out[p] = cs[p];
  return out;
}, { sel, props: STATE_PROPS });

const resetPointer = async () => {
  await page.mouse.move(NEUTRAL_POINT.x, NEUTRAL_POINT.y);
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  await page.waitForTimeout(450);
};

const results = {
  capturedAt: new Date().toISOString(),
  dshVersion: '0.2.0-rc.2',
  viewport: { width, height },
  theme: themeArg,
  title,
  targets: {},
};

for (const t of STATE_TARGETS) {
  const entry = { dsh: t.dsh, states: {} };
  const loc = page.locator(t.dsh).first();
  try {
    await resetPointer();
    if ((await page.locator(t.dsh).count()) === 0) {
      entry.missing = true;
      console.log(`[${t.name}] MISSING: ${t.dsh}`);
      results.targets[t.name] = entry;
      continue;
    }
    await loc.scrollIntoViewIfNeeded().catch(() => {});
    entry.states.base = await readStyle(t.dsh);

    const hoverLoc = t.hoverVia ? page.locator(t.hoverVia).first() : loc;
    await hoverLoc.hover({ timeout: 3000 });
    await page.waitForTimeout(450);
    entry.states.hover = await readStyle(t.dsh);

    const box = await loc.boundingBox();
    if (box && !(await loc.isDisabled().catch(() => false))) {
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      await page.mouse.down();
      await page.waitForTimeout(150);
      entry.states.active = await readStyle(t.dsh);
      await page.mouse.move(NEUTRAL_POINT.x, NEUTRAL_POINT.y);
      await page.mouse.up();
    } else if (box) {
      entry.disabled = true;
      console.log(`[${t.name}] disabled, active skipped`);
    }
    console.log(`[${t.name}] base/hover/active captured`);
  } catch (e) {
    entry.error = String(e).slice(0, 200);
    console.error(`[${t.name}] ERROR: ${entry.error}`);
  }
  results.targets[t.name] = entry;
}

await resetPointer();
for (const t of STATE_TARGETS.filter(x => x.focus)) {
  const entry = results.targets[t.name];
  if (!entry || entry.missing) continue;
  const loc = page.locator(t.dsh).first();
  const clickTarget = t.clickVia || t.hoverVia;
  const clickLoc = clickTarget ? page.locator(clickTarget).first() : loc;
  try {
    await clickLoc.click({ timeout: 3000 });
    await page.waitForTimeout(300);
    entry.states.focus = await readStyle(t.dsh);
    console.log(`[${t.name}] focus captured`);
  } catch (e) {
    entry.focusError = String(e).slice(0, 200);
    console.error(`[${t.name}] FOCUS ERROR: ${entry.focusError}`);
  }
  if (t.reloadAfterClick) {
    await page.reload({ waitUntil: 'domcontentloaded' });
    await settleDsh(page);
  }
  await resetPointer();
}

const outPath = join(outDir, 'dsh-states.json');
writeFileSync(outPath, JSON.stringify(results, null, 1));
console.log(`states: ${outPath} (${Object.keys(results.targets).length} targets)`);

await browser.close();
console.log('done');
