/**
 * 帧对比：golden vs 实际帧（Chromium canvas 像素 diff + 热力图）
 *
 * 用法: node diff-frames.mjs <goldenPng> <actualPng> <outDir>
 * 产物: <outDir>/diff-report.json + <outDir>/diff-heatmap.png（瞬时产物，入 dumps/）
 *
 * 指标：逐像素 max(|Δr|,|Δg|,|Δb|)，多阈值统计（4/12/24）+ 平均差
 * 热力图：实际帧降亮为底 + 超阈值像素标红
 */
import { chromium } from 'playwright-core';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { findEdge, EDGE_LAUNCH_ARGS } from '../lib/edge.mjs';

const [goldenArg, actualArg, outDirArg = 'dumps'] = process.argv.slice(2);
if (!goldenArg || !actualArg) {
  console.error('usage: node diff-frames.mjs <goldenPng> <actualPng> [outDir]');
  process.exit(1);
}
const golden = resolve(goldenArg);
const actual = resolve(actualArg);
const outDir = resolve(outDirArg);
mkdirSync(outDir, { recursive: true });

const fileUrl = (p) => pathToFileURL(p).href;
const harness = `<!doctype html>
<html><head><meta charset="utf-8"><style>body{margin:0}</style></head><body>
<img id="golden" src="${fileUrl(golden)}">
<img id="actual" src="${fileUrl(actual)}">
<canvas id="heatmap"></canvas>
</body></html>`;
const harnessPath = join(outDir, 'diff-harness.html');
writeFileSync(harnessPath, harness);

const browser = await chromium.launch({
  executablePath: findEdge(),
  headless: true,
  args: [...EDGE_LAUNCH_ARGS, '--allow-file-access-from-files'],
});
const page = await browser.newPage();
await page.goto(fileUrl(harnessPath), { waitUntil: 'load', timeout: 30000 });

const result = await page.evaluate(async () => {
  const ga = document.getElementById('golden');
  const ac = document.getElementById('actual');
  await Promise.all([ga.decode(), ac.decode()]);
  if (ga.naturalWidth !== ac.naturalWidth || ga.naturalHeight !== ac.naturalHeight) {
    return { error: `size mismatch: golden ${ga.naturalWidth}x${ga.naturalHeight} vs actual ${ac.naturalWidth}x${ac.naturalHeight}` };
  }
  const w = ga.naturalWidth, h = ga.naturalHeight;
  const draw = (img) => {
    const c = document.createElement('canvas');
    c.width = w; c.height = h;
    const ctx = c.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(img, 0, 0);
    return ctx.getImageData(0, 0, w, h).data;
  };
  const da = draw(ga), db = draw(ac);
  const stats = {
    width: w, height: h,
    totalPixels: w * h,
    meanDelta: 0,
    thresholds: { t4: 0, t12: 0, t24: 0 },
    pct: {},
  };
  const GRID_COLS = 12, GRID_ROWS = 10;
  const cw = Math.ceil(w / GRID_COLS), ch = Math.ceil(h / GRID_ROWS);
  const grid = Array.from({ length: GRID_COLS * GRID_ROWS }, () => 0);
  const heat = document.createElement('canvas');
  heat.width = w; heat.height = h;
  const hctx = heat.getContext('2d');
  const img = hctx.createImageData(w, h);
  let sum = 0;
  for (let i = 0; i < w * h; i++) {
    const o = i * 4;
    const dr = Math.abs(da[o] - db[o]);
    const dg = Math.abs(da[o + 1] - db[o + 1]);
    const dbv = Math.abs(da[o + 2] - db[o + 2]);
    const d = Math.max(dr, dg, dbv);
    sum += d;
    if (d > 3) stats.thresholds.t4++;
    if (d > 11) {
      stats.thresholds.t12++;
      const px = (i % w) / cw | 0, py = (i / w) / ch | 0;
      grid[py * GRID_COLS + px]++;
    }
    if (d > 23) stats.thresholds.t24++;
    if (d > 11) {
      img.data[o] = 255; img.data[o + 1] = 40; img.data[o + 2] = 40; img.data[o + 3] = 255;
    } else {
      img.data[o] = da[o] * 0.25; img.data[o + 1] = da[o + 1] * 0.25; img.data[o + 2] = da[o + 2] * 0.25; img.data[o + 3] = 255;
    }
  }
  stats.meanDelta = sum / (w * h);
  for (const [k, v] of Object.entries(stats.thresholds)) {
    stats.pct[k] = +(v / stats.totalPixels * 100).toFixed(3);
  }
  stats.topRegions = grid
    .map((count, gi) => ({
      x: (gi % GRID_COLS) * cw,
      y: ((gi / GRID_COLS) | 0) * ch,
      w: cw, h: ch, count,
    }))
    .filter((r) => r.count > 0)
    .sort((a, b) => b.count - a.count)
    .slice(0, 10);
  hctx.putImageData(img, 0, 0);
  return { stats, heatmap: heat.toDataURL('image/png') };
});

await browser.close();

if (result.error) {
  console.error(result.error);
  process.exit(1);
}

const heatBuf = Buffer.from(result.heatmap.split(',')[1], 'base64');
writeFileSync(join(outDir, 'diff-heatmap.png'), heatBuf);
const report = {
  golden: golden.replace(/\\/g, '/'),
  actual: actual.replace(/\\/g, '/'),
  ...result.stats,
  heatmap: 'diff-heatmap.png',
};
writeFileSync(join(outDir, 'diff-report.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
