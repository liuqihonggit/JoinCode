/**
 * token 生成器：dsh-tokens.json → tokens.css（CSS 自定义属性）
 *
 * 用法: node tokens2css.mjs [tokensJson] [outCss]
 * 默认: reference/dsh-tokens.json → ../../../modules/apps/gui/webui/styles/tokens.css
 *
 * 光/暗主题：亮色写 :root；暗色双通道 ——
 *   1) @media (prefers-color-scheme: dark) 且未显式标记 light（兜底，随 OS）
 *   2) :root[data-jcc-theme="dark"] 显式属性（宿主 app/init 下发，UI 主题的权威通道）
 * 禁止手改产物：tokens.css 头部有 AUTO-GENERATED 标记
 */
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDir = dirname(fileURLToPath(import.meta.url));
const tokensPath = resolve(process.argv[2] ?? join(scriptDir, '../reference/dsh-tokens.json'));
const outPath = resolve(process.argv[3] ?? join(scriptDir, '../../../modules/apps/gui/webui/styles/tokens.css'));

const tokens = JSON.parse(readFileSync(tokensPath, 'utf8'));

function block(map, indent = '  ') {
  return Object.keys(map)
    .sort()
    .map((name) => `${indent}${name}: ${map[name]};`)
    .join('\n');
}

const css = `/* AUTO-GENERATED from ${tokensPath.replace(/\\/g, '/').split('/reference/')[1] ?? 'dsh-tokens.json'} — 禁止手改（生成器: dsh_parity/generate/tokens2css.mjs） */
/* DSH token 数: ${tokens.count}（light=${Object.keys(tokens.light).length} / dark=${Object.keys(tokens.dark).length}） */

:root {
${block(tokens.light)}
}

/* 暗色通道 1：随 OS，但宿主显式标记 light 时不覆盖 */
@media (prefers-color-scheme: dark) {
  :root:not([data-jcc-theme="light"]) {
${block(tokens.dark, '    ')}
  }
}

/* 暗色通道 2：宿主显式属性（app/init 下发，权威通道） */
:root[data-jcc-theme="dark"] {
${block(tokens.dark)}
}
`;

mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, css);
console.log(`tokens.css: ${outPath} (light=${Object.keys(tokens.light).length}, dark=${Object.keys(tokens.dark).length})`);
