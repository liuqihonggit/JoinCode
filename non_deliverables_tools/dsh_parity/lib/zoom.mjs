/**
 * golden 区域放大（人工核查用）
 * 用法: node zoom.mjs <srcPng> <outDir> <scale> <name:x,y,w,h> [name2:...]
 */
import { chromium } from 'playwright-core';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { findEdge, EDGE_LAUNCH_ARGS } from './edge.mjs';

const [srcArg, outDirArg, scaleArg = '4', ...regionArgs] = process.argv.slice(2);
if (!srcArg || !outDirArg || regionArgs.length === 0) {
  console.error('usage: node zoom.mjs <srcPng> <outDir> <scale> <name:x,y,w,h> [...]');
  process.exit(1);
}
const scale = parseInt(scaleArg, 10);
const src = resolve(srcArg);
const outDir = resolve(outDirArg);
mkdirSync(outDir, { recursive: true });

const browser = await chromium.launch({ executablePath: findEdge(), headless: true, args: [...EDGE_LAUNCH_ARGS, '--allow-file-access-from-files'] });
const page = await browser.newPage({ viewport: { width: 1440 * scale, height: 900 * scale } });
const html = `<html><body style="margin:0"><img src="${pathToFileURL(src).href}" style="width:1440px;height:900px;image-rendering:pixelated"></body></html>`;
const harness = join(outDir, 'zoom-harness.html');
writeFileSync(harness, html);
await page.goto(pathToFileURL(harness).href);
await page.waitForTimeout(400);
await page.evaluate((s) => {
  const img = document.querySelector('img');
  img.style.width = `${1440 * s}px`;
  img.style.height = `${900 * s}px`;
}, scale);
await page.waitForTimeout(300);

for (const spec of regionArgs) {
  const m = /^([^:]+):(\d+),(\d+),(\d+),(\d+)$/.exec(spec);
  if (!m) { console.error('bad region:', spec); process.exit(1); }
  const [, name, x, y, w, h] = m;
  const out = join(outDir, `zoom-${name}.png`);
  await page.screenshot({ path: out, clip: { x: +x * scale, y: +y * scale, width: +w * scale, height: +h * scale } });
  console.log(`zoom: ${out} (${w}x${h} @${scale}x)`);
}
await browser.close();
