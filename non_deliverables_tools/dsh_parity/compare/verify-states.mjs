/**
 * 本地 WebUI 交互态验证 — 对照 reference/dsh-states.json（DSH golden）
 *
 * 用法: node verify-states.mjs [pagePath] [statesJson] [WxH] [theme]
 * 判定: 对 DSH 中发生变化的每个属性，本地 hover/active/focus 值必须与 DSH 完全一致；
 *       本地额外变化（DSH 无变化）记为 WARN。
 * 退出码: 0 全部通过 / 1 存在不一致
 */
import { chromium } from 'playwright-core';
import { readFileSync, mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { findEdge, EDGE_LAUNCH_ARGS } from '../lib/edge.mjs';
import { STATE_TARGETS, STATE_PROPS, NEUTRAL_POINT } from '../capture/state-targets.mjs';

const [pageArg = '../../modules/apps/gui/webui/index.html', statesArg = '../reference/dsh-states.json', viewportArg = '', themeArg = 'dark'] = process.argv.slice(2);
const golden = JSON.parse(readFileSync(resolve(statesArg), 'utf8'));
const width = parseInt(viewportArg, 10) || golden.viewport?.width || 1440;
const height = viewportArg ? parseInt(viewportArg.split('x')[1], 10) : (golden.viewport?.height || 900);

const url = /^https?:|^file:/.test(pageArg) ? pageArg : pathToFileURL(resolve(pageArg)).href;
const browser = await chromium.launch({
  executablePath: findEdge(),
  headless: true,
  args: EDGE_LAUNCH_ARGS,
});
const context = await browser.newContext({
  viewport: { width, height },
  deviceScaleFactor: 1,
  colorScheme: themeArg === 'light' ? 'light' : 'dark',
});
const page = await context.newPage();
console.log('navigating:', url);
await page.goto(url, { waitUntil: 'load', timeout: 30000 });
await page.waitForTimeout(800);

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

const theirs = {};
for (const t of STATE_TARGETS) {
  const entry = { states: {} };
  const loc = page.locator(t.ours).first();
  try {
    await resetPointer();
    if ((await page.locator(t.ours).count()) === 0) {
      entry.missing = true;
      theirs[t.name] = entry;
      continue;
    }
    entry.states.base = await readStyle(t.ours);

    const hoverLoc = t.hoverViaOurs ? page.locator(t.hoverViaOurs).first() : loc;
    await hoverLoc.hover({ timeout: 3000 });
    await page.waitForTimeout(450);
    entry.states.hover = await readStyle(t.ours);

    const box = await loc.boundingBox();
    if (box && !(await loc.isDisabled().catch(() => false))) {
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      await page.mouse.down();
      await page.waitForTimeout(150);
      entry.states.active = await readStyle(t.ours);
      await page.mouse.move(NEUTRAL_POINT.x, NEUTRAL_POINT.y);
      await page.mouse.up();
    } else if (box) {
      entry.disabled = true;
    }
  } catch (e) {
    entry.error = String(e).slice(0, 200);
  }
  theirs[t.name] = entry;
}

await resetPointer();
for (const t of STATE_TARGETS.filter(x => x.focus)) {
  const g = golden.targets[t.name];
  if (!g || !g.states?.focus) continue;
  const entry = theirs[t.name];
  if (!entry || entry.missing) continue;
  const clickTarget = t.clickVia || t.hoverViaOurs;
  const clickLoc = clickTarget ? page.locator(clickTarget).first() : page.locator(t.ours).first();
  try {
    await clickLoc.click({ timeout: 3000 });
    await page.waitForTimeout(300);
    entry.states.focus = await readStyle(t.ours);
  } catch (e) {
    entry.focusError = String(e).slice(0, 200);
  }
  await resetPointer();
}

let failures = 0;
let warnings = 0;
for (const t of STATE_TARGETS) {
  const g = golden.targets[t.name];
  const o = theirs[t.name];
  if (!g || g.missing) continue;
  if (!o || o.missing) {
    console.log(`FAIL [${t.name}] 本地选择器未命中: ${t.ours}`);
    failures++;
    continue;
  }
  const gBase = g.states?.base || {};
  for (const state of ['hover', 'active', 'focus']) {
    const gState = g.states?.[state];
    if (!gState) continue;
    const required = Object.entries(gState).filter(([k, v]) => gBase[k] !== v);
    const oState = o.states?.[state];
    if (!oState) {
      if (required.length) {
        console.log(`FAIL [${t.name}/${state}] 本地无该状态采样，golden 要求 ${required.length} 项变化`);
        failures++;
      }
      continue;
    }
    for (const [k, expected] of required) {
      const actual = oState[k];
      if (actual !== expected) {
        console.log(`FAIL [${t.name}/${state}] ${k}: golden='${expected}' 本地='${actual}'`);
        failures++;
      }
    }
    const oBase = o.states?.base || {};
    const extra = Object.entries(oState).filter(([k, v]) => oBase[k] !== v && gBase[k] === (gState[k] ?? gBase[k]));
    for (const [k, v] of extra) {
      console.log(`WARN [${t.name}/${state}] ${k} 本地额外变化 -> '${v}'（DSH 不变）`);
      warnings++;
    }
  }
}

mkdirSync('dumps', { recursive: true });
const { writeFileSync } = await import('node:fs');
writeFileSync(join('dumps', 'states-report.json'), JSON.stringify({ golden: golden.capturedAt, failures, warnings, theirs }, null, 1));
console.log(`\n交互态验证: ${failures === 0 ? 'PASS' : 'FAIL'}（${failures} 不一致 / ${warnings} 警告）report -> dumps/states-report.json`);

await browser.close();
process.exit(failures === 0 ? 0 : 1);
