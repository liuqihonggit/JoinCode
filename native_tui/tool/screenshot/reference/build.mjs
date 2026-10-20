import {build} from 'esbuild';
import {createRequire} from 'node:module';
import {resolve} from 'node:path';
const require = createRequire(import.meta.url);
const sourceRoot = process.argv[2];
if (!sourceRoot) throw new Error('Usage: node build.mjs <claude-code-rev-main/src>');
const source = resolve(sourceRoot, 'components/LogoV2/Clawd.tsx');
await build({entryPoints:['reference.tsx'], outfile:'reference.mjs', platform:'node', format:'esm', bundle:true, packages:'external', plugins:[{name:'reference-only',setup(build){
build.onResolve({filter:/^reference-clawd$/},()=>({path:source}));
build.onResolve({filter:/\/ink\.js$/},()=>({path:resolve('bridge.tsx')}));
build.onResolve({filter:/\/utils\/env\.js$/},()=>({path:resolve('env.ts')}));
build.onResolve({filter:/^react(?:\/compiler-runtime)?$/},args=>({path:args.path, external:true}));
}}]});
