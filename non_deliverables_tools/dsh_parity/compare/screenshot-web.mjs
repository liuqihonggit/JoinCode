/**
 * 本地 WebUI 页面截图（与 DSH 捕获同管线：CDP/playwright + 系统 Edge）
 *
 * 用法: node screenshot-web.mjs <pagePath|url> <outPng> [WxH] [theme]
 * 产物: outPng
 */
import { chromium } from 'playwright-core';
import { mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { findEdge, EDGE_LAUNCH_ARGS } from '../lib/edge.mjs';

const [pageArg, outArg, viewportArg = '1440x900', themeArg = 'dark'] = process.argv.slice(2);
if (!pageArg || !outArg) {
  console.error('usage: node screenshot-web.mjs <pagePath|url> <outPng> [WxH] [dark|light]');
  process.exit(1);
}
const vm = /^(\d+)x(\d+)$/.exec(viewportArg);
if (!vm) {
  console.error(`invalid viewport: ${viewportArg} (expect WxH, e.g. 1440x900)`);
  process.exit(1);
}
const [width, height] = [parseInt(vm[1], 10), parseInt(vm[2], 10)];

const url = /^https?:|^file:/.test(pageArg) ? pageArg : pathToFileURL(resolve(pageArg)).href;
const outPath = resolve(outArg);
mkdirSync(join(outPath, '..'), { recursive: true });

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

const shot = await page.screenshot({ path: outPath, fullPage: false });
console.log(`screenshot: ${outPath} (${shot.readUInt32BE(16)}x${shot.readUInt32BE(20)}, ${shot.length} bytes)`);

await browser.close();
console.log('done');
