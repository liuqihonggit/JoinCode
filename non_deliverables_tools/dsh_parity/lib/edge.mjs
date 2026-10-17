/**
 * 系统 Edge 路径探测（CDP 驱动公共模块）
 * 环境变量 DSH_PARITY_EDGE 可覆盖。
 */
import { existsSync } from 'node:fs';

export function findEdge() {
  const candidates = [
    process.env.DSH_PARITY_EDGE,
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  ].filter(Boolean);
  for (const p of candidates) if (existsSync(p)) return p;
  console.error('Edge not found. Set DSH_PARITY_EDGE env var to msedge.exe path.');
  process.exit(2);
}

export const EDGE_LAUNCH_ARGS = ['--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1'];
