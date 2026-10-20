/**
 * 像素采样：打印 PNG 指定坐标颜色（人工核查 golden 用）
 * 用法: node sample.mjs <png> <x,y> [x,y ...]
 */
import { chromium } from 'playwright-core';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { findEdge, EDGE_LAUNCH_ARGS } from './edge.mjs';

const [srcArg, ...pairs] = process.argv.slice(2);
if (!srcArg || pairs.length === 0) {
  console.error('usage: node sample.mjs <png> <x,y> [x,y ...]');
  process.exit(1);
}
const src = resolve(srcArg);
const tmpDir = join(dirname(fileURLToPath(import.meta.url)), '..', '.tmp');
const tmp = join(tmpDir, 'sample-harness.html');
mkdirSync(tmpDir, { recursive: true });
writeFileSync(tmp, `<html><body style="margin:0"><img id="i" src="${pathToFileURL(src).href}"></body></html>`);

const browser = await chromium.launch({ executablePath: findEdge(), headless: true, args: [...EDGE_LAUNCH_ARGS, '--allow-file-access-from-files'] });
const page = await browser.newPage();
await page.goto(pathToFileURL(tmp).href);
const out = await page.evaluate(async (coords) => {
  const img = document.getElementById('i');
  await img.decode();
  const c = document.createElement('canvas');
  c.width = img.naturalWidth; c.height = img.naturalHeight;
  const ctx = c.getContext('2d', { willReadFrequently: true });
  ctx.drawImage(img, 0, 0);
  const d = ctx.getImageData(0, 0, c.width, c.height).data;
  return coords.map(([x, y]) => {
    const o = (y * c.width + x) * 4;
    return `${x},${y} = rgb(${d[o]}, ${d[o + 1]}, ${d[o + 2]})`;
  });
}, pairs.map((p) => p.split(',').map(Number)));
out.forEach((l) => console.log(l));
await browser.close();
